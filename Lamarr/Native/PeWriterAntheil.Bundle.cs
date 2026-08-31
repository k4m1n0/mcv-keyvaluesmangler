using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

internal partial class PeWriterAntheil
{
    #region bundle重建

    private long FindMarkerOffset(byte[] rgB)
    {
        //查bundle marker 返回头部偏移
        for (int i = 0; i + 40 <= rgB.Length; i++)
            if (rgB[i + 8] == rgSignature[0] && MatchSig(rgB, i + 8))
                return BitConverter.ToInt64(rgB, i);
        return -1;
    }

    private bool MatchSig(byte[] rgB, int iOff)
    {
        for (int j = 0; j < 32; j++)
            if (rgB[iOff + j] != rgSignature[j]) return false;
        return true;
    }

    private long ParseBundleFirstEntryOffset(byte[] rgB, long lHeaderOff)
    {
        int iPos = (int)lHeaderOff;
        uint uMajor = ReadU32(rgB, ref iPos);
        ReadU32(rgB, ref iPos);
        int iN = ReadI32(rgB, ref iPos);
        ReadStr(rgB, ref iPos);
        if (uMajor >= 2)
        {
            ReadI64(rgB, ref iPos); ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos); ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos);
        }
        long lFirst = 0;
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            long lOff = ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos);
            if (uMajor >= 6)
                ReadI64(rgB, ref iPos);
            ReadU8(rgB, ref iPos);
            ReadStr(rgB, ref iPos);
            if (i == 0) lFirst = lOff;
        }
        return lFirst;
    }


    private void RebuildBundle(string sSeed)
    {
        rgKBsjb = GenKBsjb(sSeed);
        int iPos = (int)lBundleHeaderOffset;
        uint uMajor = ReadU32(rgPayload, ref iPos);
        ReadU32(rgPayload, ref iPos);
        int iN = ReadI32(rgPayload, ref iPos);
        string sBundleId = ReadStr(rgPayload, ref iPos);

        long lDepsSz = 0, lRtcSz = 0, lRtcHash = 0;
        if (uMajor >= 2)
        {
            ReadI64(rgPayload, ref iPos);
            lDepsSz = ReadI64(rgPayload, ref iPos);
            ReadI64(rgPayload, ref iPos);
            lRtcSz = ReadI64(rgPayload, ref iPos);
            lRtcHash = ReadI64(rgPayload, ref iPos);
        }

        var rgRel = new long[iN];
        var rgSz = new long[iN];
        var rgCsz = new long[iN];
        var rgType = new byte[iN];
        var rgName = new string[iN];
        iMainEntry = -1;
        iIdxRtc = -1;
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            rgRel[i] = ReadI64(rgPayload, ref iPos) - iBundleDataStart;
            rgSz[i] = ReadI64(rgPayload, ref iPos);
            if (uMajor >= 6)
                rgCsz[i] = ReadI64(rgPayload, ref iPos);
            else
                rgCsz[i] = 0;
            rgType[i] = ReadU8(rgPayload, ref iPos);
            rgName[i] = ReadStr(rgPayload, ref iPos);
            if (iMainEntry < 0 && rgName[i].Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                iMainEntry = i;
            if (iIdxRtc < 0 && rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                iIdxRtc = i;
        }
        if (iMainEntry < 0)
            throw new InvalidOperationException($"Bundle main assembly '{sMainName}' not found. Input: '{sPayloadPath}'");

        //区分条目 主程序/boot保留托管dll剥离 其余进新bundle
        var rgKeepIdx = new List<int>();
        var rgDepIdx = new List<int>();
        rgStripDeps.Clear();
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            if (i == iMainEntry) { rgKeepIdx.Add(i); continue; }
            if (rgName[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                IsManagedDll(i, rgRel, rgSz, rgCsz))
            {
                rgDepIdx.Add(i);
                rgStripDeps.Add(rgName[i].Substring(0, rgName[i].Length - 4));
            }
            else
            {
                rgKeepIdx.Add(i);
            }
        }

        BuildLamApp(rgRel, rgSz, rgCsz, rgName, rgDepIdx, sSeed);

        ComputeBundleStart();
        BuildBundleDataAndHeader(uMajor, sBundleId, rgKeepIdx, rgRel, rgSz, rgCsz, rgType, rgName,
                                 lDepsSz, lRtcSz, lRtcHash);
    }

    //主程序压缩流多区混入 解码器/jithook/签名表/垃圾
    private byte[] MergeMainStream(byte[] rgComp0, byte[] rgDec, byte[] rgJit, byte[] rgSig, Random rng)
    {
        //附加区在尾部 供X3直接解压
        var rgRegions = new List<(int type, byte[] data, bool bRev)>();
        if (rgDec != null && rgDec.Length > 0) rgRegions.Add((1, rgDec, true));
        if (rgJit != null && rgJit.Length > 0) rgRegions.Add((2, rgJit, true));
        if (rgSig != null && rgSig.Length > 0) rgRegions.Add((3, rgSig, true));
        int iGx = rng.Next(2, 5), iGi = rng.Next(2, 5);
        for (int i = 0; i < iGx; i++) rgRegions.Add((4, GenRandomX64(rng, 0x80, 0x200), false));
        for (int i = 0; i < iGi; i++) rgRegions.Add((5, GenIa64Area(rng.Next(0x80, 0x200)), false));
        if (rgRegions.Count == 0) return rgComp0;
        int iHdr = 8 + rgRegions.Count * 12 + 4;
        int iCompLen = rgComp0.Length;
        int iAtt = iHdr + iCompLen;
        byte[] rgOut = new byte[iAtt + rgRegions.Sum(r => r.data.Length)];
        BitConverter.GetBytes(rgRegions.Count).CopyTo(rgOut, 0);
        BitConverter.GetBytes(0x80).CopyTo(rgOut, 4);
        int iOff = 0;
        for (int r = 0; r < rgRegions.Count; r++)
        {
            BitConverter.GetBytes(rgRegions[r].type).CopyTo(rgOut, 8 + r * 12);
            BitConverter.GetBytes(iOff).CopyTo(rgOut, 12 + r * 12);
            BitConverter.GetBytes(rgRegions[r].data.Length).CopyTo(rgOut, 16 + r * 12);
            iOff += rgRegions[r].data.Length;
        }
        BitConverter.GetBytes(iCompLen).CopyTo(rgOut, 8 + rgRegions.Count * 12);
        Array.Copy(rgComp0, 0, rgOut, iHdr, iCompLen);
        int iA = iAtt;
        foreach (var (type, data, bRev) in rgRegions)
        {
            if (bRev) for (int r = 0; r < data.Length / 2; r++) (data[r], data[data.Length - 1 - r]) = (data[data.Length - 1 - r], data[r]);
            Array.Copy(data, 0, rgOut, iA, data.Length);
            iA += data.Length;
        }
        return rgOut;
    }


    private void BuildLamApp(long[] rgRel, long[] rgSz, long[] rgCsz, string[] rgName, List<int> rgDepIdx, string sSeed)
    {
        var rgRaw = new List<byte[]>();
        var rgNames = new List<string>();
        byte[] rgSigBlob = null;
        int iDecIdx = -1, iJitIdx = -1, iSigIdx = -1, iPheropodIdx = -1;
        var rng = new Random(Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        string RandName() => new string(Enumerable.Range(0, rng.Next(4, 16)).Select(_ => (char)('a' + rng.Next(26))).ToArray());

        //jithook与解码器 混入主程序流时XOR并字节反转

        {
            long lOndisk = rgCsz[iMainEntry] > 0 ? rgCsz[iMainEntry] : rgSz[iMainEntry];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[iMainEntry]), rgDll, 0, (int)lOndisk);
            rgRaw.Add(rgDll); rgNames.Add(sMainName);
        }
        foreach (int i in rgDepIdx)
        {
            long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgDll, 0, (int)lOndisk);
            rgRaw.Add(rgDll); rgNames.Add(rgName[i]);
        }

        if (rgJitHook != null)
        {
            uint uJitKey = Fnv1a(SeedKey(Encoding.ASCII.GetBytes(sSeed)));
            var rgAllCrcs = new List<(ulong Hi, ulong Lo)>();
            int iNEncMethods = 0;
            //主程序始终加密 依赖SetEncryptDeps显式指定才加密 不压缩名单优先
            for (int i = 0; i < rgRaw.Count - 1; i++)
            {
                if (i > 0 && (!rgEncryptDeps.Contains(rgNames[i]) || rgNoCompressDeps.Contains(rgNames[i]))) continue;
                var rgCrcs = MethodEncryptor.EncryptAll(rgRaw[i], uJitKey);
                rgAllCrcs.AddRange(rgCrcs);
                iNEncMethods += rgCrcs.Count;
            }
            rgAllCrcs.AddRange(rgBootCrcs);//B后半方法(W1/X4/X5)密文指纹 一并进签名表供jithook识别
            Console.WriteLine($"[jithook] encrypted {iNEncMethods} method bodies -> {rgAllCrcs.Count} sigs");
            var rgSigBytes = new byte[rgAllCrcs.Count * 16];
            for (int i = 0; i < rgAllCrcs.Count; i++)
            {
                BitConverter.GetBytes(rgAllCrcs[i].Lo).CopyTo(rgSigBytes, i * 16);//lo64 低32=crc2^mask32
                BitConverter.GetBytes(rgAllCrcs[i].Hi).CopyTo(rgSigBytes, i * 16 + 8);//hi64 uKey2^mask64
            }
            rgSigBlob = rgSigBytes;
        }

        //附加诱饵蜜罐
        if (rgPheropod != null)
        {
            iPheropodIdx = rgRaw.Count;
            rgRaw.Add(rgPheropod); rgNames.Add(RandName());
            Console.WriteLine($"[pheropod] gzip decoy: {rgPheropod.Length} bytes");
        }

        int iCount = rgRaw.Count;

        //生成随机诱饵条目混淆bundle
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
            {
                byte[] rgPlain = BuildVmLure(rng);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true))
                    gz.Write(rgPlain, 0, rgPlain.Length);
                rgDecData[i] = ms.ToArray();
            }
            else
            {
                byte[] rgPlain = GenRandomX64(rng, 64, 512);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true))
                    gz.Write(rgPlain, 0, rgPlain.Length);
                rgDecData[i] = ms.ToArray();
            }
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
                rgCompLen[i] = rgRawLen[i];//明文存储 头部扰动
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
                rgCompLen[i] = uPcb;
                if (i == 0 && rgDecoder.Length > 0)
                {
                    byte[] rgBlk = new byte[uPcb];
                    Array.Copy(rgBlocks[i], 0, rgBlk, 0, uPcb);
                    rgBlocks[i] = MergeMainStream(rgBlk, rgDecoder, rgJitHook, rgSigBlob, rng);
                    rgBlocks[i] = XorBytes(rgBlocks[i], rgSeedKey, uAdj);//整体XOR
                    rgCompLen[i] = (uint)rgBlocks[i].Length;
                }
            }
        }

        var rgPhys = new List<(int iKind, int iIdx)>();
        rgPhys.Add((5, -1));//BSJB头
        rgPhys.Add((0, 0));//主程序流
        rgPhys.Add((3, -1));//表块 4B total+16B*N
        rgPhys.Add((2, -1));//垃圾
        rgPhys.Add((4, -1));//名字块
        var rgOrder = Enumerable.Range(1, Math.Max(0, iCount - 1)).OrderBy(_ => rng.Next()).ToArray();
        for (int i = 0; i < rgOrder.Length; i++)
        {
            rgPhys.Add((0, rgOrder[i]));
            if (i % 2 == 1) rgPhys.Add((2, -1));
        }
        rgPhys.Add((2, -1));
        rgPhys.Add((1, 0));//伪PE 明文
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


        uint uKey = LamKey(sSeed);
        int iHash = 64;
        int iHead = 68 + ((int)((uKey >> 0) & 0x1Fu) << 2);//68-192 bundle头
        int iDataBase = (iHead + 16 + ((int)((uKey >> 8) & 0x3Fu) << 2) + 7) & ~7; //数据区起点 8对齐

        uint uTotalLen = (uint)iDataBase + uDataOff;
        rgLamApp = new byte[uTotalLen];

        uint cbOrigTotal = 0;
        for (int i = 0; i < iCount; i++) cbOrigTotal += rgRawLen[i];

        int[] rgPerm = LamPerm(uKey);

        //前导区 IA64密钥区 FNV 随机填充 bundle头XOR uKey 随机填充
        Array.Copy(rgHead, 0, rgLamApp, 0, Math.Min(rgHead.Length, 64));
        BitConverter.GetBytes(Fnv1a(rgRaw[0])).CopyTo(rgLamApp, iHash);
        byte[] rgRand = new byte[Math.Max(1, iDataBase - 68)];
        rng.NextBytes(rgRand);
        Array.Copy(rgRand, 0, rgLamApp, 68, Math.Min(rgRand.Length, iDataBase - 68));
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
                //条目表 [total 4B^uKey][16B*N] XOR K3 LamPerm置换
                LamWriteXor(rgLamApp, iDst, (uint)(iCount + iDecoys), uKey);
                for (int i = 0; i < iCount + iDecoys; i++)
                {
                    uint uKk = LamSlot(uKey, i);
                    uint[] rgF = new uint[4];
                    if (i < iCount)
                    {
                        bool bPlain = i > 0 && i != iDecIdx && i != iJitIdx && i != iSigIdx && i != iPheropodIdx && (!bCompressDeps || rgNoCompressDeps.Contains(rgNames[i]));
                        rgF[0] = bPlain ? 0x7FFFFFFFu : rgRawLen[i];
                        rgF[1] = bPlain ? rgRawLen[i] : rgCompLen[i];
                        rgF[2] = rgCompOff[i];
                        rgF[3] = 0;
                    }
                    else
                    {
                        int iDd = i - iCount;
                        rgF[0] = (uint)rgDecData[iDd].Length;
                        rgF[1] = (uint)rgDecData[iDd].Length;
                        rgF[2] = rgDecoyOff[iDd];
                        rgF[3] = 1;
                    }
                    int iOff = iDst + 4 + i * 16;
                    for (int iS = 0; iS < 4; iS++)
                        BitConverter.GetBytes(rgF[rgPerm[iS]] ^ uKk).CopyTo(rgLamApp, iOff + iS * 4);
                }
                uPos += 4u + (uint)(iCount + iDecoys) * 16;
            }
            else if (iKind == 4)
            {
                //名字块 长度前缀 XOR名字流
                uint uNm = uKey ^ 0x2B7E1516u;
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
                //BSJB头按8字节对齐 供FakeLamAppLoader按8步进扫描
                uPos += (uint)((8 - (int)(uPos % 8)) % 8);
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

    #endregion
    #region 密钥派生

    private static string MakeSeed() => Guid.NewGuid().ToString("N");

    //IA64 bundle外观 16字节含3指令槽与5位template
    private static byte[] GenIa64Area(int cb)
    {
        byte[] rgB = new byte[cb];
        var rng = new Random(Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF) ^ 0x5A5A5A5A);
        rng.NextBytes(rgB);
        for (int i = 0; i + 16 <= rgB.Length; i += 16)
            rgB[i + 15] = (byte)((rgB[i + 15] & 0xE0) | (byte)rng.Next(0, 32));//template位
        return rgB;
    }

    //四路FNV乱序
    private static string DeriveSeed(byte[] rgH)
    {
        uint uA = 0x811C9DC5, uB = 0x15050001, uC = 0x1234567u, uD = 0x9E3779B9u;
        for (int k = 0; k < 64; k++)
        {
            byte b = rgH[(k * 23 + 7) & 63];
            if ((k & 1) == 0) { uA ^= b; uA *= uPermMul; uC ^= b; uC *= uPermMul; }
            else { uB ^= b; uB *= uPermMul; uD ^= b; uD *= uPermMul; }
        }
        return uA.ToString("X8") + uB.ToString("X8") + uC.ToString("X8") + uD.ToString("X8");
    }

    //分支种子从密钥区子段派生给VM壳/诱饵/垃圾
    private static uint DeriveBranch(byte[] rgH, int iOff, int iLen, uint uSalt)
    {
        uint uA = 0x1234567u ^ uSalt, uB = uPermAdd ^ uSalt;
        for (int k = 0; k < iLen; k++)
        {
            byte b = rgH[iOff + ((k * 23 + 7) & 63) % (iLen > 0 ? iLen : 1)];
            if ((k & 1) == 0) { uA ^= b; uA *= uPermMul; }
            else { uB ^= b; uB *= uPermMul; }
        }
        return uA ^ (uB << 7) ^ (uB >> 9);
    }

    //假BSJB头密钥由seed派生随机生成 写入stub的##KBSJB##槽
    private static byte[] GenKBsjb(string sSeed)
    {
        byte[] rgB = new byte[32];
        uint uS = LamKey(sSeed) ^ 0x9E3779B9u;
        for (int i = 0; i < 32; i++)
        {
            uS = uS * 0x01000193u + 0x9E3779B9u;
            rgB[i] = (byte)(uS >> 24);
        }
        return rgB;
    }

    //明文依赖头部扰动key 每产物不同 独立于seed链 单向不可逆推rgHead
    private static byte[] GenDisturb(byte[] rgH)
    {
        uint uA = 0x6E5A1F2Bu, uB = 0x4D7C9E35u;
        for (int k = 0; k < 64; k++)
        {
            byte b = rgH[(k * 19 + 11) & 63];
            if ((k & 1) == 0) uA = (uA ^ b) * 0x85EBCA6Bu;
            else uB = (uB ^ b) * 0xC2B2AE35u;
        }
        byte[] rgK = new byte[16];
        uint uS = uA ^ uB;
        for (int i = 0; i < 16; i++)
        {
            uS = uS * 0x01000193u + 0x9E3779B9u;
            rgK[i] = (byte)(uS >> 24);
        }
        return rgK;
    }

    private static void DisturbHead(byte[] rgD, byte[] rgH)
    {
        byte[] rgK = GenDisturb(rgH);
        int n = Math.Min(1024, rgD.Length);
        for (int i = 0; i < n; i++)
            rgD[i] = (byte)(rgD[i] ^ rgK[i & 15]);
    }

    #endregion
    #region 指纹随机化

    //等长等语义指纹随机化 每产物随机 跳过栈指针 全表含32/64位互换 保守表仅xor与sub
    private static void ScrambleFingerprint(byte[] rgB, Random rng, bool bConservative, int iStart = 0, int iEnd = -1)
    {
        int iLim = iEnd < 0 ? rgB.Length : Math.Min(iEnd, rgB.Length);
        for (int i = Math.Max(0, iStart); i + 2 < iLim; i++)
        {
            byte b0 = rgB[i], b1 = rgB[i + 1];
            bool fRex = b0 == 0x48;
            if (!fRex && (b0 == 0x33 || b0 == 0x29) && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4)
            {
                if (rng.Next(2) == 0) rgB[i] = (byte)(b0 ^ 0x1A);
                i++;
                continue;
            }
            if (fRex && (b1 == 0x33 || b1 == 0x29) && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4)
            {
                if (rng.Next(2) == 0) rgB[i + 1] = (byte)(b1 ^ 0x1A);
                i += 2;
                continue;
            }
            if (bConservative) continue;
            if (!fRex && b0 == 0x83 && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4)
            {
                int iReg = (b1 >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 2;
                    if (iImm < rgB.Length && rgB[iImm] != 0x80)
                    {
                        if (rng.Next(2) == 0)
                        {
                            rgB[iImm] = (byte)(-(sbyte)rgB[iImm]);
                            rgB[i + 1] = (byte)((b1 & 0xC7) | (uint)(iReg == 0 ? 5 : 0) << 3);
                        }
                        i += 2;
                        continue;
                    }
                }
            }
            if (fRex && b1 == 0x83 && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4)
            {
                int iReg = (rgB[i + 2] >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 3;
                    if (iImm < rgB.Length && rgB[iImm] != 0x80)
                    {
                        if (rng.Next(2) == 0)
                        {
                            rgB[iImm] = (byte)(-(sbyte)rgB[iImm]);
                            rgB[i + 2] = (byte)((rgB[i + 2] & 0xC7) | (uint)(iReg == 0 ? 5 : 0) << 3);
                        }
                        i += 3;
                        continue;
                    }
                }
            }
            if (!fRex && b0 == 0x81 && (b1 & 0xC0) == 0xC0 && (b1 & 7) != 4)
            {
                int iReg = (b1 >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 2;
                    if (iImm + 3 < rgB.Length)
                    {
                        if (rng.Next(2) == 0)
                        {
                            uint uV = BitConverter.ToUInt32(rgB, iImm);
                            BitConverter.GetBytes(0u - uV).CopyTo(rgB, iImm);
                            rgB[i + 1] = (byte)((b1 & 0xC7) | (uint)(iReg == 0 ? 5 : 0) << 3);
                        }
                        i += 5;
                        continue;
                    }
                }
            }
            if (fRex && b1 == 0x81 && (rgB[i + 2] & 0xC0) == 0xC0 && (rgB[i + 2] & 7) != 4)
            {
                int iReg = (rgB[i + 2] >> 3) & 7;
                if (iReg == 0 || iReg == 5)
                {
                    int iImm = i + 3;
                    if (iImm + 3 < rgB.Length)
                    {
                        if (rng.Next(2) == 0)
                        {
                            uint uV = BitConverter.ToUInt32(rgB, iImm);
                            BitConverter.GetBytes(0u - uV).CopyTo(rgB, iImm);
                            rgB[i + 2] = (byte)((rgB[i + 2] & 0xC7) | (uint)(iReg == 0 ? 5 : 0) << 3);
                        }
                        i += 6;
                        continue;
                    }
                }
            }
        }
    }

    //dll文件 等长等语义扰动仅限.text节
    private static void ScrambleTextSection(byte[] rgDll, Random rng, bool bConservative)
    {
        if (rgDll == null || rgDll.Length < 0x80) return;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        if (iPe + 0x18 > rgDll.Length || BitConverter.ToUInt32(rgDll, iPe) != 0x4550) return;
        ushort usNum = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 4 + 20 + usOpt;
        for (int i = 0; i < usNum; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rgDll.Length) break;
            string sName = Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0');
            if (sName != ".text") continue;
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uRsz = BitConverter.ToUInt32(rgDll, o + 16);
            int iStart = (int)uRaw;
            int iEnd = (int)Math.Min((uint)rgDll.Length, uRaw + uRsz);
            if (iStart < 0 || iEnd <= iStart + 2) return;
            ScrambleFingerprint(rgDll, rng, bConservative, iStart, iEnd);
            return;
        }
    }

    #endregion
    #region 假壳生成

    //假BSJB metadata root #~/#Strings指向诱饵
    private static byte[] BuildFakeBsjb(int iNameOff, int iNameLen, int iDecOff, int iDecLen)
    {
        byte[] rgB = new byte[64];
        rgB[0] = 0x42; rgB[1] = 0x53; rgB[2] = 0x4A; rgB[3] = 0x42;//BSJB
        rgB[4] = 0x01; rgB[5] = 0x00;                        //uMajor 1
        rgB[6] = 0x01; rgB[7] = 0x00;                        //minor 1
        rgB[12] = 0x0C;                                      //version iLen 12
        byte[] rgVer = Encoding.ASCII.GetBytes("v4.0.30319");
        Array.Copy(rgVer, 0, rgB, 16, rgVer.Length);
        rgB[30] = 0x02;                                      //2 streams
        BitConverter.GetBytes((uint)iDecOff).CopyTo(rgB, 32);//stream0 #~ 解码器数据区
        BitConverter.GetBytes((uint)iDecLen).CopyTo(rgB, 36);
        rgB[40] = 0x23; rgB[41] = 0x7E;                      //"#~"
        BitConverter.GetBytes((uint)iNameOff).CopyTo(rgB, 44);//stream1 #Strings 名称区
        BitConverter.GetBytes((uint)iNameLen).CopyTo(rgB, 48);
        byte[] rgNs = Encoding.ASCII.GetBytes("#Strings");
        Array.Copy(rgNs, 0, rgB, 52, rgNs.Length);
        return rgB;
    }

    private static byte[] GenRandomX64(Random rng, int iCbMin, int iCbMax)
    {
        int iCbTarget = rng.Next(iCbMin, iCbMax);
        using var ms = new MemoryStream();
        if (rng.Next(2) == 0)
        {
            //MSVC风味 影子区保存 push非易失 sub rsp,N 函数体 尾声
            int nReg = 1 + rng.Next(4);
            var rgReg = new int[nReg];
            for (int i = 0; i < nReg; i++) rgReg[i] = rng.Next(8);
            for (int i = 0; i < nReg; i++) { byte[] m = MsvcSave(rgReg[i], i); ms.Write(m, 0, m.Length); }
            for (int i = nReg - 1; i >= 0; i--) { byte[] p = MsvcPush(rgReg[i]); ms.Write(p, 0, p.Length); }
            int iFr = 0x20 + 0x10 * rng.Next(8);
            ms.Write(new byte[] { 0x48, 0x83, 0xEC, (byte)iFr }, 0, 4);//sub rsp,N
            iCbTarget += 16 + nReg * 4;
            while (ms.Length < iCbTarget)
            {
                byte[] rgInsn = X64Insn(rng);
                ms.Write(rgInsn, 0, rgInsn.Length);
            }
            ms.Write(new byte[] { 0x48, 0x83, 0xC4, (byte)iFr }, 0, 4);//add rsp,N
            for (int i = 0; i < nReg; i++) { byte[] p = MsvcPop(rgReg[i]); ms.Write(p, 0, p.Length); }
            ms.Write(new byte[] { 0xC3 }, 0, 1);//ret
            return ms.ToArray();
        }
        if (rng.Next(4) == 0)
        {
            ms.Write(new byte[] { 0x55, 0x48, 0x8B, 0xEC }, 0, 4);//push rbp; mov rbp,rsp
            iCbTarget += 4;
        }
        while (ms.Length < iCbTarget)
        {
            byte[] rgInsn = X64Insn(rng);
            ms.Write(rgInsn, 0, rgInsn.Length);
        }
        if (rng.Next(4) == 0)//尾声
            ms.Write(new byte[] { 0x5D, 0xC3 }, 0, 2);//pop rbp; ret
        return ms.ToArray();
    }

    private static int MsvcReg(int ri) => ri < 4 ? new[] { 3, 5, 6, 7 }[ri] : 12 + (ri - 4);

    //mov [rsp+8+iOff*8],reg 影子区保存
    private static byte[] MsvcSave(int ri, int iOff)
    {
        int r = MsvcReg(ri);
        byte[] rg = new byte[r >= 8 ? 5 : 4];
        int p = 0;
        if (r >= 8) rg[p++] = 0x41;
        rg[p++] = 0x89;
        rg[p++] = (byte)(0x44 | ((r & 7) << 3));
        rg[p++] = 0x24;
        rg[p++] = (byte)(8 + iOff * 8);
        return rg;
    }

    private static byte[] MsvcPush(int ri)
    {
        int r = MsvcReg(ri);
        if (r < 8) return new byte[] { (byte)(0x50 + r) };
        return new byte[] { 0x41, (byte)(0x50 + (r - 8)) };
    }

    private static byte[] MsvcPop(int ri)
    {
        int r = MsvcReg(ri);
        if (r < 8) return new byte[] { (byte)(0x58 + r) };
        return new byte[] { 0x41, (byte)(0x58 + (r - 8)) };
    }

    private static byte[] X64Insn(Random rng)
    {
        switch (rng.Next(40))
        {
            case 0: return new byte[] { 0x48, 0x8B, (byte)(0xC0 + rng.Next(8)) };//mov rAX,rX
            case 1: return new byte[] { 0x48, 0x89, (byte)(0xC0 + rng.Next(8)) };//mov rX,rAX
            case 2: { var rgI = new byte[10]; rgI[0] = 0x48; rgI[1] = 0xB8; BitConverter.GetBytes(((ulong)(uint)rng.Next() << 32) | (uint)rng.Next()).CopyTo(rgI, 2); return rgI; }//mov rAX,imm64
            case 3: return new byte[] { 0x48, 0x83, (byte)(0xC0 + rng.Next(8)), (byte)rng.Next(256) };//add rX,imm8
            case 4: return new byte[] { 0x48, 0x29, (byte)(0xC0 + rng.Next(8)) };//sub rAX,rX
            case 5: return new byte[] { 0x48, 0x33, (byte)(0xC0 + rng.Next(8)) };//xor rAX,rX
            case 6: return new byte[] { 0x31, (byte)(0xC0 + rng.Next(8)) };//xor rX,rAX 32位
            case 7: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0x8D; rgI[2] = (byte)(0x80 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//lea rX,[rAX+disp32]
            case 8: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0xC7; rgI[2] = (byte)(0xC0 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//mov rX,imm32
            case 9: return new byte[] { (byte)(0x50 + rng.Next(8)) };//push rX
            case 10: return new byte[] { (byte)(0x58 + rng.Next(8)) };//pop rX
            case 11: return new byte[] { 0x48, 0x85, (byte)(0xC0 + rng.Next(8)) };//test rAX,rX
            case 12: return new byte[] { 0x48, 0x39, (byte)(0xC0 + rng.Next(8)) };//cmp rAX,rX
            case 13: return new byte[] { 0x48, 0x63, (byte)(0xC0 + rng.Next(8)) };//movsxd rAX,rX
            case 14: return new byte[] { 0x48, 0x0F, 0xAF, (byte)(0xC0 + rng.Next(8)) };//imul rAX,rX
            case 15: return new byte[] { 0x48, 0x01, (byte)(0xC0 + rng.Next(8)) };//add rAX,rX
            case 16: return new byte[] { 0x48, 0x21, (byte)(0xC0 + rng.Next(8)) };//and rAX,rX
            case 17: return new byte[] { 0x48, 0x09, (byte)(0xC0 + rng.Next(8)) };//or rAX,rX
            case 18: return new byte[] { 0x48, 0xD1, (byte)(0xE0 + rng.Next(8)) };//shl rX,1
            case 19: return new byte[] { 0x48, 0xD1, (byte)(0xE8 + rng.Next(8)) };//shr rX,1
            case 20: return new byte[] { 0x48, 0xC1, (byte)(0xE0 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//shl rX,imm8
            case 21: return new byte[] { 0x48, 0x0F, 0x44, (byte)(0xC0 + rng.Next(8)) };//cmove rAX,rX
            case 22: return new byte[] { 0x48, 0xF7, (byte)(0xD0 + rng.Next(8)) };//not rX
            case 23: return new byte[] { 0x48, 0xF7, (byte)(0xD8 + rng.Next(8)) };//neg rX
            case 24: return new byte[] { 0x0F, 0x1F, 0x40, 0x00 };//多字节nop
            case 25: return new byte[] { 0xF3, 0x90 };//pause
            case 26: return new byte[] { 0xEB, (byte)(1 + rng.Next(127)) };//jmp rel8正向前跳
            case 27: { var rgI = new byte[6]; rgI[0] = 0x0F; rgI[1] = (byte)(0x80 + rng.Next(16)); BitConverter.GetBytes(rng.Next(0, 0x20000)).CopyTo(rgI, 2); return rgI; }//jcc rel32
            case 28: { var rgI = new byte[5]; rgI[0] = 0xE8; BitConverter.GetBytes(0).CopyTo(rgI, 1); return rgI; }//call rel32(0)
            case 29: return new byte[] { 0x48, 0x0F, 0xB6, (byte)(0x40 + rng.Next(8)), (byte)rng.Next(256) };//movzx rX,byte [rAX+off]
            case 30: return new byte[] { 0x48, 0x0F, 0xBF, (byte)(0x40 + rng.Next(8)), (byte)rng.Next(256) };//movsx rX,word [rAX+off]
            case 31: { var rgI = new byte[8]; rgI[0] = 0x48; rgI[1] = 0x69; rgI[2] = (byte)(0xC0 + rng.Next(8)); rgI[3] = (byte)(0xC0 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 4); return rgI; }//imul rX,rY,imm32
            case 32: { var rgI = new byte[4]; rgI[0] = 0x48; rgI[1] = 0x89; rgI[2] = (byte)(0x40 + rng.Next(8)); rgI[3] = (byte)rng.Next(256); return rgI; }//mov [rAX+off],rX
            case 33: { var rgI = new byte[8]; rgI[0] = 0x48; rgI[1] = 0xC7; rgI[2] = (byte)(0x40 + rng.Next(8)); rgI[3] = (byte)rng.Next(256); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 4); return rgI; }//mov qword [rAX+off],imm32
            case 34: return new byte[] { 0x48, 0xC1, (byte)(0xC8 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//ror rX,imm8
            case 35: return new byte[] { 0x48, 0xC1, (byte)(0xC0 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//rol rX,imm8
            case 36: return new byte[] { 0x48, 0x8D, (byte)(0x80 + rng.Next(8)), 0, 0, 0, 0 };//lea rX,[rAX+0]
            case 37: return new byte[] { 0x48, 0x3B, (byte)(0xC0 + rng.Next(8)) };//cmp rAX,rX
            case 38: return new byte[] { 0x48, 0x23, (byte)(0xC0 + rng.Next(8)) };//and rAX,rX
            case 39: return new byte[] { 0x48, 0x33, (byte)(0xC0 + rng.Next(8)) };//xor rAX,rX

            default: return new byte[] { 0xC3 };//ret
        }
    }

    private static byte[] DcryptInsn(Random rng)
    {
        switch (rng.Next(8))
        {
            case 0: return new byte[] { 0x05, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//add eax,imm32
            case 1: return new byte[] { 0x2D, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//sub eax,imm32
            case 2: return new byte[] { 0x35, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//xor eax,imm32
            case 3: return new byte[] { 0xC1, 0xC0, (byte)(1 + rng.Next(31)) };//rol eax,imm8
            case 4: return new byte[] { 0xC1, 0xC8, (byte)(1 + rng.Next(31)) };//ror eax,imm8
            case 5: return new byte[] { 0x0F, 0xC8 };//bswap eax
            case 6: return new byte[] { 0xF7, 0xD0 };//not eax
            default: return new byte[] { 0xF7, 0xD8 };//neg eax
        }
    }

    private static byte[] PpcWord(uint u)
    {
        return new byte[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u };
    }

    private static int PpcReg(Random rng)
    {
        int i = rng.Next(24);
        if (i < 8) return new[] { 0, 3, 4, 5, 6, 11, 12, 31 }[rng.Next(8)];
        if (i < 16) return 7 + rng.Next(5);
        if (i < 20) return 13 + rng.Next(6);
        return 20 + rng.Next(12);
    }

    private static byte[] IlToPpc(Random rng, byte bOp)
    {
        int iRD = PpcReg(rng), iRA = PpcReg(rng), iRB = PpcReg(rng);
        uint u;
        switch (bOp)
        {
            case 0x00: return PpcWord(24u << 26);                               //nop = ori r0,r0,0
            case 0x02: case 0x03: case 0x04: case 0x05:                         //ldarg.N -> lwz rD, 8+4N(r1)
                return PpcWord((32u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(8 + 4 * (bOp - 0x02)));
            case 0x06: case 0x07: case 0x08: case 0x09:                         //ldloc.N -> lwz rD, -(8+4N)(r1)
                return PpcWord((32u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x06)) & 0xFFFF));
            case 0x0A: case 0x0B: case 0x0C: case 0x0D:                         //stloc.N -> stw rD, -(8+4N)(r1)
                return PpcWord((36u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x0A)) & 0xFFFF));
            case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A: case 0x1B: case 0x1C: case 0x1D: case 0x1E://ldc.i4.N -> li rD,N
                return PpcWord((14u << 26) | ((uint)iRD << 21) | (uint)(bOp - 0x16));
            case 0x28: return PpcWord((18u << 26) | 1u);                        //call -> bl +0
            case 0x58: u = 266u; break;                                         //add
            case 0x59: u = 40u; break;                                          //sub -> subf
            case 0x5A: u = 235u; break;                                         //mul -> mullw
            case 0x5B: case 0x5D: u = 491u; break;                              //div/rem -> divw
            case 0x5F: u = 28u; break;                                          //and
            case 0x60: u = 444u; break;                                         //or
            case 0x61: u = 316u; break;                                         //xor
            case 0x62: u = 24u; break;                                          //shl -> slw
            case 0x63: u = 536u; break;                                         //shr -> srw
            case 0x65: return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRA << 16) | (104u << 1));//neg rD,rA
            case 0x66: return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRB << 16) | ((uint)iRB << 11) | (124u << 1));//not -> nor rD,rB,rB
            case 0x2A: return PpcWord((19u << 26) | (20u << 21) | (16u << 1));  //ret -> blr
            case 0x2C: return PpcConcat(PpcWord((11u << 26) | ((uint)iRA << 16)), PpcWord((16u << 26) | (12u << 21) | (2u << 16)));//brfalse -> cmpwi rA,0; beq +0
            case 0x2D: return PpcConcat(PpcWord((11u << 26) | ((uint)iRA << 16)), PpcWord((16u << 26) | (4u << 21) | (2u << 16)));//brtrue -> cmpwi rA,0; bne +0
            case 0x70: return PpcRound(rng, 0);
            case 0x71: return PpcRound(rng, 1);
            case 0x72: return PpcRound(rng, 2);
            case 0x73: return PpcTbl(rng);
            case 0x74: return PpcByteSwap(rng);
            case 0x75: return PpcBarrier(rng);
            case 0x76: return PpcConcat(PpcWord((31u << 26) | (8u << 16) | (339u << 1)), PpcWord((31u << 26) | (8u << 16) | (467u << 1)));
            case 0x77: return PpcConcat(PpcWord((31u << 26) | ((uint)PpcReg(rng) << 21) | (854u << 1)), PpcWord((19u << 26) | (150u << 1)));

            default: return PpcWord(24u << 26);
        }
        return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRA << 16) | ((uint)iRB << 11) | (u << 1));//算术通用
    }

    private static byte[] PpcConcat(byte[] rgA, byte[] rgB)
    {
        byte[] rgR = new byte[rgA.Length + rgB.Length];
        Buffer.BlockCopy(rgA, 0, rgR, 0, rgA.Length);
        Buffer.BlockCopy(rgB, 0, rgR, rgA.Length, rgB.Length);
        return rgR;
    }

    private static byte[] PpcConcat(byte[] rgA, byte[] rgB, byte[] rgC)
    {
        byte[] rgR = new byte[rgA.Length + rgB.Length + rgC.Length];
        Buffer.BlockCopy(rgA, 0, rgR, 0, rgA.Length);
        Buffer.BlockCopy(rgB, 0, rgR, rgA.Length, rgB.Length);
        Buffer.BlockCopy(rgC, 0, rgR, rgA.Length + rgB.Length, rgC.Length);
        return rgR;
    }

    //P2变体0 xor与rlwinm与addic 解密轮
    private static byte[] PpcRound(Random rng, int iV)
    {
        int rD = PpcReg(rng), rK = 12 + rng.Next(4);
        if (iV == 0) return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | ((uint)rK << 11) | (316u << 1)),
            PpcWord((21u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (5u << 11) | (0u << 6) | (31u << 1)),
            PpcWord((12u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)));
        if (iV == 1) return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | ((uint)rK << 11) | (60u << 1)),
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (3u << 11) | (24u << 1)),
            PpcWord((8u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)));
        return PpcConcat(
            PpcWord((12u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)),
            PpcWord((15u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)),
            PpcWord((20u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (8u << 11) | (8u << 6) | (31u << 1)));
    }

    //查表S-box lwzx+rlwimi
    private static byte[] PpcTbl(Random rng)
    {
        int rD = PpcReg(rng), rA = PpcReg(rng), rB = PpcReg(rng);
        return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | ((uint)rB << 11) | (87u << 1)),
            PpcWord((20u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (8u << 11) | (16u << 6) | (23u << 1)));
    }

    //字节序反转 lwbrx
    private static byte[] PpcByteSwap(Random rng)
    {
        int rD = PpcReg(rng), rA = PpcReg(rng), rB = PpcReg(rng);
        return PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | ((uint)rB << 11) | (790u << 1));
    }

    //缓存屏障 eieio+isync
    private static byte[] PpcBarrier(Random rng)
    {
        return PpcConcat(PpcWord((31u << 26) | (854u << 1)), PpcWord((19u << 26) | (150u << 1)));
    }


    private static byte[] BuildVmLure(Random rng)
    {
        var rngV = new Random(rng.Next());//独立rng种子 使两个VM壳不同
        List<byte> rgIl = new();
        List<byte> rgPpc = new();
        int iFrame = 32 + 32 * rngV.Next(16);
        int iSave = 8 + 8 * rngV.Next(4);
        //P1 IL opcode重映射 每实例不同
        byte[] rgP1 = new byte[0x80];
        for (int i = 0; i < 0x80; i++) rgP1[i] = (byte)i;
        for (int i = 0x7F; i > 0; i--) { int j = rngV.Next(i + 1); (rgP1[i], rgP1[j]) = (rgP1[j], rgP1[i]); }
        byte[] rgOps = { 0x00,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x16,0x17,0x18,0x19,0x1A,0x28,0x2A,0x2C,0x2D,0x65,0x66,0x70,0x71,0x72,0x73,0x74,0x75,0x76,0x77 };
        //PPC序言
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (339u << 1)));
        rgPpc.AddRange(PpcWord((36u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));
        rgPpc.AddRange(PpcWord((37u << 26) | (1u << 21) | (1u << 16) | (uint)(-iFrame & 0xFFFF)));
        int iN = 12 + rngV.Next(24);
        for (int i = 0; i < iN; i++)
        {
            byte bSem = rgOps[rngV.Next(rgOps.Length)];
            rgIl.Add(rgP1[bSem]);
            rgPpc.AddRange(IlToPpc(rngV, bSem));
        }
        rgIl.Add(rgP1[0x2A]); rgPpc.AddRange(IlToPpc(rngV, 0x2A));
        //PPC尾声
        rgPpc.AddRange(PpcWord((14u << 26) | (1u << 21) | (1u << 16) | (uint)iFrame));
        rgPpc.AddRange(PpcWord((32u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (467u << 1)));
        rgPpc.AddRange(PpcWord((19u << 26) | (20u << 21) | (16u << 1)));
        var rgHandlers = new List<byte[]>();
        int iNH = 6 + rngV.Next(6);
        for (int i = 0; i < iNH; i++)
        {
            using var h = new MemoryStream();
            int iC = 2 + rngV.Next(5);
            for (int j = 0; j < iC; j++) { byte[] rgInsn = DcryptInsn(rngV); h.Write(rgInsn, 0, rgInsn.Length); }
            h.Write(new byte[] { 0xC3 }, 0, 1);
            rgHandlers.Add(h.ToArray());
        }
        const ulong uBase = 0x180000000UL;
        int iTable = 47;
        int iHandlers = iTable + rgHandlers.Count * 8;
        int iIlIn = iHandlers + rgHandlers.Sum(x => x.Length) + 6;
        int iPpc = iIlIn + rgIl.Count;
        int iIlOut = iPpc + rgPpc.Count;
        byte[] rgDisp = new byte[]
        {
            0x48,0xBE,0,0,0,0,0,0,0,0,
            0x9C,
            0x41,0x57,
            0x41,0x56,
            0x8B,0x06,
            0x0F,0xC8,
            0x05,0,0,0,0,
            0x35,0,0,0,0,
            0xC1,0xC0,0x05,
            0x25,0xFF,0x00,0x00,0x00,
            0x48,0x8B,0x04,0xC5,0,0,0,0,
            0xFF,0xE0
        };
        Buffer.BlockCopy(BitConverter.GetBytes(uBase + (ulong)iPpc), 0, rgDisp, 2, 8);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rngV.Next()), 0, rgDisp, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rngV.Next()), 0, rgDisp, 25, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(uBase + (ulong)iTable)), 0, rgDisp, 41, 4);
        using var ms = new MemoryStream();
        ms.Write(rgDisp, 0, rgDisp.Length);
        for (int i = 0; i < rgHandlers.Count; i++)
        {
            int iH = iHandlers + rgHandlers.Take(i).Sum(x => x.Length);
            ms.Write(BitConverter.GetBytes((ulong)(uint)(uBase + (ulong)iH)), 0, 8);
        }
        foreach (var h in rgHandlers) ms.Write(h, 0, h.Length);
        ms.Write(new byte[] { 0x41,0x5E, 0x41,0x5F, 0x9D, 0xC3 }, 0, 6);
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        ms.Write(rgPpc.ToArray(), 0, rgPpc.Count);
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        return ms.ToArray();
    }

    private static byte[] BuildFakePe(Random rng)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 }, 0, 16);
        ms.Write(new byte[0x20], 0, 0x20);
        byte[] rgDos = Encoding.ASCII.GetBytes("This program cannot be run in DOS mode.\r\n\r\n$");
        ms.Write(rgDos, 0, rgDos.Length);
        ms.Position = 0x3C;
        ms.Write(new byte[] { 0x80, 0x00, 0x00, 0x00 }, 0, 4);//e_lfanew -> PE
        ms.Position = 0x80;
        ms.Write(Encoding.ASCII.GetBytes("PE\0\0"), 0, 4);
        var writer = new BinaryWriter(ms, Encoding.ASCII, true);
        writer.Write((ushort)0x8664);//Machine x64
        writer.Write((ushort)2);     //NumberOfSections
        writer.Write(rng.Next());    //TimeDateStamp
        writer.Write(0u); writer.Write(0u);//PtrToSymbolTable / NumSymbols
        writer.Write((ushort)0xF0);  //SizeOfOptionalHeader
        writer.Write((ushort)0x2022);//Characteristics
        writer.Write((ushort)0x20B); //Magic PE32+
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
        writer.Write(0x180000000UL); //ImageBase
        writer.Write(0x1000u); writer.Write(0x200u);
        writer.Write((ushort)6); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write(0x4000u);       //SizeOfImage
        writer.Write(0x400u);        //SizeOfHeaders
        writer.Write(0u);            //Checksum
        writer.Write((ushort)3);     //Subsystem console
        writer.Write((ushort)0);     //DllCharacteristics
        writer.Write(0x100000UL); writer.Write(0x1000UL); writer.Write(0x100000UL); writer.Write(0x1000UL);
        writer.Write(0u); writer.Write(0u);
        for (int i = 0; i < 16; i++) { writer.Write(0u); writer.Write(0u); }//数据目录
        writer.Write(Encoding.ASCII.GetBytes(".text\0\0\0")); writer.Write(0x1000u); writer.Write(0x1000u); writer.Write(0x400u); writer.Write(0x200u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0x60000020u);
        writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(0x2000u); writer.Write(0x200u); writer.Write(0x600u); writer.Write(0x600u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0x40000040u);
        writer.Write(GenRandomX64(rng, 64, 256));//假代码段
        byte[] rgRes = ms.ToArray();
        //2字节交错 0x80起MZ与DOS头明文保留
        for (int i = 0x80; i + 1 < rgRes.Length; i += 2)
        {
            byte byTmp = rgRes[i]; rgRes[i] = rgRes[i + 1]; rgRes[i + 1] = byTmp;
        }
        return rgRes;
    }

    #endregion
    #region 打包与密码

    private static uint Fnv1a(byte[] rgD)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte bX in rgD) { uH ^= bX; uH *= uLK1A ^ uLK1B; }
        return uH;
    }

    private static uint MixAdj(byte[] rgKey)
    {
        uint uA = rgKey[0] | ((uint)rgKey[1] << 8) | ((uint)rgKey[2] << 16) | ((uint)rgKey[3] << 24);
        uint uB = rgKey[4] | ((uint)rgKey[5] << 8) | ((uint)rgKey[6] << 16) | ((uint)rgKey[7] << 24);
        uint uM = uA ^ uB ^ 0x811C9DC5u;
        return uM != 0 ? uM : 0x811C9DC5u;
    }

    private static byte[] SeedKey(byte[] rgSeed)
    {
        byte[] rgKey = new byte[16];
        uint uA = 0x811C9DC5u, uB = 0x1B0CA2B5u;
        for (int i = 0; i < 16; i++)
        {
            uA ^= rgSeed[i]; uA *= 0x01000193u;
            uB ^= rgSeed[i + 16]; uB *= 0x9E3779B9u;
            uint uT = (uA ^ (uB << 1)) + (uB ^ (uA >> 3));
            rgKey[i] = (byte)((uT >> 16) ^ (uT >> 24) ^ rgSeed[i]);
        }
        return rgKey;
    }

    private static byte[] XorBytes(byte[] rgD, byte[] rgKey, uint uAdj)
    {
        byte[] rgR = new byte[rgD.Length];
        for (int i = 0; i < rgD.Length; i++) rgR[i] = (byte)(rgD[i] ^ rgKey[i % rgKey.Length] ^ (byte)(uAdj >> (8 * (i % 4))));
        return rgR;
    }

    private static uint LamKey(string sVal)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (char ch in sVal)
        {
            uH ^= ch;
            uH *= uLK1A ^ uLK1B;
        }
        return uH;
    }

    private static int[] LamPerm(uint uK)
    {
        int[] rgA = { 0, 1, 2, 3 };
        uint uS = uK;
        for (int i = 3; i > 0; i--)
        {
            uS = uS * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            int iJ = (int)(uS % (uint)(i + 1));
            (rgA[i], rgA[iJ]) = (rgA[iJ], rgA[i]);
        }
        return rgA;
    }

    private static uint LamSlot(uint uK, int i) => uK + (uLGAA ^ uLGAB) * (uint)i;

    private static void LamWriteXor(byte[] rgB, int iOff, uint uV, uint uX)
        => BitConverter.GetBytes(uV ^ uX).CopyTo(rgB, iOff);

    //重建bundle boot替换主程序条目 数据写在rdata之后
    private void BuildBundleDataAndHeader(uint uMajor, string sBundleId, List<int> rgKeepIdx,
        long[] rgRel, long[] rgSz, long[] rgCsz, byte[] rgType, string[] rgName,
        long lDepsSz, long lRtcSz, long lRtcHash)
    {
        int iM = rgKeepIdx.Count;
        rgBundleOffsets = new long[iM];
        rgBundleCsz = new long[iM];
        rgBundleSz = new long[iM];

        using var ms = new MemoryStream();
        long lDepsSzNew = lDepsSz;
        long lRtcSzNew = lRtcSz;
        for (int k = 0; k < iM; k++)
        {
            int i = rgKeepIdx[k];
            byte[] rgData;
            if (i == iMainEntry)
            {
                rgData = rgBoot;
            }
            else
            {
                long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
                rgData = new byte[lOndisk];
                Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)lOndisk);
                if (rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                {
                    rgData = StripDepsDependencies(rgData);
                    lDepsSzNew = rgData.Length;
                }
                else if (rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                {
                    rgData = RewriteRuntimeConfig(rgData);
                    lRtcSzNew = rgData.Length;
                }
            }
            rgBundleOffsets[k] = ms.Position;
            ms.Write(rgData, 0, rgData.Length);
            rgBundleCsz[k] = i == iMainEntry ? 0 : (rgCsz[i] > 0 ? rgData.Length : 0);
            rgBundleSz[k] = i == iMainEntry
                || rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                ? rgData.Length : rgSz[i];
        }
        rgBundleData = ms.ToArray();
        lBundleDataLen = rgBundleData.Length;
        iEntryCount = iM;
        lDepsSz = lDepsSzNew;

        iNewRtcIdx = -1;
        for (int k = 0; k < iM; k++)
        {
            if (rgName[rgKeepIdx[k]].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            { iNewRtcIdx = k; break; }
        }

        uint uOutMajor = uMajor >= 2 ? 2u : uMajor;
        using var hd = new MemoryStream();
        WriteU32(hd, uOutMajor);
        WriteU32(hd, 0);
        WriteI32(hd, iM);
        WriteStr(hd, sBundleId);

        if (uMajor >= 2)
        {
            int iKDeps = -1;
            for (int k = 0; k < iM; k++)
            {
                if (rgName[rgKeepIdx[k]].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                { iKDeps = k; break; }
            }
            WriteI64(hd, iKDeps >= 0 ? lBundleStart + rgBundleOffsets[iKDeps] : 0);
            WriteI64(hd, lDepsSz);
            WriteI64(hd, iNewRtcIdx >= 0 ? lBundleStart + rgBundleOffsets[iNewRtcIdx] : 0);
            WriteI64(hd, lRtcSzNew);
            WriteI64(hd, lRtcHash);
        }

        for (int k = 0; k < iM; k++)
        {
            WriteI64(hd, lBundleStart + rgBundleOffsets[k]);
            WriteI64(hd, rgBundleSz[k]);
            if (uOutMajor >= 6)
                WriteI64(hd, rgBundleCsz[k]);
            WriteU8(hd, rgType[rgKeepIdx[k]]);
            WriteStr(hd, rgName[rgKeepIdx[k]]);
        }
        rgNewHeader = hd.ToArray();
    }
    #endregion
}
