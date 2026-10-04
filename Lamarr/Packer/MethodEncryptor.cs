using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.Packer;

public static class MethodEncryptor
{
    //本批方法的全局签名表起始序号
#if LAMARR_TRIAGE
    private static readonly int iVariantShift =
        int.TryParse(Environment.GetEnvironmentVariable("LAMARR_VARIANT_SHIFT") ?? "", out int iVs) ? (iVs & 3) : 0;
#else
    private static readonly int iVariantShift = 0;//发行构建恒0 不可被环境变量位移
#endif

    public static List<uint> EncryptAll(byte[] rgD, ulong uKey, ulong uMaskKey, IReadOnlyCollection<uint>? rgOnly = null, int iBase = 0, List<byte>? rgVariants = null, int[]? rgPerm = null, List<int>? rgSlots = null)
    {
        int iShift = rgOnly == null ? iVariantShift : 0;//只对载荷体生效 启动期自身的体在钩子装好前已编译
        if (rgD.Length < 0x40) throw new InvalidDataException("too small");
        uint uLfaNew = ReadU32(rgD, 0x3C);
        int iPe = (int)uLfaNew;
        if (iPe <= 0 || iPe + 0x60 >= rgD.Length || ReadU32(rgD, iPe) != 0x4550)
            throw new InvalidDataException("no PE signature");
        ushort usNumSec = ReadU16(rgD, iPe + 6);
        ushort usOptSize = ReadU16(rgD, iPe + 20);
        ushort usMagic = ReadU16(rgD, iPe + 24);//0x10B/0x20B
        int iDdStart = iPe + 24 + (usMagic == 0x20B ? 112 : 96);

        uint uCliRva = ReadU32(rgD, iDdStart + 14 * 8);//DataDirectory[14]=CLI头
        if (uCliRva == 0) throw new InvalidDataException("no CLI header");
        int iCliOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uCliRva);

        var rgCrcs = new List<uint>();
        if ((uint)MaskOf(uMaskKey) == (uint)MaskOf(uKey))//掩码须来自独立域 否则表项能筛出密钥
            throw new InvalidOperationException("signature table mask and decrypt key share a source: maskKey miswired (reproduces the uKey shortcut)");
        uint uMaskLo = (uint)MaskOf(uMaskKey);
        int iLocal = 0;//本批内序号+iBase=全局序号
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var mdh in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(mdh);
            if (rgOnly != null)
            {
                uint uTok = 0x06000000u | (uint)MetadataTokens.GetRowNumber(mr, mdh);
                if (!rgOnly.Contains(uTok)) continue;
            }
            uint uRva = (uint)md.RelativeVirtualAddress;
            if (uRva == 0) continue;//平台调用与抽象与接口方法无方法体
            int iOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uRva);
            int iIlStart = ((rgD[iOff] & 3) == 2) ? iOff + 1 : iOff + 12;//tiny头1字节 fat头12字节
            int iEb = EncryptBody(rgD, iOff, uKey);
            if (iEb > 0)
            {
                //第二层密钥由钩子按全局序号现算 表里不存密钥 序号先过全空间置换再派生 无置换时恒等
            if (rgPerm != null && (uint)(iBase + iLocal) >= (uint)rgPerm.Length) throw new InvalidOperationException("method index space exhausted (slot table too small)");
            int iSlot = rgPerm == null ? iBase + iLocal : rgPerm[iBase + iLocal];
            ulong uKey2 = DeriveKey2(uKey, (uint)iSlot);
                int iVx = ((int)(uKey2 ^ uKey) + iShift) & 3;//第二层按方法变体号加密
                Xor(rgD, iIlStart, iEb, uKey2, iVx, 1);
                rgVariants?.Add((byte)iVx);//实际使用的变体也交给容器 jithook直接取用
            rgSlots?.Add(iSlot);
                uint uCrc2 = Crc32(rgD, iIlStart, iEb);
                rgCrcs.Add(uCrc2 ^ uMaskLo);//表项是32位的校验值与掩码异或 掩码来自独立域
                iLocal++;
            }
        }
        return rgCrcs;
    }

    //离线逆 槽位靠内容寻址恢复 表项是Crc32(层1输出)异或掩码
    //候选明文重加密回去必须与密文体逐字节相同 否则换变体或换候选槽位
    public static (int iEnc, int iOk, int iFail) DecryptAll(byte[] rgD, ulong uKey, ulong uMaskKey, byte[] rgSigBlob)
    {
        if (rgD.Length < 0x40) throw new InvalidDataException("too small");
        int iPe = (int)ReadU32(rgD, 0x3C);
        if (iPe <= 0 || iPe + 0x60 >= rgD.Length || ReadU32(rgD, iPe) != 0x4550)
            throw new InvalidDataException("no PE signature");
        ushort usNumSec = ReadU16(rgD, iPe + 6);
        ushort usOptSize = ReadU16(rgD, iPe + 20);
        ushort usMagic = ReadU16(rgD, iPe + 24);//0x10B/0x20B
        int iDdStart = iPe + 24 + (usMagic == 0x20B ? 112 : 96);
        if (ReadU32(rgD, iDdStart + 14 * 8) == 0) throw new InvalidDataException("no CLI header");

        uint uMaskLo = (uint)MaskOf(uMaskKey);
        var dic = new Dictionary<uint, List<int>>();
        if (rgSigBlob != null && rgSigBlob.Length >= 8 && ReadU32(rgSigBlob, 0) == 0x32474953u)//"SIG2"
        {
            int iNSig = (int)ReadU32(rgSigBlob, 4);
            if (iNSig <= 0 || 8L + (long)iNSig * 5 > rgSigBlob.Length)
                throw new InvalidDataException($"SIG2 table bad: count={iNSig} len={rgSigBlob.Length}");
            for (int i = 0; i < iNSig; i++)
            {
                uint u = ReadU32(rgSigBlob, 8 + i * 4);
                if (!dic.TryGetValue(u, out var rgSlot)) { rgSlot = new List<int>(1); dic[u] = rgSlot; }
                rgSlot.Add(i);
            }
        }

        int iEnc = 0, iOk = 0, iFail = 0;
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var mdh in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(mdh);
            uint uRva = (uint)md.RelativeVirtualAddress;
            if (uRva == 0) continue;//平台调用与抽象与接口方法无方法体
            int iOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uRva);
            byte b0 = rgD[iOff];
            int iIlStart = ((b0 & 3) == 2) ? iOff + 1 : iOff + 12;//tiny头1字节 fat头12字节
            int iEb = (b0 & 3) == 2 ? (b0 >> 2) : ((b0 & 3) == 3 ? (int)ReadU32(rgD, iOff + 4) : 0);
            if (iEb <= 0 || iEb > 0x00FFFFFF || iIlStart + iEb > rgD.Length) continue;

            byte[] rgSav = new byte[iEb];
            Array.Copy(rgD, iIlStart, rgSav, 0, iEb);
            uint uCrc = Crc32(rgD, iIlStart, iEb) ^ uMaskLo;
            if (!dic.TryGetValue(uCrc, out var rgCand)) continue;//不在表里 = 该体从未加密
            iEnc++;
            bool bDone = false;
            foreach (int iSlot in rgCand)
            {
                ulong uKey2 = DeriveKey2(uKey, (uint)iSlot);
                int iVx0 = ((int)(uKey2 ^ uKey) + iVariantShift) & 3;//变体号由key2与key加位移导出
                for (int iT = 0; iT < 4 && !bDone; iT++)
                {
                    int iVx = (iVx0 + iT) & 3;
                    byte[] rgT = (byte[])rgSav.Clone();
                    DeXor(rgT, 0, iEb, uKey2, iVx, 1);//先层1后层0 与加密顺序相反
                    DeXor(rgT, 0, iEb, uKey, 0, 0);
                    byte[] rgV = (byte[])rgT.Clone();
                    Xor(rgV, 0, iEb, uKey, 0, 0);//重加密回密文
                    Xor(rgV, 0, iEb, uKey2, iVx, 1);
                    bool bSame = true;
                    for (int k = 0; k < iEb; k++) if (rgV[k] != rgSav[k]) { bSame = false; break; }
                    if (!bSame) continue;
                    Array.Copy(rgT, 0, rgD, iIlStart, iEb);
                    bDone = true;
                }
            }
            if (bDone) iOk++; else iFail++;
        }
        return (iEnc, iOk, iFail);
    }

    private static void DeXor(byte[] rgD, int iOff, int iLen, ulong uKey, int iV, int iLayer)//Xor的逆 因为nibble置换表不是自逆 解密要用GenPerm产出的逆表
    {
        byte[] rgPH, rgPL, rgIH, rgIL;
        GenPerm(uKey, out rgPH, out rgPL, out rgIH, out rgIL);
        byte[] ckey = KdfChaCha(uKey);
        byte[] rgNonce = new byte[12];
        if (iLayer == 1)
        {
            W32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
            W32(rgNonce, 4, (uint)(uKey >> 32));
            W32(rgNonce, 8, 0x2B7E1516u ^ (uint)iV);
        }
        else
        {
            W32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
            W32(rgNonce, 4, 0);
            W32(rgNonce, 8, 0xC2B2AE35u);
        }
        byte[] ks = new byte[64];
        uint uCounter = 0;
        int ki = 0;
        ChaCha.Block(ckey, rgNonce, uCounter, ks, 20);
        ulong uPrev = 0;
        for (int i = 0; i < iLen; i++)
        {
            if (ki >= 64)
            {
                uCounter++;
                ChaCha.Block(ckey, rgNonce, uCounter, ks, 20);
                ki = 0;
            }
            byte byK = (byte)(ks[ki] ^ uPrev);
            byte byC = rgD[iOff + i];//uPrev与加密侧同为前一个密文字节 所以必须先读后写
            byte byM = (byte)((rgIH[byC >> 4] << 4) | rgIL[byC & 0xF]);
            rgD[iOff + i] = (byte)(byM ^ byK);
            uPrev = byC;
            ki++;
        }
    }

    private static int RvaToOffset(byte[] rgD, int iPe, ushort usOptSize, ushort usNumSec, uint uRva)
    {
        int iSecStart = iPe + 24 + usOptSize;
        for (int i = 0; i < usNumSec; i++)
        {
            int iS = iSecStart + i * 40;
            uint uVs = ReadU32(rgD, iS + 8);
            uint uVa = ReadU32(rgD, iS + 12);
            uint uRs = ReadU32(rgD, iS + 16);
            uint uPo = ReadU32(rgD, iS + 20);
            uint uEnd = Math.Max(uVs, uRs);
            if (uRva >= uVa && uRva < uVa + uEnd) return (int)(uPo + (uRva - uVa));
        }
        throw new InvalidDataException($"RVA 0x{uRva:X8} unmapped");
    }

    private static int EncryptBody(byte[] rgD, int iOff, ulong uKey)
    {
        byte b0 = rgD[iOff];
        if ((b0 & 3) == 2)//tiny头1字节codeSize=b0>>2
        {
            int iSize = b0 >> 2;
            if (iSize == 0) return 0;
            Xor(rgD, iOff + 1, iSize, uKey, 0, 0);
            return iSize;
        }
        if ((b0 & 3) == 3)//fat头12字节
        {
            uint uCodeSize = ReadU32(rgD, iOff + 4);
            if (uCodeSize == 0 || uCodeSize > 0x00FFFFFF) return 0;
            Xor(rgD, iOff + 12, (int)uCodeSize, uKey, 0, 0);
            return (int)uCodeSize;
        }
        return 0;
    }

    private static void Xor(byte[] rgD, int iOff, int iLen, ulong uKey, int iV, int iLayer)//ChaCha20密钥流先nibble置换再异或 走CFB 轮数由变体选
    {
        byte[] rgPH, rgPL, rgIH, rgIL;
        GenPerm(uKey, out rgPH, out rgPL, out rgIH, out rgIL);
        byte[] ckey = KdfChaCha(uKey);
        byte[] rgNonce = new byte[12];
        if (iLayer == 1)
        {
            W32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
            W32(rgNonce, 4, (uint)(uKey >> 32));
            W32(rgNonce, 8, 0x2B7E1516u ^ (uint)iV);
        }
        else
        {
            W32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
            W32(rgNonce, 4, 0);
            W32(rgNonce, 8, 0xC2B2AE35u);
        }
        int iRounds = 20;//轮数统一20 变体只影响nonce
        byte[] ks = new byte[64];
        uint uCounter = 0;
        int ki = 0;
        ChaCha.Block(ckey, rgNonce, uCounter, ks, iRounds);
        ulong uPrev = 0;
        for (int i = 0; i < iLen; i++)
        {
            if (ki >= 64)
            {
                uCounter++;
                ChaCha.Block(ckey, rgNonce, uCounter, ks, iRounds);
                ki = 0;
            }
            byte byK = (byte)(ks[ki] ^ uPrev);
            byte byM = rgD[iOff + i];
            byte byC = (byte)((rgPH[(byM ^ byK) >> 4] << 4) | rgPL[(byM ^ byK) & 0xF]);
            rgD[iOff + i] = byC;
            uPrev = byC;
            ki++;
        }
    }

    internal static byte[] KdfChaCha(ulong uKey) => ChaCha.Kdf(uKey);//核心在ChaCha 这里只是对外的薄包装

    private static void W32(byte[] rg, int iOff, uint uV)
    {
        rg[iOff] = (byte)uV; rg[iOff + 1] = (byte)(uV >> 8);
        rg[iOff + 2] = (byte)(uV >> 16); rg[iOff + 3] = (byte)(uV >> 24);
    }

    private static void GenPerm(ulong uKey, out byte[] rgPH, out byte[] rgPL, out byte[] rgIH, out byte[] rgIL)//nibble置换表由密钥派生 高与低独立 含逆表
    {
        byte[] rgP = new byte[16];
        ulong uS = uKey ^ 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 16; i++) rgP[i] = (byte)i;
        for (int i = 15; i >= 1; i--)
        {
            uS = uS * 0x0100019301000193UL + 0x9E3779B97F4A7C15UL;
            int j = (int)((uS >> 56) % (uint)(i + 1));
            (rgP[i], rgP[j]) = (rgP[j], rgP[i]);
        }
        rgPH = (byte[])rgP.Clone();
        uS = uKey ^ 0x85EBCA6B85EBCA6BUL;
        for (int i = 0; i < 16; i++) rgP[i] = (byte)i;
        for (int i = 15; i >= 1; i--)
        {
            uS = uS * 0x9E3779B19E3779B1UL + 0x9747B28C9747B28CUL;
            int j = (int)((uS >> 56) % (uint)(i + 1));
            (rgP[i], rgP[j]) = (rgP[j], rgP[i]);
        }
        rgPL = (byte[])rgP.Clone();
        rgIH = new byte[16]; rgIL = new byte[16];
        for (int i = 0; i < 16; i++) { rgIH[rgPH[i]] = (byte)i; rgIL[rgPL[i]] = (byte)i; }
    }

    private static void Step(byte[] rgD, int iPos, ref ulong uS1, ref ulong uS2, ref ulong uPrev,
        ulong uC1, ulong uC2, ulong uC3, ulong uC4, int iV, byte[] rgPH, byte[] rgPL)
    {
        byte byOut;
        if (iV == 1)
        {
            uS1 = uS1 * uC4 + uC2;
            uS2 = (uS2 * uC1 + uC3) ^ (uS1 >> 8) ^ (uS1 << 16) ^ uPrev;
            uS2 = (uS2 << 11) | (uS2 >> 53);
            byOut = (byte)((uS2 >> 24) ^ (uS1 >> 16) ^ (uS1 >> 24));
        }
        else if (iV == 2)
        {
            uS1 = (uS1 * uC1 + uC3) ^ (uS2 >> 7);
            uS2 = (uS2 * uC4 + uC2) ^ (uS1 << 16) ^ uPrev;
            uS2 = (uS2 << 17) | (uS2 >> 47);
            byOut = (byte)((uS1 ^ uS2 ^ (uS1 >> 16) ^ (uS2 >> 8)) & 0xFF);
        }
        else
        {
            uS1 = uS1 * uC1 + uC3;
            uS2 = (uS2 * uC4 + uC2) ^ (uS1 >> 8) ^ (uS1 << 16) ^ uPrev;
            uS2 = (uS2 << 13) | (uS2 >> 51);
            byOut = (byte)((uS1 >> 24) ^ (uS2 >> 16) ^ (uS2 >> 24));
        }
        rgD[iPos] ^= byOut;//先XOR流
        rgD[iPos] = (byte)((rgPH[rgD[iPos] >> 4] << 4) | rgPL[rgD[iPos] & 0xF]);//后4位置换
        uPrev = rgD[iPos];
    }

    private static ulong Rol64(ulong uV, int iN) => (uV << iN) | (uV >> (64 - iN));

    internal static ulong MaskOf(ulong uKey) => uKey ^ (uKey >> 16) ^ (uKey << 13) ^ 0x9E3779B97F4A7C15UL;

    internal static ulong DeriveKey2(ulong uKey, uint uId)//与方法的全局序号绑定 常量要小于2^31 便于asm用imm32
    {
        ulong uH = uKey ^ (ulong)uId * 0x27220A95UL;
        uH ^= uH >> 33;
        uH *= 0x165667B1UL;
        uH ^= uH >> 29;
        uH *= 0x27220A95UL;
        uH ^= uH >> 32;
        return uH;
    }

    internal static uint Crc32(byte[] rgD, int iOff, int iLen)
    {
        uint uCrc = 0xFFFFFFFFu;
        for (int i = 0; i < iLen; i++)
        {
            uCrc ^= rgD[iOff + i];
            for (int j = 0; j < 8; j++)
                uCrc = (uCrc >> 1) ^ (0xEDB88320u & (uint)-(int)(uCrc & 1));
        }
        return ~uCrc;
    }

    internal static ushort ReadU16(byte[] rgD, int iOff) => BitConverter.ToUInt16(rgD, iOff);
    internal static uint ReadU32(byte[] rgD, int iOff) => BitConverter.ToUInt32(rgD, iOff);
}
