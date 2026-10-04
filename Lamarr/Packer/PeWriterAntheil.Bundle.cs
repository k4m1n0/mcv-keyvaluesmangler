using Lamarr;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Numerics;
namespace Lamarr.Packer;

internal partial class PeWriterAntheil
{
    #region bundle重建

    private long FindMarkerOffset(byte[] rgB)
    {
        for (int i = 0; i + 40 <= rgB.Length; i++)
            if (rgB[i + 8] == rgSignatureDotnet[0] && MatchSig(rgB, i + 8))
                return BitConverter.ToInt64(rgB, i);
        return -1;
    }

    private bool MatchSig(byte[] rgB, int iOff)
    {
        for (int j = 0; j < 32; j++)
            if (rgB[iOff + j] != rgSignatureDotnet[j]) return false;
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

    //容器区域表与条目表的构建 位域与字节序随载体指令集
private void RebuildBundle(string sSeed)
    {
        rgKBsjb = GenKBsjb(sSeed);
        rgSigOut = GenAnchorSig(sSeed);//标记记录与入口层校验与shim搜索键都等于本产物的派生锚点 与##KBSJB##域分离
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

        if (iN <= 0 || iN > 0x1000) throw new InvalidOperationException($"bundle entry count out of range: {iN}");
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

        //区分条目 主程序集与引导保留 托管依赖剥离 其余进新容器
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
        if (bLayout)//构建侧条目清单 只走stdout 不进产物
            Console.WriteLine($"[layout] major={uMajor} iN={iN} bundleId={sBundleId} keep={rgKeepIdx.Count} dep={rgDepIdx.Count} deps=" +
                string.Join(",", rgDepIdx.ConvertAll(i => $"{i}:{rgName[i]}:{rgSz[i]}:{rgCsz[i]}")));

        ComputeBundleStart();
        BuildBundleDataAndHeader(uMajor, sBundleId, rgKeepIdx, rgRel, rgSz, rgCsz, rgType, rgName,
                                 lDepsSz, lRtcSz, lRtcHash);
    }

    private static uint RegionFieldMask(byte[] rgSiteKey, int r, int iField)//区域表type/off/len掩码 两端由同一张表算出 材料=站点键四字+条目号+字段号
    {
        uint u = BitConverter.ToUInt32(rgSiteKey, 0) ^ (BitConverter.ToUInt32(rgSiteKey, 4) + (uint)r)
               ^ (BitConverter.ToUInt32(rgSiteKey, 8) * (uint)(iField + 1)) ^ BitConverter.ToUInt32(rgSiteKey, 12);
        u = (u ^ (u >> 15)) * uPermMul;//0x01000193两端同值的常量对
        u = (u ^ (u >> 13)) + uPermAdd;//0x9E3779B9两端同值的常量对
        return u ^ (u >> 16);
    }

    private byte[] MergeMainStream(byte[] rgComp0, byte[]? rgDec, byte[]? rgJit, byte[]? rgSig, byte[]? rgVm, byte[]? rgRoll, Random rng, byte[] rgSiteKey)//主程序压缩流 多区混入解码器与jithook与签名表与垃圾区
    {
        var rgRegions = new List<(int iType, byte[] data, bool bRev)>();//附加区在尾部 供X3直接解压
        if (rgDec?.Length > 0) rgRegions.Add((1, rgDec, true));
        if (rgJit?.Length > 0) rgRegions.Add((2, rgJit, true));
        if (rgSig?.Length > 0) rgRegions.Add((3, rgSig, true));
        if (rgVm != null && rgVm.Length > 0)
        {
            rgRegions.Add((6, rgVm, true));
            if (!bNoEngineSum)//引擎摘要region由u32镜像长度与u32 .text摘要构成 运行期碰引擎前核对 缺失或不符即毁状态加FailFast 单独篡改region 6必须连region 8一起改
            {
                byte[] rgSum8 = new byte[8];
                BitConverter.GetBytes((uint)rgVm.Length).CopyTo(rgSum8, 0);
                BitConverter.GetBytes(bBadEngineSum ? EngineTextSum(rgVm) ^ 0x5A5A5A5Au : EngineTextSum(rgVm)).CopyTo(rgSum8, 4);
                rgRegions.Add((8, rgSum8, true));//真实区一律整体反转 取出时反转还原
            }
        }
        if (rgRoll?.Length > 0) rgRegions.Add((7, rgRoll, true));
        int iGx = rng.Next(2, 5), iGi = rng.Next(2, 5);
        for (int i = 0; i < iGx; i++) rgRegions.Add((4, GenRandomX64(rng, 0x80, 0x200), false));
        for (int i = 0; i < iGi; i++) rgRegions.Add((5, GenShapeArea(rng.Next(0x80, 0x200)), false));
        if (rgRegions.Count == 0) return rgComp0;
        for (int i = rgRegions.Count - 1; i > 0; i--)//区域顺序随构建打乱 真实区先后与相邻关系不作保证 boot按type查表
        {
            int j = rng.Next(i + 1);
            (rgRegions[i], rgRegions[j]) = (rgRegions[j], rgRegions[i]);
        }
        if (bLayout)//构建侧区域清单 只走stdout
        {
            var sTypes = string.Join(",", rgRegions.ConvertAll(r => r.iType + "-" + r.data.Length));
            Console.WriteLine($"[layout] regions gx={iGx} gi={iGi} count={rgRegions.Count} types={sTypes}");
        }
        int iHdr = 8 + rgRegions.Count * 12 + 4;
        int iCompLen = rgComp0.Length;
        int iAtt = iHdr + iCompLen;
        byte[] rgOut = new byte[iAtt + rgRegions.Sum(r => r.data.Length)];
        BitConverter.GetBytes(rgRegions.Count).CopyTo(rgOut, 0);
        BitConverter.GetBytes(0x80).CopyTo(rgOut, 4);
        int iOff = 0;
        for (int r = 0; r < rgRegions.Count; r++)
        {
            BitConverter.GetBytes(rgRegions[r].iType ^ (int)RegionFieldMask(rgSiteKey, r, 0)).CopyTo(rgOut, 8 + r * 12);//type/off/len逐项掩码 容器整体XOR之外的第二层
            BitConverter.GetBytes(iOff ^ (int)RegionFieldMask(rgSiteKey, r, 1)).CopyTo(rgOut, 12 + r * 12);
            BitConverter.GetBytes(rgRegions[r].data.Length ^ (int)RegionFieldMask(rgSiteKey, r, 2)).CopyTo(rgOut, 16 + r * 12);
            iOff += rgRegions[r].data.Length;
        }
        BitConverter.GetBytes(iCompLen).CopyTo(rgOut, 8 + rgRegions.Count * 12);
        Array.Copy(rgComp0, 0, rgOut, iHdr, iCompLen);
        int iA = iAtt;
        foreach (var (iType, data, bRev) in rgRegions)
        {
            if (bRev) for (int r = 0; r < data.Length / 2; r++) (data[r], data[data.Length - 1 - r]) = (data[data.Length - 1 - r], data[r]);
            Array.Copy(data, 0, rgOut, iA, data.Length);
            iA += data.Length;
        }
        return rgOut;
    }

private static byte[] PolyTag(byte[] rgSeedKey, byte[] rgComp, int iLen)
    {
        byte[] rgKey = MethodEncryptor.KdfChaCha(BitConverter.ToUInt64(rgSeedKey, 0));
        byte[] rgBody = new byte[iLen];
        Array.Copy(rgComp, 0, rgBody, 0, iLen);
        return Poly1305(rgKey, rgBody);
    }

    private static byte[] Poly1305(byte[] rgKey, byte[] rgMsg)
    {
        var r0 = new byte[16];
        Array.Copy(rgKey, r0, 16);
        r0[3] &= 15; r0[7] &= 15; r0[11] &= 15; r0[15] &= 15;
        r0[4] &= 252; r0[8] &= 252; r0[12] &= 252;
        var r = new BigInteger(r0, isUnsigned: true);
        var s = new BigInteger(rgKey[16..32], isUnsigned: true);
        var p = (BigInteger.One << 130) - 5;
        var h = BigInteger.Zero;
        for (int i = 0; i < rgMsg.Length; i += 16)
        {
            int n = Math.Min(16, rgMsg.Length - i);
            var rgBlk = new byte[n + 1];
            Array.Copy(rgMsg, i, rgBlk, 0, n);
            rgBlk[n] = 1;
            h = ((h + new BigInteger(rgBlk, isUnsigned: true)) * r) % p;
        }
        h = (h + s) % (BigInteger.One << 128);
        var rgTag = new byte[16];
        var bb = h.ToByteArray();
        for (int i = 0; i < 16 && i < bb.Length; i++) rgTag[i] = bb[i];
        return rgTag;
    }

    #endregion
    #region 密钥派生

    private int iShapeCall;//区域内容纯随机 与头部/诱饵同分布 由种子与头部序号派生
    private byte[] GenShapeArea(int cb)
    {
        byte[] rgB = new byte[cb];
        ulong uS = ShapeMix((ulong)iShapeCall++);
        for (int i = 0; i < cb; i++)
        {
            if ((i & 7) == 0) uS = NextShape(uS);
            rgB[i] = (byte)(uS >> ((i & 7) * 8));
        }
        return rgB;
    }

    //形状流的种子 不同头部给出不同形状
    private ulong ShapeMix(ulong uIdx)
    {
        ulong uH = 0;
        if (rgHead != null)
            for (int i = 0; i < 8 && i < rgHead.Length; i++) uH |= (ulong)rgHead[i] << (i * 8);
        return NextShape(uVmSeed ^ uH ^ (uIdx * 0x9E3779B97F4A7C15UL));
    }

    private static ulong NextShape(ulong u)
    {
        u += 0x9E3779B97F4A7C15UL;
        u = (u ^ (u >> 30)) * 0xBF58476D1CE4E5B9UL;
        u = (u ^ (u >> 27)) * 0x94D049BB133111EBUL;
        return u ^ (u >> 31);
    }

    //头材料是域分离哈希的输出 不可逆 用splitmix链就能从产物头部反推出uVmSeed 而它驱动载体ISA与运行期表
    private static byte[] GenHead(byte[] rgK) => Sha512.Hash(KdfLabel(rgK, "chain:head"));

    //根密钥K是32B 只在打包进程内存在 写escrow时才留档 产物里不得出现任何KdfLabel输出
    internal static byte[] KdfLabel(byte[] rgK, string sLabel)
        => Sha512.Hash(Encoding.ASCII.GetBytes("lamarr-m1|" + sLabel + "|"), rgK, null);

    internal static ulong Kdf64(byte[] rgK, string sLabel) => BitConverter.ToUInt64(KdfLabel(rgK, sLabel), 0);

    //输出是32个大写hex 下游SeedKey与LamKey与GenKBsjb都按这个形状取用
    internal static string KdfSeedHex(byte[] rgK) => Convert.ToHexString(KdfLabel(rgK, "chain:seed")).Substring(0, 32);

    private static uint DeriveBranch(byte[] rgH, int iOff, int iLen, uint uSalt)//分支种子从密钥区子段派生 给VM壳与诱饵与垃圾
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

    //锚点键必须与stub的##KBSJB##域分离 否则产物里会出现两处相同的32B 扫描先命中stub那份会把前8字节当头部偏移
    private static byte[] GenAnchorSig(string sSeed) => GenKBsjb(sSeed + "|anchor");

    private static byte[] GenKBsjb(string sSeed)//假BSJB头密钥由种子派生随机生成 写入stub的##KBSJB##槽
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

    private static byte[] GenDisturb(byte[] rgH)//明文依赖块的扰动键由根材料派生 每产物不同
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

    static void DisturbHead(byte[] rgD, byte[] rgH)
    {
        byte[] rgK = GenDisturb(rgH);
        int n = Math.Min(1024, rgD.Length);
        for (int i = 0; i < n; i++)
            rgD[i] = (byte)(rgD[i] ^ rgK[i & 15]);
    }

private static uint Fnv1a(byte[] rgD)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte bX in rgD) { uH ^= bX; uH *= uLK1A ^ uLK1B; }
        return uH;
    }

    private static uint Fnv1aK1(byte[] rgD)//jit密钥专用派生常量与其它派生分开
    {
        uint uH = 0x2F1E4B7Du;
        foreach (byte bX in rgD) { uH ^= bX; uH *= 0x9E3B7A11u; }
        return uH;
    }

    internal static ulong MaskKeyOf(byte[] rgMat)//签名表掩码专用派生 与jit密钥域用不同常量 表项=crc2^mask32只含本域函数值 否则2^32候选能筛出uKey
    {
        ulong uH = 0x6A09E667F3BCC909UL;
        foreach (byte bX in rgMat) { uH ^= bX; uH *= 0xC2B2AE3D27D4EB4FUL; uH ^= uH >> 29; }
        return uH;
    }

    //入口层与VM内核共用的材料 两侧必须喂同一串字节
    internal static byte[] VmMaterial(byte[] rgHead, uint uDomainSeed)
    {
        byte[] rgMat = new byte[32];
        BitConverter.GetBytes(uDomainSeed).CopyTo(rgMat, 0);
        int n = Math.Min(28, rgHead.Length);
        Array.Copy(rgHead, 0, rgMat, 4, n);
        return rgMat;
    }

    //主压缩流第二层用的内核输出 种子内核对roll头32B的材料算得
    internal static byte[] VmSeedKeyOf(byte[] rgRoll)
    {
        byte[] rgMat = new byte[32];
        Array.Copy(rgRoll, 0, rgMat, 0, Math.Min(32, rgRoll.Length));
        return SeedKey(rgMat);
    }

    internal static ulong Fnv64Of(byte[] rg, int cb)//构建侧判据用 对同一段材料算同一个哈希
    {
        ulong u = 0xCBF29CE484222325uL;
        for (int i = 0; i < cb; i++) { u ^= rg[i]; u *= 0x100000001B3uL; }
        return u;
    }

    //入口层折叠量 掩码内核与种子内核在各自材料上的输出 打包器用明文回声对出同一值
    internal static uint VmMaskEcho(byte[] rgHead, uint uDomainSeed, uint uRollKey)
        => (uint)MethodEncryptor.MaskOf(MaskKeyOf(VmMaterial(rgHead, uDomainSeed)))
           ^ BitConverter.ToUInt32(SeedKey(VmMaterial(rgHead, uRollKey)), 0);

    //前置内核在域种子材料上的输出回声Host侧由入口层那枚单内核容器执行得到同一个值
    internal static uint PreKernEcho(byte[] rgHead, uint uDomainSeed)
        => (uint)MethodEncryptor.MaskOf(MaskKeyOf(VmMaterial(rgHead, uDomainSeed)));

    internal static uint EngineTextSum(byte[]? rgDll) => rgDll == null ? 0u : PianolaEngine.TextSum(rgDll);//对引擎镜像.text做滚动摘要

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

    private static uint LamMix(uint u)//混合器只用已有常量对 两步异或移位与两个奇数乘都是一一对应 下标不同pad不同
    {
        u ^= u >> 16; u *= (uLLCA ^ uLLCB);
        u ^= u >> 13; u *= (uLQCA ^ uLQCB);
        u ^= u >> 16;
        return u;
    }

    private static int[] LamPermAt(uint uK, int i)//逐entry置换 全局只有4!=24类 知道任意一行就等于知道全表顺序
    {
        int[] rgA = { 0, 1, 2, 3 };
        uint uS = LamMix(LamSlot(uK, i + 1));
        for (int k = 3; k > 0; k--)
        {
            uS = uS * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            int iJ = (int)(uS % (uint)(k + 1));
            (rgA[k], rgA[iJ]) = (rgA[iJ], rgA[k]);
        }
        return rgA;
    }

    private static uint LamSlot(uint uK, int i) => uK + (uLGAA ^ uLGAB) * (uint)i;

    private static uint LamFieldKey(uint uK, int i, int iS) => LamMix(LamSlot(uK, i * 4 + iS + 1));//逐字段独立pad 同pad下stored[a]^stored[b]==raw[a]^raw[b]会泄露行内关系 诱饵行两长度字段相等则该差为0

    private static void LamWriteXor(byte[] rgB, int iOff, uint uV, uint uX)
        => BitConverter.GetBytes(uV ^ uX).CopyTo(rgB, iOff);

    private void BuildBundleDataAndHeader(uint uMajor, string sBundleId, List<int> rgKeepIdx,//重建bundle boot替换主程序条目数据写在rdata之后
        long[] rgRel, long[] rgSz, long[] rgCsz, byte[] rgType, string[] rgName,
        long lDepsSz, long lRtcSz, long lRtcHash)
    {
        int iM = rgKeepIdx.Count;
        rgBundleOffsets = new long[iM];
        rgBundleCsz = new long[iM];
        rgBundleSz = new long[iM];

        using var ms = new MemoryStream();
        var rgEnt = new byte[iM][];//入口层的密钥材料含全条目尺寸表 先备齐非主条目 主条目最后按尺寸表拼
        int iKMain = -1;
        for (int k = 0; k < iM; k++)
        {
            int i = rgKeepIdx[k];
            if (i == iMainEntry) { iKMain = k; continue; }
            long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
            byte[] rgData = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)lOndisk);
            if (rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                rgData = StripDepsDependencies(rgData);
                rgBundleSz[k] = rgData.Length;
            }
            else if (rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            {
                rgData = RewriteRuntimeConfig(rgData);
                rgBundleSz[k] = rgData.Length;
            }
            else
            {
                rgBundleSz[k] = rgSz[i];
            }
            rgEnt[k] = rgData;
        }
        if (iKMain < 0)
            throw new InvalidOperationException("Bundle main entry missing from the keep list");

        long lDepsSzNew = lDepsSz;
        long lRtcSzNew = lRtcSz;
        for (int k = 0; k < iM; k++)
        {
            int i = rgKeepIdx[k];
            if (rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) lDepsSzNew = rgBundleSz[k];
            else if (rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) lRtcSzNew = rgBundleSz[k];
        }

        rgBundleCsz[iKMain] = 0;//主条目是boot本体 或入口层镜像加加密boot加16B尾部记录
        if (rgShim == null)
        {
            rgEnt[iKMain] = rgBoot;
            rgBundleSz[iKMain] = rgBoot.Length;
        }
        else
        {
            rgBundleSz[iKMain] = rgShim.Length + (rgRoll?.Length ?? 0) + (rgEngine?.Length ?? 0) + (rgPreKern?.Length ?? 0) + rgBoot.Length + A0.Q.iTailLen;
            rgEnt[iKMain] = BuildShimEntry(rgBundleSz[iKMain], rgBundleSz);
        }
        for (int k = 0; k < iM; k++)
        {
            int i = rgKeepIdx[k];
            if (k != iKMain) rgBundleCsz[k] = rgCsz[i] > 0 ? rgEnt[k].Length : 0;
            rgBundleOffsets[k] = ms.Position;
            ms.Write(rgEnt[k], 0, rgEnt[k].Length);
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
