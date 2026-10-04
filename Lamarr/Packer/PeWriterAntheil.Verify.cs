namespace Lamarr.Packer;

using A0;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal partial class PeWriterAntheil
{
    private static readonly ExpSlot[] rgBodySlots = { ExpSlot.Ad, ExpSlot.X1, ExpSlot.X3, ExpSlot.X6 };//复算这四个方法体

    internal static int CheckBootExp(string sExe)
    {
        byte[] rgExe = File.ReadAllBytes(sExe);
        Console.WriteLine($"[exp] exe={Path.GetFileName(sExe)} bytes={rgExe.Length}");
        BootLayer ly = RecoverBootLayer(rgExe);
        if (ly.rgPlain == null) { Console.WriteLine($"[exp] FAIL {ly.sWhy}"); return 1; }
        Console.WriteLine($"[exp] boot={ly.rgPlain.Length}B entry={ly.lEntSize}B layer={ly.iLayer}B cipher={ly.iCipher}B tail={(ly.bTailOk ? "ok" : "skip")}");
        if (!ly.bLayer)
        {
            Console.WriteLine("[exp] N/A the artifact has no entry layer, the expectation contract does not apply");
            return 0;
        }
        if (!AuditEntryLayer(rgExe, out string sAudit)) { Console.WriteLine($"[exp] FAIL entry layer audit: {sAudit}"); return 1; }

        int iSent = FindSentinelRun(ly.rgPlain);//哨兵没被覆盖=打包漏 比关系式先判 能指名位置
        if (iSent >= 0) { Console.WriteLine($"[exp] FAIL sentinel intact bootOff=0x{iSent:X}"); return 1; }

        var mpHash = MethodHashMap(ly.rgPlain);
        var rgHit = new List<SlotHit>();
        for (int iAt = 0; iAt + BootExp.BlobBytes <= ly.rgPlain.Length; iAt++)
        {
            SlotHit sh = CheckSlots(ly.rgPlain, iAt, mpHash);
            if (sh.bOk) rgHit.Add(sh);
        }
        if (rgHit.Count == 0)
        {
            SlotHit shNear = BestEffort(ly.rgPlain, mpHash);//一处都不中时报最像的那处 区分槽位被改与镜像被换
            if (shNear.iAt >= 0) Console.WriteLine($"[exp] near miss bootOff=0x{shNear.iAt:X} score={shNear.iScore}/6 ({shNear.sWhy})");
            Console.WriteLine("[exp] FAIL no blob matches the relations (slot edited or boot swapped)");
            return 1;
        }
        if (rgHit.Count > 1) { Console.WriteLine($"[exp] FAIL blobs={rgHit.Count} first=0x{rgHit[0].iAt:X} location not trustworthy"); return 1; }

        SlotHit hit = rgHit[0];
        Console.WriteLine($"[exp] blob bootOff=0x{hit.iAt:X} uHs={hit.uHs:X8}");
        Console.WriteLine($"[exp] slots jit={hit.rgSlot[(int)ExpSlot.Jit]:X8} dec={hit.rgSlot[(int)ExpSlot.Dec]:X8} ad={hit.rgSlot[(int)ExpSlot.Ad]:X8} x1={hit.rgSlot[(int)ExpSlot.X1]:X8}");
        Console.WriteLine($"[exp] slots x3={hit.rgSlot[(int)ExpSlot.X3]:X8} x6={hit.rgSlot[(int)ExpSlot.X6]:X8} katA={hit.rgSlot[(int)ExpSlot.KatA]:X8} katB={hit.rgSlot[(int)ExpSlot.KatB]:X8} katC={hit.rgSlot[(int)ExpSlot.KatC]:X8} fan={hit.rgSlot[(int)ExpSlot.Fan]:X8}");
        foreach (ExpSlot s in rgBodySlots)
        {
            uint uWant = hit.rgSlot[(int)s] ^ hit.uHs;
            mpHash.TryGetValue(uWant, out var rgTok);
            Console.WriteLine($"[exp] body={(int)s} hash={uWant:X8} token={rgTok![0]:X8}");
        }
        //掩码能回到cctor立即数里 即属于这个镜像
        Console.WriteLine($"[exp] fan=ok uHs=hit cctor={(CctorImms(ly.rgPlain).Contains(hit.uHs) ? "hit" : "miss")}");
        Console.WriteLine("[exp] N/A the jit/dec digests live in the jithook and decoder images, self-asserted at runtime by JitVerify/DecVerify");
        Console.WriteLine("[exp] PASS");
        return 0;
    }

    //一个偏移上的槽位判定评分与理由 供一处都不中时报最接近的那处
    private struct SlotHit
    {
        public bool bOk;
        public int iAt;
        public int iScore;
        public uint uHs;
        public uint[] rgSlot;
        public string sWhy;
    }

    private static SlotHit CheckSlots(byte[] rgD, int iAt, Dictionary<uint, List<uint>> mpHash)//十个槽位是否全部对上关系式 与运行期的SelfCheck逐一对应
    {
        var rgSlot = new uint[BootExp.SlotCount];
        for (int i = 0; i < BootExp.SlotCount; i++) rgSlot[i] = BitConverter.ToUInt32(rgD, iAt + i * 4);
        uint uHs = rgSlot[(int)ExpSlot.KatA] ^ rgSlot[(int)ExpSlot.KatB] ^ rgSlot[(int)ExpSlot.KatC]//掩码由三个已知答案槽反解 反解不出即此处不是块
                 ^ BootExp.RawKatA ^ BootExp.RawKatB ^ BootExp.RawKatC;
        int iScore = 0;
        var rgWhy = new List<string>();
        bool bMask = uHs != 0 && uHs != 0xFFFFFFFFu;
        if (bMask) { iScore++; rgWhy.Add("mask"); }
        bool bCrc = rgSlot[(int)ExpSlot.Jit] != 0 && rgSlot[(int)ExpSlot.Dec] != 0
                 && rgSlot[(int)ExpSlot.Jit] != rgSlot[(int)ExpSlot.Dec];
        if (bCrc) { iScore++; rgWhy.Add("crc"); }
        var setTok = new HashSet<uint>();//四个方法体摘要各命中唯一方法 且四个方法互不相同
        int iBody = 0;
        foreach (ExpSlot s in rgBodySlots)
        {
            if (!mpHash.TryGetValue(rgSlot[(int)s] ^ uHs, out var rgTok) || rgTok.Count != 1) continue;
            if (!setTok.Add(rgTok[0])) continue;
            iBody++;
        }
        iScore += iBody;
        rgWhy.Add($"body={iBody}");
        uint uFanRaw = rgSlot[(int)ExpSlot.Jit] ^ rgSlot[(int)ExpSlot.Dec]//宽扇入九项一并核对
                     ^ (rgSlot[(int)ExpSlot.Ad] ^ uHs) ^ (rgSlot[(int)ExpSlot.X1] ^ uHs)
                     ^ (rgSlot[(int)ExpSlot.X3] ^ uHs) ^ (rgSlot[(int)ExpSlot.X6] ^ uHs)
                     ^ BootExp.RawKatA ^ BootExp.RawKatB ^ BootExp.RawKatC;
        bool bFan = rgSlot[(int)ExpSlot.Fan] == (uHs ^ uFanRaw);
        if (bFan) { iScore++; rgWhy.Add("fan"); }
        return new SlotHit
        {
            bOk = bMask && bCrc && bFan && iBody == rgBodySlots.Length,
            iAt = iAt, iScore = iScore, uHs = uHs, rgSlot = rgSlot, sWhy = string.Join(" ", rgWhy),
        };
    }

    private static SlotHit BestEffort(byte[] rgD, Dictionary<uint, List<uint>> mpHash)//全镜像找评分最高的一处
    {
        var shBest = new SlotHit { iAt = -1, rgSlot = Array.Empty<uint>(), sWhy = "" };
        for (int iAt = 0; iAt + BootExp.BlobBytes <= rgD.Length; iAt++)
        {
            SlotHit sh = CheckSlots(rgD, iAt, mpHash);
            if (sh.iScore > shBest.iScore) shBest = sh;
        }
        return shBest;
    }

    private static Dictionary<uint, List<uint>> MethodHashMap(byte[] rgD)//镜像里全部方法体的摘要表 摘要值对方法令牌
    {
        var mpHash = new Dictionary<uint, List<uint>>();
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (md.RelativeVirtualAddress == 0) continue;//extern无实体 GetMethodBody会抛
            byte[]? rgIl = per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
            if (rgIl == null) continue;
            uint uHash = IlHash(rgIl, BootExp.RawK0, BootExp.RawK1);
            if (!mpHash.TryGetValue(uHash, out var rgTok)) { rgTok = new List<uint>(); mpHash[uHash] = rgTok; }
            rgTok.Add((uint)MetadataTokens.GetToken(h));
        }
        return mpHash;
    }

    private static HashSet<uint> CctorImms(byte[] rgD)//cctor里的整数立即数集合 掩码应当能在这里找到
    {
        var set = new HashSet<uint>();
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) != ".cctor") continue;
            if (md.RelativeVirtualAddress == 0) continue;//extern无实体 GetMethodBody会抛
            byte[]? rgIl = per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
            if (rgIl == null) continue;
            for (int iP = 0; iP + 4 < rgIl.Length; iP++)
            {
                if (rgIl[iP] != 0x80 && rgIl[iP] != 0x7D) continue;
                if (iP >= 5 && rgIl[iP - 5] == 0x20) { set.Add(BitConverter.ToUInt32(rgIl, iP - 4)); continue; }
                if (iP >= 2 && rgIl[iP - 2] == 0x1F) { set.Add((uint)(sbyte)rgIl[iP - 1]); continue; }
                if (iP >= 1 && rgIl[iP - 1] >= 0x16 && rgIl[iP - 1] <= 0x1E) set.Add((uint)(rgIl[iP - 1] - 0x16));
            }
        }
        return set;
    }

    private static int FindSentinelRun(byte[] rgD)//占位块是整段哨兵 找不到返回-1
    {
        for (int i = 0; i + BootExp.BlobBytes <= rgD.Length; i++)
        {
            if (rgD[i] != BootExp.Sentinel) continue;
            bool bOk = true;
            for (int k = 1; k < BootExp.BlobBytes; k++) if (rgD[i + k] != BootExp.Sentinel) { bOk = false; break; }
            if (bOk) return i;
        }
        return -1;
    }

    private struct BootLayer//产物入口层几何与boot明文 空=复原失败 原因在sWhy
    {
        public byte[]? rgPlain;
        public bool bLayer;
        public bool bTailOk;
        public long lEntOff;
        public long lEntSize;
        public int iLayer;
        public int iCipher;
        public string sWhy;
    }

    private static BootLayer RecoverBootLayer(byte[] rgExe)//入口层条目布局 入口层镜像密文16字节尾部记录密文校验 先判不过就当复原失败
    {
        var ly = new BootLayer { sWhy = "" };
        int iMarker = FindMarkerOff(rgExe);
        if (iMarker < 0) { ly.sWhy = "bundle marker not found"; return ly; }
        int iPos = (int)BitConverter.ToInt64(rgExe, iMarker - 8);
        if (iPos < 0 || iPos + 16 > rgExe.Length) { ly.sWhy = "bundle header offset out of range"; return ly; }
        uint uMajor = BitConverter.ToUInt32(rgExe, iPos); iPos += 4;
        iPos += 4;
        int iCount = BitConverter.ToInt32(rgExe, iPos); iPos += 4;
        if (iCount <= 0 || iCount > 0x1000) { ly.sWhy = "entry count bad"; return ly; }
        if (iPos >= rgExe.Length) { ly.sWhy = "bundle header truncated"; return ly; }
        iPos += 1 + rgExe[iPos];//bundleId
        if (uMajor >= 2) iPos += 40;
        var rgSz = new long[iCount];
        long lOff0 = 0, lSz0 = 0;
        for (int i = 0; i < iCount && i < 0x1000; i++)
        {
            if (iPos + 17 > rgExe.Length) { ly.sWhy = "entry table truncated"; return ly; }
            long lOff = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            long lSz = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            if (uMajor >= 6) iPos += 8;
            iPos += 1;
            if (iPos >= rgExe.Length) { ly.sWhy = "entry table truncated"; return ly; }
            iPos += 1 + rgExe[iPos];//条目名
            rgSz[i] = lSz;
            if (i == 0) { lOff0 = lOff; lSz0 = lSz; }
        }
        if (lSz0 <= Q.iTailLen || lOff0 <= 0 || lOff0 > rgExe.Length - lSz0) { ly.sWhy = "app entry bounds bad"; return ly; }
        ly.lEntOff = lOff0;
        ly.lEntSize = lSz0;
        int iCipher = (int)(BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 16) ^ Q.uLenMask);
        ulong uChk = BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 8) ^ Q.uChkMask;
        int iLayer = (int)(lSz0 - Q.iTailLen - iCipher);
        if (iCipher <= 0 || iLayer <= 0)
        {
            byte[] rgPlain = new byte[(int)(lSz0 - Q.iTailLen)];//没有入口层 该条目本体就是boot
            Array.Copy(rgExe, (int)lOff0, rgPlain, 0, rgPlain.Length);
            ly.rgPlain = rgPlain;
            ly.sWhy = "no entry layer (boot is the entry itself)";
            return ly;
        }
        ly.bLayer = true;
        ly.iLayer = iLayer;
        ly.iCipher = iCipher;
        byte[] rgHead = new byte[Math.Min(Q.iHeadSample, iLayer)];
        Array.Copy(rgExe, (int)lOff0, rgHead, 0, rgHead.Length);
        int iRollLenV = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 0);
        int iEngLenV = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 4);
        int iPrekLenV = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 16) ^ (int)Q.uPrekMask;
        uint uRollKeyEnt = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 8) ^ Q.TailKeyMaskOf(PreKernEcho(rgHead, Q.uDomainSeed));//掩码含前置内核输出
        uint uPolicyV = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 12) ^ Q.uLayoutMask;
        Q.EntrySegmentOffsets(iLayer - iPrekLenV, iRollLenV, iEngLenV, uPolicyV, out int iRollOffV, out int iEngOffV);
        ulong uSelfMix = Q.SelfMixRange(rgExe, (int)lOff0 + iRollOffV, iRollLenV, (int)lOff0 + iEngOffV, iEngLenV);
        byte[] rgKey = Q.KeyStream(rgHead, lSz0, iCipher, Q.TableDigest(rgSz), VmMaskEcho(rgHead, Q.uDomainSeed, uRollKeyEnt), uSelfMix);
        byte[] rgBody = new byte[iCipher];
        Array.Copy(rgExe, (int)(lOff0 + iLayer), rgBody, 0, iCipher);
        ulong uGot = Q.Hash64(rgBody, 0, rgBody.Length);
        ly.bTailOk = uGot == uChk;
        if (!ly.bTailOk)
        {
            ly.sWhy = $"entry cipher check failed got=0x{uGot:X16} want=0x{uChk:X16}";
            return ly;
        }
        Q.Xor(rgBody, 0, rgKey);
        Array.Clear(rgKey, 0, rgKey.Length);
        Array.Clear(rgHead, 0, rgHead.Length);
        ly.rgPlain = rgBody;
        ly.sWhy = "entry layer ok";
        return ly;
    }
}
