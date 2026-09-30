namespace Lamarr.Packer;
using Lamarr;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Numerics;
using System.Runtime.InteropServices;

internal partial class PeWriterAntheil
{

    void BuildLamApp(long[] rgRel, long[] rgSz, long[] rgCsz, string[] rgName, List<int> rgDepIdx, string sSeed)
    {
        var rgRaw = new List<byte[]>();
        var rgNames = new List<string>();
        byte[]? rgSigBlob = null;
        int iDecIdx = -1, iJitIdx = -1, iSigIdx = -1, iPheropodIdx = -1;
        bool bLayout = Environment.GetEnvironmentVariable("LAMARR_LAYOUT") == "1";//构建侧布局摘要 只走stdout 不进产物
        int iRngSeed = RngSeed(0x31u);
        Random rng = new Random(iRngSeed);
        string RandName() => new string(Enumerable.Range(0, rng.Next(4, 16)).Select(_ => (char)('a' + rng.Next(26))).ToArray());

        {
            long lOndisk = rgCsz[iMainEntry] > 0 ? rgCsz[iMainEntry] : rgSz[iMainEntry];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[iMainEntry]), rgDll, 0, (int)lOndisk);
            PeScrub.Scrub(rgDll);
            if (bLayout) Console.WriteLine($"[layout] main ondisk={lOndisk} scrubbed={rgDll.Length} fnv={Fnv1aK1(rgDll):x8}");
            rgRaw.Add(rgDll); rgNames.Add(sMainName);
        }
        foreach (int i in rgDepIdx)
        {
            long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgDll, 0, (int)lOndisk);
            PeScrub.Scrub(rgDll);
            rgRaw.Add(rgDll); rgNames.Add(rgName[i]);
        }

        if (rgJitHook != null)
        {
            byte[] rgMat = SeedKey(Encoding.ASCII.GetBytes(sSeed));
            uint uJitKey = Fnv1aK1(rgMat);
            ulong uMaskKey = MaskKeyOf(rgMat);
            Array.Clear(rgMat, 0, rgMat.Length);
            var rgAllCrcs = new List<uint>(rgBootCrcs);//列表顺序=签名表序号 uKey2由序号派生 boot方法在前 base=0
            var rgAllVars = new List<byte>(rgBootVars);//与rgAllCrcs同序的变体标记
            int iNEncMethods = 0;
            for (int i = 0; i < rgRaw.Count; i++)//主程序始终加密 依赖SetEncryptDeps显式指定才加密 不压缩名单优先
            {
                if (i > 0 && (!rgEncryptDeps.Contains(rgNames[i]) || rgNoCompressDeps.Contains(rgNames[i]))) continue;
                EnsureIdxMap();//序号继续从全局计数取 与启动批共享同一张置换表 表项散列到各自槽位
                var rgBatchSlots = new List<int>();
                var rgBatchVars = new List<byte>();
                var rgCrcs = MethodEncryptor.EncryptAll(rgRaw[i], uJitKey, uMaskKey, null, rgAllCrcs.Count, rgBatchVars, rgIdxPerm, rgBatchSlots);
                for (int k = 0; k < rgCrcs.Count; k++) PutIdxEntry(rgBatchSlots[k], rgCrcs[k], rgBatchVars[k]);
                rgAllVars.AddRange(rgBatchVars);
                if (rgCrcs.Count == 0)//应加密批次须真出条目 静默少加即构建期报错
                    throw new InvalidOperationException($"batch {i}({rgNames[i]}) encrypted no method body: check the rgRaw bound and the encryption predicate");
                rgAllCrcs.AddRange(rgCrcs);
                iNEncMethods += rgCrcs.Count;
                if (bLayout) Console.WriteLine($"[layout] after-encrypt entry={i}({rgNames[i]}) len={rgRaw[i].Length} fnv={Fnv1aK1(rgRaw[i]):x8} crcs={rgCrcs.Count}");
            }
            if (iNEncMethods == 0)//主程序须始终加密 一条都没有=上界或判定写错
                throw new InvalidOperationException("no method body encrypted: check the rgRaw bound and the encryption predicate (the main program must always be encrypted)");
            Console.WriteLine($"[jithook] encrypted {iNEncMethods} method bodies -> {rgAllCrcs.Count} sigs, {rgAllVars.Count} variant tags");
            if (rgAllVars.Count != rgAllCrcs.Count)//region 3 指纹区count*4 变体区=magic+count+count*4指纹+count字节变体 变体段把打包期iVx交给运行期钩子
                throw new InvalidOperationException($"variant tag count differs from signature count: {rgAllVars.Count} != {rgAllCrcs.Count}");
            int iNSig = iIdxSpace;//表长=槽位空间 位置即位号 未用槽填诱饵指纹 诱饵槽需确定性冲突修正 否则按内容寻址命中诱饵槽
            var hsReal = new HashSet<uint>(rgAllCrcs);
            var rgSigBytes = new byte[8 + iNSig * 5];
            BitConverter.GetBytes(0x32474953u).CopyTo(rgSigBytes, 0);//"SIG2"
            BitConverter.GetBytes((uint)iNSig).CopyTo(rgSigBytes, 4);
            ulong uDecoy = uVmSeed ^ 0xD1B54A32D192ED03UL;
            if (uDecoy == 0) uDecoy = 0x9E3779B97F4A7C15UL;
            int iReal = 0;
            for (int i = 0; i < iNSig; i++)
            {
                uint uVal;
                if (rgIdxUsed[i]) { uVal = rgIdxCrc[i]; iReal++; }
                else
                {
                    do
                    {
                        uDecoy ^= uDecoy << 13; uDecoy ^= uDecoy >> 7; uDecoy ^= uDecoy << 17;
                        uVal = (uint)uDecoy ^ (uint)(uDecoy >> 32);
                    } while (hsReal.Contains(uVal));
                }
                BitConverter.GetBytes(uVal).CopyTo(rgSigBytes, 8 + i * 4);
                rgSigBytes[8 + iNSig * 4 + i] = rgIdxVar[i];
            }
            if (iReal != rgAllCrcs.Count)
                throw new InvalidOperationException($"index table hash mismatch: inserted {iReal} != bodies {rgAllCrcs.Count} (space {iIdxSpace})");
            rgSigBlob = rgSigBytes;
        }

        if (rgPheropod != null)
        {
            iPheropodIdx = rgRaw.Count;
            rgRaw.Add(rgPheropod); rgNames.Add(RandName());
            Console.WriteLine($"[pheropod] gzip decoy: {rgPheropod.Length} bytes");
        }

        int iCount = rgRaw.Count;

        const int iDecoys = 6;
        var rgDecName = new byte[iDecoys][];
        var rgDecData = new byte[iDecoys][];
        var rgDecoyOff = new uint[iDecoys];
        for (int i = 0; i < iDecoys; i++)
        {
            int iNl = rng.Next(4, 16);
            rgDecName[i] = new byte[iNl];
            for (int j = 0; j < iNl; j++)
                rgDecName[i][j] = (byte)('a' + rng.Next(26));
            if (i == 0)
                rgDecData[i] = BuildFakePe(rng);
            else if (i == 1 || i == 2)
                rgDecData[i] = GzipZ0(BuildVmLure(rng));
            else
                rgDecData[i] = GzipZ0(GenRandomX64(rng, 64, 512));
        }

        var rgNameBytes = new byte[iCount + iDecoys][];
        uint uNameTotal = 0;
        for (int i = 0; i < iCount; i++)
        {
            rgNameBytes[i] = Encoding.UTF8.GetBytes(rgNames[i]);
            uNameTotal += (uint)rgNameBytes[i].Length;
        }
        for (int i = 0; i < iDecoys; i++)
        {
            rgNameBytes[iCount + i] = rgDecName[i];
            uNameTotal += (uint)rgNameBytes[iCount + i].Length;
        }
        uint uNameAreaLen = (uNameTotal + (uint)(iCount + iDecoys) + 3) & ~3u;

        var rgBlocks = new byte[iCount][];
        var rgRawLen = new uint[iCount];
        var rgCompLen = new uint[iCount];
        var rgCompOff = new uint[iCount];
        byte[] rgSeedKey = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        uint uAdj = MixAdj(rgSeedKey);
        for (int i = 0; i < iCount; i++)
        {
            rgRawLen[i] = (uint)rgRaw[i].Length;
            if (i == iDecIdx || i == iJitIdx || i == iSigIdx || i == iPheropodIdx)
            {
                rgCompLen[i] = rgRawLen[i];
                rgBlocks[i] = XorBytes(rgRaw[i], rgSeedKey, uAdj);
            }
            else if (i > 0 && (!bCompressDeps || rgNoCompressDeps.Contains(rgNames[i])))
            {
                rgCompLen[i] = rgRawLen[i];
                rgBlocks[i] = rgRaw[i];
                DisturbHead(rgBlocks[i], rgHead);
            }
            else
            {
                uint uCap = LamarrEncoder.GetMaxEncodedSize(rgRawLen[i]);
                rgBlocks[i] = new byte[uCap];
                uint uPcb = uCap;
                if (LamarrEncoder.Encode(rgBlocks[i], ref uPcb, rgRaw[i], rgRawLen[i]) != 0)
                    throw new InvalidOperationException($"Lamarr encode failed: {rgNames[i]}");
                if (bLayout && i == 0) Console.WriteLine($"[layout] comp0={uPcb} cap={uCap}");
                if (bLayout && i > 0) Console.WriteLine($"[layout] comp{i}={uPcb} entry={rgNames[i]}");
                byte[] rgTag = PolyTag(rgSeedKey, rgBlocks[i], (int)uPcb);//Poly1305标记对压缩流 头16字节
                byte[] rgBody = new byte[16 + uPcb];
                rgTag.CopyTo(rgBody, 0);
                Array.Copy(rgBlocks[i], 0, rgBody, 16, (int)uPcb);
                rgBlocks[i] = rgBody;
                rgCompLen[i] = (uint)(16 + uPcb);
                if (i == 0 && rgDecoder.Length > 0)
                {
                    rgBlocks[i] = MergeMainStream(rgBlocks[i], rgDecoder, rgJitHook, rgSigBlob, rgEngine, rgRoll, rng, rgSeedKey);
                    rgBlocks[i] = XorBytes(rgBlocks[i], rgSeedKey, uAdj);
                    rgCompLen[i] = (uint)rgBlocks[i].Length;
                    if (bLayout) Console.WriteLine($"[layout] merged{i}={rgBlocks[i].Length}");
                }
            }
        }

        var rgPhys = new List<(int iKind, int iIdx)>();
        rgPhys.Add((5, -1));//BSJB头
        rgPhys.Add((0, 0));//主程序流
        rgPhys.Add((3, -1));//表块4B total+16B*N
        rgPhys.Add((2, -1));//垃圾
        rgPhys.Add((4, -1));//名字块
        var rgOrder = Enumerable.Range(1, Math.Max(0, iCount - 1)).ToArray();
        for (int i = rgOrder.Length - 1; i > 0; i--)//显式重排 不用会随实现版本变的Linq的延迟求值
        {
            int j = rng.Next(i + 1);
            (rgOrder[i], rgOrder[j]) = (rgOrder[j], rgOrder[i]);
        }
        for (int i = 0; i < rgOrder.Length; i++)
        {
            rgPhys.Add((0, rgOrder[i]));
            if (i % 2 == 1) rgPhys.Add((2, -1));
        }
        rgPhys.Add((2, -1));
        rgPhys.Add((1, 0));//伪PE明文
        foreach (int iT in new[] { 3, 1, 4, 2, 5 })
        {
            rgPhys.Add((2, -1));
            rgPhys.Add((1, iT));
        }
        rgPhys.Add((2, -1));

        var rgGarbage = new List<int>();
        uint uDataOff = 0;
        int iTblPos = 0, iNamePos = 0, iBsjbPos = 0;
        foreach (var (iKind, iIdx) in rgPhys)
        {
            if (iKind == 0) { rgCompOff[iIdx] = uDataOff; uDataOff += rgCompLen[iIdx]; }
            else if (iKind == 1) { rgDecoyOff[iIdx] = uDataOff; uDataOff += (uint)rgDecData[iIdx].Length; }
            else if (iKind == 3) { iTblPos = (int)uDataOff; uDataOff += 4u + (uint)(iCount + iDecoys) * 16; }
            else if (iKind == 4) { iNamePos = (int)uDataOff; uDataOff += uNameAreaLen; }
            else if (iKind == 5) { uDataOff += (uint)((8 - (int)(uDataOff % 8)) % 8); iBsjbPos = (int)uDataOff; uDataOff += (uint)iBsjbLen; }
            else { int iSz = rng.Next(32, 257); rgGarbage.Add(iSz); uDataOff += (uint)iSz; }
        }
        if (bLayout)//构建侧数据区分量 定位长度差
        {
            uint uCompSum = 0;
            for (int i = 0; i < iCount; i++) uCompSum += rgCompLen[i];
            var r0 = NewRng(0x31u);
            var sFresh = string.Join(",", Enumerable.Range(0, 8).Select(_ => r0.Next(32, 257)));
            Console.WriteLine($"[layout] dataOff={uDataOff} tbl={4 + (iCount + iDecoys) * 16} names={uNameAreaLen} bsjb={iBsjbLen} garbage={rgGarbage.Count}:[{string.Join(",", rgGarbage)}] fresh=[{sFresh}] compSum={uCompSum} decoys={string.Join(",", Array.ConvertAll(rgDecData, d => d.Length))}");
        }

uint uKey = LamKey(sSeed);
        int iHead = 68 + ((int)((uKey >> 0) & 0x1Fu) << 2);//bundle头落在68到192之间
        int iDataBase = (iHead + 16 + ((int)((uKey >> 8) & 0x3Fu) << 2) + 7) & ~7; //数据区起点按8对齐

        uint uTotalLen = (uint)iDataBase + uDataOff;
        rgLamApp = new byte[uTotalLen];

        uint cbOrigTotal = 0;
        for (int i = 0; i < iCount; i++) cbOrigTotal += rgRawLen[i];

        //entry表的字段顺序与逐字段pad按entry序号派生

        //开头64字节是根材料明文 后面全是随机字节 与诱饵同分布
        Array.Copy(rgHead, 0, rgLamApp, 0, Math.Min(rgHead.Length, 64));
        byte[] rgRand = GenShapeArea(Math.Max(1, iDataBase - 64));
        Array.Copy(rgRand, 0, rgLamApp, 64, Math.Min(rgRand.Length, iDataBase - 64));
        LamWriteXor(rgLamApp, iHead, (uint)iCount, uKey);
        LamWriteXor(rgLamApp, iHead + 4, cbOrigTotal, uKey);
        LamWriteXor(rgLamApp, iHead + 8, (uint)(iDataBase + iTblPos), uKey);
        LamWriteXor(rgLamApp, iHead + 12, (uint)(iDataBase + iNamePos), uKey);

        uint uPos = 0; int iG = 0;
        foreach (var (iKind, iIdx) in rgPhys)
        {
            int iDst = iDataBase + (int)uPos;
            if (iKind == 0)
            {
                Array.Copy(rgBlocks[iIdx], 0, rgLamApp, iDst, rgCompLen[iIdx]);
                uPos += rgCompLen[iIdx];
            }
            else if (iKind == 1)
            {
                Array.Copy(rgDecData[iIdx], 0, rgLamApp, iDst, rgDecData[iIdx].Length);
                uPos += (uint)rgDecData[iIdx].Length;
            }
            else if (iKind == 2)
            {
                int iSz = rgGarbage[iG++];
                byte[] rgGb = GenRandomX64(rng, iSz, iSz + 200);
                if (rgGb.Length > iSz) Array.Resize(ref rgGb, iSz);
                Array.Copy(rgGb, 0, rgLamApp, iDst, rgGb.Length);
                uPos += (uint)rgGb.Length;
            }
            else if (iKind == 3)
            {
                LamWriteXor(rgLamApp, iDst, (uint)(iCount + iDecoys), uKey);//条目表 [total 4B^uKey][16B*N] XOR K3逐entry置换加逐字段独立pad
                for (int i = 0; i < iCount + iDecoys; i++)
                {
                    uint[] rgF = new uint[4];
                    int[] rgP = LamPermAt(uKey, i);//字段顺序按entry派生 解出一行即全表
                    if (i < iCount)
                    {
                        bool bPlain = i > 0 && i != iDecIdx && i != iJitIdx && i != iSigIdx && i != iPheropodIdx && (!bCompressDeps || rgNoCompressDeps.Contains(rgNames[i]));
                        rgF[0] = bPlain ? 0x7FFFFFFFu : rgRawLen[i];
                        rgF[1] = bPlain ? rgRawLen[i] : rgCompLen[i];
                        rgF[2] = rgCompOff[i];
                        rgF[3] = LamMix(LamSlot(uKey, ((iCount + iDecoys) << 2) + i + 1));//boot只写不读 真值0/诱饵1是现成标记 取与pad无关的伪随机值
                    }
                    else
                    {
                        int iDd = i - iCount;
                        int iL = rgDecData[iDd].Length;
                        //长度与压缩长度取不等且合理的值 压缩长度取小
                        int iCut = iL > 1 ? 1 + (int)(LamMix(LamSlot(uKey, iDd)) % (uint)Math.Min(iL - 1, 64)) : 0;
                        rgF[0] = (uint)iL;
                        rgF[1] = (uint)(iL - iCut);
                        rgF[2] = rgDecoyOff[iDd];
                        rgF[3] = LamMix(LamSlot(uKey, ((iCount + iDecoys) << 2) + i + 1));
                    }
                    int iOff = iDst + 4 + i * 16;
                    for (int iS = 0; iS < 4; iS++)
                        BitConverter.GetBytes(rgF[rgP[iS]] ^ LamFieldKey(uKey, i, iS)).CopyTo(rgLamApp, iOff + iS * 4);
                }
                uPos += 4u + (uint)(iCount + iDecoys) * 16;
            }
            else if (iKind == 4)
            {
                uint uNm = uKey ^ 0x2B7E1516u;//名字块长度前缀异或名字流
                int iNp = iDst;
                for (int i = 0; i < iCount + iDecoys; i++)
                {
                    byte[] nm = rgNameBytes[i];
                    rgLamApp[iNp] = (byte)nm.Length;
                    uNm = (uNm * uPermMul) ^ 0x9E3779B9u;
                    for (int j = 0; j < nm.Length; j++)
                    {
                        uNm = (uNm * uPermMul) + 0x1234567u;
                        rgLamApp[iNp + 1 + j] = (byte)(nm[j] ^ (byte)(uNm >> 24));
                    }
                    iNp += 1 + nm.Length;
                }
                uPos += uNameAreaLen;
            }
            else if (iKind == 5)
            {
                uPos += (uint)((8 - (int)(uPos % 8)) % 8);//BSJB头8字节对齐 FakeLamAppLoader按8步进扫描
                int iDst2 = iDataBase + (int)uPos;
                int iNameOffAbs = iDataBase + iNamePos;
                int iDecOffAbs = iDataBase + (int)rgCompOff[0] + 8;//真流0内段表区
                byte[] rgBsjb = BuildFakeBsjb(iNameOffAbs, (int)uNameAreaLen, iDecOffAbs, 0x80);
                for (int i = 0; i < iBsjbLen; i++)
                    rgLamApp[iDst2 + i] = (byte)(rgBsjb[i] ^ rgKBsjb[i % rgKBsjb.Length]);
                uPos += (uint)iBsjbLen;
            }
        }

        Console.WriteLine($"  .rdata: {iCount} entry(s) + {iDecoys} decoy, {cbOrigTotal} -> {rgLamApp.Length} bytes (Lamarr)");
    }

    private IntPtr hZ0;//z0.dll打包期只加载一次 导出名随释放种子改名 压缩入口按序号取
    private Z0Deflate? fnZ0;

    private delegate IntPtr Z0Deflate(byte[] rgDst, IntPtr cbDst, byte[] rgSrc, IntPtr cbSrc);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string sPath);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hLib, IntPtr pName);

    private static byte[]? ReadEmbeddedZ0()//程序集内嵌压缩端 无外部z0.dll时用
    {
        Stream? st = typeof(PeWriterAntheil).Assembly.GetManifestResourceStream("z0.dll");
        if (st == null)
            return null;
        using (st)
        {
            using var ms = new MemoryStream();
            st.CopyTo(ms);
            return ms.ToArray();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hLib);

    private static byte[] Z0Press(string sPath, byte[] rgPlain)//独立加载一份并压一次 供自检比输出
    {
        IntPtr h = LoadLibraryW(sPath);
        if (h == IntPtr.Zero)
            throw new InvalidOperationException($"z0 load failed: {sPath} err={Marshal.GetLastWin32Error()}");
        try
        {
            IntPtr pFn = GetProcAddress(h, new IntPtr(5));//序号按指针值传 不走名字
            if (pFn == IntPtr.Zero)
                throw new InvalidOperationException($"z0 export 5 missing: {sPath}");
            var fn = Marshal.GetDelegateForFunctionPointer<Z0Deflate>(pFn);
            byte[] rgDst = new byte[rgPlain.Length + (rgPlain.Length >> 3) + 0x40];
            int iOut = (int)fn(rgDst, new IntPtr(rgDst.Length), rgPlain, new IntPtr(rgPlain.Length));
            if (iOut < 18 || iOut > rgDst.Length)
                throw new InvalidOperationException($"z0 compress failed: {iOut}");
            byte[] rgOut = new byte[iOut];
            Array.Copy(rgDst, rgOut, iOut);
            return rgOut;
        }
        finally
        {
            FreeLibrary(h);
        }
    }

    private void Z0Verify(byte[] rgPre, byte[] rgMut)//变异只动元数据与中立插槽 压缩输出必须与变异前一致
    {
        string sPre = LayOutZ0(rgPre);
        string sMut = LayOutZ0(rgMut);
        var rng = new Random(0x5A17);
        var rgVec = new byte[3][];
        rgVec[0] = new byte[4096];
        rng.NextBytes(rgVec[0]);
        rgVec[1] = new byte[8192];//高可压 走另一条匹配路径
        for (int i = 0; i < rgVec[1].Length; i++) rgVec[1][i] = (byte)(i & 7);
        rgVec[2] = new byte[257];
        rng.NextBytes(rgVec[2]);
        var rgBase = new byte[rgVec.Length][];
        int nOut = 0;
        for (int v = 0; v < rgVec.Length; v++)
        {
            rgBase[v] = Z0Press(sMut, rgVec[v]);
            byte[] rgOutPre = Z0Press(sPre, rgVec[v]);
            if (rgOutPre.Length != rgBase[v].Length || !rgOutPre.AsSpan().SequenceEqual(rgBase[v]))
                throw new InvalidOperationException("pheropod self check failed: the mutated copy changed the compressed output");
            nOut += rgBase[v].Length;
        }
        int nTry = 0;
        foreach (var vs in rgVarSlot)//每个等长变体候选单独跑一遍 穷尽验证等价性
        {
            for (int k = 1; k < vs.Cand.Length; k++)
            {
                byte[] rgTry = (byte[])rgMut.Clone();
                Buffer.BlockCopy(vs.Cand[k], 0, rgTry, vs.Ofs, vs.Len);
                string sTry = LayOutZ0(rgTry);
                for (int v = 0; v < rgVec.Length; v++)
                {
                    byte[] rgOut = Z0Press(sTry, rgVec[v]);
                    if (rgOut.Length != rgBase[v].Length || !rgOut.AsSpan().SequenceEqual(rgBase[v]))
                        throw new InvalidOperationException($"pheropod variant self check failed at 0x{vs.Ofs:X} candidate {k}");
                }
                nTry++;
            }
        }
        Console.WriteLine($"[pheropod] self-check ok {rgVec.Length} vectors {nOut} bytes, variants tried {nTry}");
    }

    private static string LayOutZ0(byte[] rgZ0)//LoadLibrary要文件 同内容同名复用避免落盘
    {
        uint uKey = 2166136261;
        foreach (byte b in rgZ0)
            uKey = (uKey ^ b) * 16777619;
        string sDir = Path.Combine(Path.GetTempPath(), "LamarrPacker");
        Directory.CreateDirectory(sDir);
        string sPath = Path.Combine(sDir, $"z0_{rgZ0.Length:x}_{uKey:x8}.dll");
        if (!File.Exists(sPath) || new FileInfo(sPath).Length != rgZ0.Length)
            File.WriteAllBytes(sPath, rgZ0);
        return sPath;
    }

    private void OpenZ0(string sPath)//诱饵与压缩端同源 已按种子改过落盘 这里只加载
    {
        if (fnZ0 != null)
            return;
        hZ0 = LoadLibraryW(sPath);
        if (hZ0 == IntPtr.Zero)
            throw new InvalidOperationException($"z0 load failed: {sPath} err={Marshal.GetLastWin32Error()}");
        IntPtr pFn = GetProcAddress(hZ0, new IntPtr(5));//序号按指针值传 不走名字
        if (pFn == IntPtr.Zero)
            throw new InvalidOperationException($"z0 export 5 missing: {sPath}");
        fnZ0 = Marshal.GetDelegateForFunctionPointer<Z0Deflate>(pFn);
    }

    private byte[] GzipZ0(byte[] rgPlain)//诱饵条目是gzip流 解码端与编码端同在这份z0.dll里
    {
        if (fnZ0 == null)
            throw new InvalidOperationException("z0 compressor not loaded");
        byte[] rgDst = new byte[rgPlain.Length + (rgPlain.Length >> 3) + 0x40];//与压缩端约定的容量上界
        IntPtr lOut = fnZ0(rgDst, new IntPtr(rgDst.Length), rgPlain, new IntPtr(rgPlain.Length));
        int iOut = (int)lOut;
        if (iOut < 18 || iOut > rgDst.Length)
            throw new InvalidOperationException($"z0 compress failed: {iOut}");
        if (rgDst[0] != 0x1F || rgDst[1] != 0x8B || rgDst[2] != 8)
            throw new InvalidOperationException("z0 output carries no gzip header");
        if (BitConverter.ToUInt32(rgDst, iOut - 4) != (uint)rgPlain.Length)//尾四字节=isize 对不上即入口取错
            throw new InvalidOperationException("z0 output length marker mismatch");
        byte[] rgOut = new byte[iOut];
        Array.Copy(rgDst, rgOut, iOut);
        return rgOut;
    }
}
