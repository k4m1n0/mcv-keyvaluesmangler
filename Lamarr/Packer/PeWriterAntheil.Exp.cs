namespace Lamarr.Packer;

using A0;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal partial class PeWriterAntheil
{
    private static readonly string[] rgExpFields = { "uLK0A", "uLK0B", "uLK1A", "uLK1B", "uHs" };//改名后按名取就晚了 这些输入在LoadBoot按原名一次采齐
    private uint uExpK0, uExpK1, uExpHs;
    private uint uExpTokAd, uExpTokX1, uExpTokX3, uExpTokX6;
    private uint uKillTok;//负例kill旋钮 名字要在改名之前解析
    private uint[]? rgSlotBaked;//第一份块写下的真值 供第二份块复用

    private void CaptureExpInputs(byte[] rgD)//常量对改写保持异或不变 改名前读到的密钥仍有效
    {
        var rgTok = FieldTokens(rgD, rgExpFields);
        foreach (var sName in rgExpFields) if (!rgTok.ContainsKey(sName)) throw new InvalidOperationException($"boot field missing: {sName}");
        uExpK0 = CctorImm(rgD, rgTok["uLK0A"]) ^ CctorImm(rgD, rgTok["uLK0B"]);
        uExpK1 = CctorImm(rgD, rgTok["uLK1A"]) ^ CctorImm(rgD, rgTok["uLK1B"]);
        uExpHs = CctorImm(rgD, rgTok["uHs"]);
        uExpTokAd = MethodToken(rgD, "AD");
        uExpTokX1 = MethodToken(rgD, "X1");
        uExpTokX3 = MethodToken(rgD, "X3");
        uExpTokX6 = MethodToken(rgD, "X6");
        //kill旋钮的名字只能在改名之前解析成token
        uKillTok = 0;
#if LAMARR_TRIAGE
        string sKill = Environment.GetEnvironmentVariable("LAMARR_IL_KILL") ?? "";
        if (sKill.Length > 0) uKillTok = MethodToken(rgD, sKill);
#endif
        //发行构建里没有IL_KILL这条路径uKillTok恒0
        //噪声表逐产物随机化 必须在改名之前写 体摘要口径与名字解析都依赖这个顺序
        WriteNoiseTable(rgD);
        KillNoiseBody(rgD);
    }

    private static readonly string[] rgNoiseBodies = { "NZ1", "NZ2", "NZ3", "NZ4", "NZ5", "NZ6", "NZ7", "NZ8", "NR1", "NR2" };
    private const byte NoiseBodySentinel = 0xC3;
    private const byte NoiseExpSentinel = 0x3C;

    //把10个噪声体逐产物随机置换到10个槽 真条目NR1与NR2的期望摘要写进它们所在的槽 其余槽写0
    private void WriteNoiseTable(byte[] rgD)
    {
        int iN = rgNoiseBodies.Length;
        var rgExp = new uint[iN];
        for (int i = 0; i < iN; i++)
        {
            uint uTok = MethodToken(rgD, rgNoiseBodies[i]);
            rgExp[i] = uExpHs ^ IlHash(rgD, uTok, uExpK0, uExpK1);
            if (rgExp[i] == 0) throw new InvalidOperationException($"noise body expectation resolved to zero: {rgNoiseBodies[i]}");
        }
        var rgSlotBody = new byte[iN];
        for (int i = 0; i < iN; i++) rgSlotBody[i] = (byte)i;
        var rnd = NewRng(0x2Bu);//逐产物 由运行种子与产物头材料派生 不依赖时钟
        for (int i = iN - 1; i > 0; i--) { int j = rnd.Next(i + 1); (rgSlotBody[i], rgSlotBody[j]) = (rgSlotBody[j], rgSlotBody[i]); }
        var rgSlotExp = new uint[iN];
        var rgReal = new List<int>();
        for (int i = 0; i < iN; i++)
        {
            int iBody = rgSlotBody[i];
            if (iBody == 8 || iBody == 9) { rgSlotExp[i] = rgExp[iBody]; rgReal.Add(i); }//真条目槽
        }
        if (rgReal.Count != 2) throw new InvalidOperationException($"noise table: expected 2 real slots, got {rgReal.Count}");
        FoldSealSlot(rgSlotExp);//封印折 期望值槽要先折过boot才认 切掉产物就过不了自检
        int iAtB = FindSentinelRun(rgD, NoiseBodySentinel, iN, "noise body table");
        int iAtE = FindSentinelRun(rgD, NoiseExpSentinel, iN * 4, "noise expectation table");
        for (int i = 0; i < iN; i++) rgD[iAtB + i] = rgSlotBody[i];
        for (int i = 0; i < iN; i++) BitConverter.GetBytes(rgSlotExp[i]).CopyTo(rgD, iAtE + i * 4);
        for (int i = 0; i < iN; i++) if (rgD[iAtB + i] != rgSlotBody[i]) throw new InvalidOperationException("noise body table readback mismatch");
        for (int i = 0; i < iN; i++) if (BitConverter.ToUInt32(rgD, iAtE + i * 4) != rgSlotExp[i]) throw new InvalidOperationException("noise expectation readback mismatch");
        if (bLayout) Console.WriteLine("  noise: slots=" + string.Join(",", rgSlotBody) + "  real=" + string.Join(",", rgReal) + "  exp=" + string.Join(",", rgExp));//期望值只在triage且LAMARR_LAYOUT=1时打印 发行stdout不落派生量
    }

    private const byte TmplPermSentinel = 0xA7;
    private const byte TmplRvaSentinel = 0x69;
    private const int TmplPermLen = 256;

    //把dispatch模板写进boot opcode到槽的256B置换表加h_perm在引擎.text里的RVA
    private void BakeRunTables(byte[] rgD, string sWhat)
    {
        if (uVmSeed == 0) { if (bLayout) Console.WriteLine($"  runtt[{sWhat}]: skipped (vmSeed=0)"); return; }
        if (rgEngine == null || rgEngine.Length == 0)
            throw new InvalidOperationException("runtt: engine must be loaded before the dispatch-template bake");
        string sIsa = IsaPool.CarrierIsa(uVmSeed, 0);
        byte[] rgPerm = PianolaEngine.BuildDispatchPerm(uVmSeed, sIsa, out _);
        int iRva = FindTextPatternRva(rgEngine, rgPerm, "h_perm");
        int iAtP = FindSentinelRun(rgD, TmplPermSentinel, TmplPermLen, "dispatch perm template");
        int iAtR = FindSentinelRun(rgD, TmplRvaSentinel, 8, "dispatch rva placeholder");
        Array.Copy(rgPerm, 0, rgD, iAtP, TmplPermLen);
        BitConverter.GetBytes(iRva).CopyTo(rgD, iAtR);
        for (int i = 0; i < TmplPermLen; i++)
            if (rgD[iAtP + i] != rgPerm[i]) throw new InvalidOperationException("runtt: perm template readback mismatch");
        if (BitConverter.ToInt32(rgD, iAtR) != iRva) throw new InvalidOperationException("runtt: rva readback mismatch");
        if (bLayout) Console.WriteLine($"  runtt[{sWhat}]: isa={sIsa} perm@0x{iAtP:X} rva@0x{iAtR:X} h_perm.rva=0x{iRva:X} (h_tbl = +0x100)");
    }

    //按唯一命中在引擎.text里定位模板h_perm紧排在h_tbl之前所以h_tbl等于h_perm加256
    private static int FindTextPatternRva(byte[] rgPe, byte[] rgPat, string sWhat)
    {
        int iPe = BitConverter.ToInt32(rgPe, 0x3C);
        ushort usNum = BitConverter.ToUInt16(rgPe, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgPe, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        for (int s = 0; s < usNum; s++)
        {
            int o = iSec + s * 40;
            if (o + 40 > rgPe.Length) break;
            if (System.Text.Encoding.ASCII.GetString(rgPe, o, 8).TrimEnd('\0') != ".text") continue;
            int iVa = BitConverter.ToInt32(rgPe, o + 12);
            int cbRaw = BitConverter.ToInt32(rgPe, o + 16);
            int iRaw = BitConverter.ToInt32(rgPe, o + 20);
            if (cbRaw <= 0 || iRaw <= 0 || iRaw + cbRaw > rgPe.Length) break;
            int iAt = -1;
            for (int i = iRaw; i + rgPat.Length <= iRaw + cbRaw; i++)
            {
                if (rgPe[i] != rgPat[0]) continue;
                if (!rgPe.AsSpan(i, rgPat.Length).SequenceEqual(rgPat)) continue;//整段比较 等价且快得多
                if (iAt >= 0) throw new InvalidOperationException($"runtt: {sWhat} pattern found twice in .text (untrustworthy)");
                iAt = i;
            }
            if (iAt < 0) throw new InvalidOperationException($"runtt: {sWhat} pattern not found in .text (seed/ISA mismatch between engine and boot bake?)");
            return iVa + (iAt - iRaw);
        }
        throw new InvalidOperationException("runtt: engine .text section not found");
    }

    private static int FindSentinelRun(byte[] rgD, byte bSent, int iLen, string sWhat)//哨兵占位块必须唯一命中否则定位不可信
    {
        byte[] rgSent = new byte[iLen];
        if (bSent != 0) Array.Fill(rgSent, bSent);//整段比较 等价于逐字节内循环但快一个数量级
        int iAt = -1;
        for (int i = 0; i + iLen <= rgD.Length; i++)
        {
            if (rgD[i] != bSent) continue;
            if (!rgD.AsSpan(i, iLen).SequenceEqual(rgSent)) continue;
            if (iAt >= 0) throw new InvalidOperationException($"{sWhat} placeholder found twice (location untrustworthy)");
            iAt = i;
        }
        if (iAt < 0) throw new InvalidOperationException($"{sWhat} placeholder not found (boot not built with it)");
        return iAt;
    }

    private static uint CctorImm(byte[] rgD, uint uTok)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) != ".cctor") continue;
            byte[]? rgIl = per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
            if (rgIl == null) continue;
            for (int iP = 0; iP + 5 < rgIl.Length; iP++)
            {
                if (rgIl[iP] != 0x80 && rgIl[iP] != 0x7D) continue;
                if (BitConverter.ToUInt32(rgIl, iP + 1) != uTok) continue;
                if (iP >= 5 && rgIl[iP - 5] == 0x20) return BitConverter.ToUInt32(rgIl, iP - 4);
                if (iP >= 2 && rgIl[iP - 2] == 0x1F) return (uint)(sbyte)rgIl[iP - 1];
                if (iP >= 1 && rgIl[iP - 1] >= 0x16 && rgIl[iP - 1] <= 0x1E) return (uint)(rgIl[iP - 1] - 0x16);
            }
        }
        throw new InvalidOperationException($"cctor immediate missing token=0x{uTok:X8}");
    }

    private static int ExpTamperSlot()//LAMARR_EXP_TAMPER sentinel表示不写数字表示把该槽位异或1
    {
#if LAMARR_TRIAGE
        string? sTamper = Environment.GetEnvironmentVariable("LAMARR_EXP_TAMPER");
        if (string.IsNullOrEmpty(sTamper)) return -2;
        int iSlot;
        if (sTamper.Equals("sentinel", StringComparison.OrdinalIgnoreCase)) iSlot = -1;//槽位块保持哨兵
        else if (int.TryParse(sTamper, out int iT) && iT >= 0 && iT < BootExp.SlotCount) iSlot = iT;//该槽位异或1
        else throw new InvalidOperationException($"LAMARR_EXP_TAMPER invalid: {sTamper}");
        Console.WriteLine(iSlot < 0
            ? "[exp] TAMPER sentinel: write skipped (build regression only)"
            : $"[exp] TAMPER slot={iSlot}: one slot corrupted (build regression only)");
        return iSlot;
#else
        return -2;//发行构建里没有篡改期望值块这条路
#endif
    }

    //把seal折叠量异或进非零槽 0槽是噪声槽必须保持0 改pub_B或剥掉seal都会让期望值对不上
    private void FoldSeal(uint[] rgSlot)
    {
        if (!bSealOn) return;
        byte[] rgPubB = rkSealPubB.Length == 32 ? rkSealPubB
            : (rkBuildKey.Length == 32 ? PubB(rkBuildKey) : throw new InvalidOperationException("seal fold needs the build key or the escrow's pubB"));
        uint uFold = SealFold(rgPubB);
        Console.WriteLine($"  [fold] pubB={Convert.ToHexString(rgPubB)[..16]} fold=0x{uFold:X8}");
        for (int i = 0; i < rgSlot.Length; i++)
        {
            if (rgSlot[i] == 0) { Console.WriteLine($"  [fold] slot[{i}]=0 (noise, kept)"); continue; }
            Console.WriteLine($"  [fold] slot[{i}] raw=0x{rgSlot[i]:X8} -> 0x{rgSlot[i] ^ uFold:X8}");
            rgSlot[i] ^= uFold;
            if (rgSlot[i] == 0) throw new InvalidOperationException("seal fold zeroed expectation slot " + i + " (re-seal with a different K)");
        }
    }
    //封印折是load-bearing 期望值槽要先按它折过boot才认 切掉产物就过不了自检
    private void FoldSealSlot(uint[] rgSlot) => FoldSeal(rgSlot);

    private void WriteBootExpectations(byte[] rgD)
    {
        uint uK0 = uExpK0, uK1 = uExpK1, uHs = uExpHs;//输入已按原名采好 这里只用令牌 改名与搬移都不影响
        if (uK0 == 0 || uK1 == 0 || uHs == 0) throw new InvalidOperationException("expectation inputs not captured (LoadBoot must run the BootExp contract)");

        uint uAd = IlHash(rgD, uExpTokAd, uK0, uK1);//原始值共四项 含方法体摘要与两个text摘要与三项已知答案
        uint uX1 = IlHash(rgD, uExpTokX1, uK0, uK1);
        uint uX3 = IlHash(rgD, uExpTokX3, uK0, uK1);
        uint uX6 = IlHash(rgD, uExpTokX6, uK0, uK1);
        uint uFanRaw = uJitSum ^ uDecSum ^ uAd ^ uX1 ^ uX3 ^ uX6
                     ^ BootExp.RawKatA ^ BootExp.RawKatB ^ BootExp.RawKatC;

        int iTamper = ExpTamperSlot();

        var rgSlot = new uint[BootExp.SlotCount];
        rgSlot[(int)ExpSlot.Jit] = uJitSum;
        rgSlot[(int)ExpSlot.Dec] = uDecSum;
        rgSlot[(int)ExpSlot.Ad] = uHs ^ uAd;
        rgSlot[(int)ExpSlot.X1] = uHs ^ uX1;
        rgSlot[(int)ExpSlot.X3] = uHs ^ uX3;
        rgSlot[(int)ExpSlot.X6] = uHs ^ uX6;
        rgSlot[(int)ExpSlot.KatA] = uHs ^ BootExp.RawKatA;
        rgSlot[(int)ExpSlot.KatB] = uHs ^ BootExp.RawKatB;
        rgSlot[(int)ExpSlot.KatC] = uHs ^ BootExp.RawKatC;
        rgSlot[(int)ExpSlot.Fan] = uHs ^ uFanRaw;
        FoldSealSlot(rgSlot);//封印折load-bearing 期望值槽先折过boot才认
        rgSlotBaked = (uint[])rgSlot.Clone();//真值留档给第二份块用 哨兵篡改只作用在第一份块上

        int iAt = FindSentinel(rgD);
        if (iTamper == -1)
        {
            Console.WriteLine($"[exp] TAMPER: sentinel kept @0x{iAt:X}");//故意不写哨兵留在产物里
        }
        else
        {
            if (iTamper >= 0) rgSlot[iTamper] ^= 1u;
            for (int i = 0; i < BootExp.SlotCount; i++) BitConverter.GetBytes(rgSlot[i]).CopyTo(rgD, iAt + i * 4);
            for (int i = 0; i < BootExp.SlotCount; i++)//回读覆盖位置 写错槽位或漏写都在这里暴露
                if (BitConverter.ToUInt32(rgD, iAt + i * 4) != rgSlot[i]) throw new InvalidOperationException($"expectation readback mismatch slot={i}");
        }
        Console.WriteLine($"  exp: {BootExp.SlotCount} slots @0x{iAt:X} jit=0x{uJitSum:X8} dec=0x{uDecSum:X8} fan=0x{rgSlot[(int)ExpSlot.Fan]:X8}");
    }

    private static uint IlHash(byte[] rgD, uint uTok, uint uK0, uint uK1)//方法体摘要逐字节异或乘 量的是镜像里的最终IL
    {
        byte[] rgIl = MethodIlByToken(rgD, uTok) ?? throw new InvalidOperationException($"boot method body missing token=0x{uTok:X8}");
        return IlHash(rgIl, uK0, uK1);
    }

    private static uint IlHash(byte[] rgIl, uint uK0, uint uK1)
    {
        uint uHash = uK0;
        foreach (byte b in rgIl) { uHash ^= b; uHash *= uK1; }
        return uHash;
    }

    private static uint MethodToken(byte[] rgD, string sName)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) == sName) return (uint)MetadataTokens.GetToken(h);
        }
        throw new InvalidOperationException($"boot method missing: {sName}");
    }

    private static byte[]? MethodIlByToken(byte[] rgD, uint uTok)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            if ((uint)MetadataTokens.GetToken(h) != uTok) continue;
            var md = mr.GetMethodDefinition(h);
            return per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
        }
        return null;
    }

    private static int FindSentinel(byte[] rgD)//占位块整段哨兵 命中数必须为1 否则定位不可信
    {
        int iAt = -1;
        for (int i = 0; i + BootExp.BlobBytes <= rgD.Length; i++)
        {
            if (rgD[i] != BootExp.Sentinel) continue;
            bool bOk = true;
            for (int k = 1; k < BootExp.BlobBytes; k++) if (rgD[i + k] != BootExp.Sentinel) { bOk = false; break; }
            if (!bOk) continue;
            if (iAt >= 0) throw new InvalidOperationException("expectation placeholder block found twice (location untrustworthy)");
            iAt = i;
        }
        if (iAt < 0) throw new InvalidOperationException("expectation placeholder block not found (boot not built from the BootExp contract)");
        return iAt;
    }

    private const byte Sentinel2 = 0x5A;//第二份占位块的哨兵

    private static int FindSentinelB(byte[] rgD)//第二份占位块 同样必须唯一命中否则定位不可信
    {
        byte[] rgSent = new byte[BootExp.BlobBytes];
        Array.Fill(rgSent, Sentinel2);
        int iAt = -1;
        for (int i = 0; i + BootExp.BlobBytes <= rgD.Length; i++)
        {
            if (rgD[i] != Sentinel2) continue;
            if (!rgD.AsSpan(i, BootExp.BlobBytes).SequenceEqual(rgSent)) continue;
            if (iAt >= 0) throw new InvalidOperationException("expectation shard placeholder found twice (location untrustworthy)");
            iAt = i;
        }
        if (iAt < 0) throw new InvalidOperationException("expectation shard placeholder not found (boot not built with the second blob)");
        return iAt;
    }

    private static uint ExpMask(uint uTail) => ((uTail ^ 0x9E3779B9u) >> 7) | ((uTail ^ 0x9E3779B9u) << 25);//掩码算式boot侧复算同一值

    private static int ExpTamper2Slot()//只改第二份块 用于证明后置读取点确实依赖它
    {
#if LAMARR_TRIAGE
        string? sTamper = Environment.GetEnvironmentVariable("LAMARR_EXP_TAMPER2");
        if (string.IsNullOrEmpty(sTamper)) return -2;
        if (!int.TryParse(sTamper, out int iT) || iT < 0 || iT >= BootExp.SlotCount)
            throw new InvalidOperationException($"LAMARR_EXP_TAMPER2 invalid: {sTamper}");
        Console.WriteLine($"[exp2] TAMPER2 slot={iT}: one slot of the second shard corrupted (build regression only)");
        return iT;
#else
        return -2;//发行构建里没有篡改第二份块这条路
#endif
    }

    private void WriteExpShard2(uint uTailRaw)//期望值分片 真值异或ExpMask(链尾)
    {
        if (rgSlotBaked == null) throw new InvalidOperationException("exp slots not captured (WriteBootExpectations must run first)");
        uint uMask = ExpMask(uTailRaw);
        int iAt = FindSentinelB(rgBoot);
        int iTamper2 = ExpTamper2Slot();
        for (int i = 0; i < BootExp.SlotCount; i++)
        {
            uint u = rgSlotBaked[i] ^ uMask;
            if (i == iTamper2) u ^= 1u;
            BitConverter.GetBytes(u).CopyTo(rgBoot, iAt + i * 4);
        }
        for (int i = 0; i < BootExp.SlotCount; i++)//回读覆盖位置 写错槽位或漏写都在这里暴露
        {
            uint uWant = rgSlotBaked[i] ^ uMask ^ (i == iTamper2 ? 1u : 0u);
            if (BitConverter.ToUInt32(rgBoot, iAt + i * 4) != uWant) throw new InvalidOperationException($"expectation shard readback mismatch slot={i}");
        }
        if (bLayout) Console.WriteLine($"  exp2: {BootExp.SlotCount} slots @0x{iAt:X} mask=0x{uMask:X8} tail=0x{uTailRaw:X8}");//mask与tail是派生量 发行不打
    }

    //把该方法体整体换成ldc.i4.0加ret 等长且IL合法 必须在WriteNoiseTable之后调用否则期望会量到已被杀掉的体
    private void KillNoiseBody(byte[] rgD)
    {
        if (uKillTok == 0) return;//LAMARR_IL_KILL 未设置
        int iWantRow = MetadataTokens.GetRowNumber(MetadataTokens.MethodDefinitionHandle((int)uKillTok));
        int iPe = BitConverter.ToInt32(rgD, 0x3C);
        ushort usNum = BitConverter.ToUInt16(rgD, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgD, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        int iTva = 0, iTroff = 0;
        for (int i = 0; i < usNum; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rgD.Length) break;
            if (System.Text.Encoding.ASCII.GetString(rgD, o, 8).TrimEnd('\0') == ".text")
            { iTva = BitConverter.ToInt32(rgD, o + 12); iTroff = BitConverter.ToInt32(rgD, o + 20); break; }
        }
        if (iTva == 0 || iTroff == 0) throw new InvalidOperationException("IL kill: .text section not found");
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var mdh in mr.MethodDefinitions)
        {
            if (MetadataTokens.GetRowNumber(mr, mdh) != iWantRow) continue;
            var md = mr.GetMethodDefinition(mdh);
            if (md.RelativeVirtualAddress == 0) break;
            int iOff = iTroff + (md.RelativeVirtualAddress - iTva);
            byte b0 = rgD[iOff];
            int iHdr, iCs;
            if ((b0 & 3) == 2) { iHdr = 1; iCs = b0 >> 2; }                                   //tiny header
            else if ((b0 & 3) == 3) { iHdr = 12; iCs = BitConverter.ToInt32(rgD, iOff + 4); }  //fat header 带locals
            else throw new InvalidOperationException($"IL kill: row {iWantRow} has no IL body");
            if (iCs < 2) throw new InvalidOperationException($"IL kill: row {iWantRow} body too small ({iCs})");
            for (int k = 0; k < iCs; k++) rgD[iOff + iHdr + k] = k == 0 ? (byte)0x16 : (k == 1 ? (byte)0x2A : (byte)0x00);
            Console.WriteLine($"  il_kill: tok=0x{uKillTok:X8} row={iWantRow} -> ldc.i4.0;ret  code={iCs} (negative test hook)");
            return;
        }
        throw new InvalidOperationException($"IL kill: row {iWantRow} not found");
    }
}
