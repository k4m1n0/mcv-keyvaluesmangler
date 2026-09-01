using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

public static class MethodEncryptor
{
    public static List<(ulong Hi, ulong Lo)> EncryptAll(byte[] rgD, ulong uKey, IReadOnlyCollection<uint>? rgOnly = null)
    {
        if (rgD.Length < 0x40) throw new InvalidDataException("too small");
        uint uLfaNew = ReadU32(rgD, 0x3C);
        int iPe = (int)uLfaNew;
        if (iPe <= 0 || iPe + 0x60 >= rgD.Length || ReadU32(rgD, iPe) != 0x4550)
            throw new InvalidDataException("no PE signature");
        ushort usNumSec = ReadU16(rgD, iPe + 6);
        ushort usOptSize = ReadU16(rgD, iPe + 20);
        ushort usMagic = ReadU16(rgD, iPe + 24);//0x10B / 0x20B
        int iDdStart = iPe + 24 + (usMagic == 0x20B ? 112 : 96);

        uint uCliRva = ReadU32(rgD, iDdStart + 14 * 8);//DataDirectory[14]=CLI头
        if (uCliRva == 0) throw new InvalidDataException("no CLI header");
        int iCliOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uCliRva);

        //用MetadataReader枚举MethodDef RVA
        var rgCrcs = new List<(ulong, ulong)>();
        ulong uMask = MaskOf(uKey);
        using (var ms = new MemoryStream(rgD, writable: false))
        using (var pereader = new PEReader(ms))
        {
            var mr = pereader.GetMetadataReader();
            foreach (var mdh in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(mdh);
                if (rgOnly != null)
                {
                    uint uTok = 0x06000000u | (uint)MetadataTokens.GetRowNumber(mr, mdh);
                    if (!rgOnly.Contains(uTok)) continue;
                }
                uint uRva = (uint)md.RelativeVirtualAddress;
                if (uRva == 0) continue;//PInvoke/abstract/interface 无方法体
                int iOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uRva);
                int iIlStart = ((rgD[iOff] & 3) == 2) ? iOff + 1 : iOff + 12;//tiny头1字节 fat头12字节
                int iEb = EncryptBody(rgD, iOff, uKey);
                if (iEb > 0)
                {
                    //第二层key 运行时从签名表Hi还原
                    ulong uKey2 = (ulong)Random.Shared.NextInt64();
                    //第二层按方法变体号加密
                    int iVx = (int)(uKey2 ^ uKey) & 3;
                    Xor(rgD, iIlStart, iEb, uKey2, iVx == 3 ? 0 : iVx);
                    uint uCrc2 = Crc32(rgD, iIlStart, iEb);//最终密文指纹
                    //表项128位 Hi=uKey2^mask Lo低32=crc2^mask
                    rgCrcs.Add((uKey2 ^ uMask, (uint)(uCrc2 ^ (uint)uMask)));
                }
            }
        }
        return rgCrcs;
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
        throw new InvalidDataException("RVA 0x" + uRva.ToString("X8") + " unmapped");
    }

    private static int EncryptBody(byte[] rgD, int iOff, ulong uKey)
    {
        byte b0 = rgD[iOff];
        if ((b0 & 3) == 2)//tiny头1字节 codeSize=b0>>2
        {
            int iSize = b0 >> 2;
            if (iSize == 0) return 0;
            Xor(rgD, iOff + 1, iSize, uKey, 0);
            return iSize;
        }
        if ((b0 & 3) == 3)//fat头12字节
        {
            uint uCodeSize = ReadU32(rgD, iOff + 4);
            if (uCodeSize == 0 || uCodeSize > 0x00FFFFFF) return 0;
            Xor(rgD, iOff + 12, (int)uCodeSize, uKey, 0);
            return (int)uCodeSize;
        }
        return 0;//不支持的格式 跳过
    }

    //双向流密码 前半正向后半逆向 输出再按nibble置换 高/低各用独立流
    private static void Xor(byte[] rgD, int iOff, int iLen, ulong uKey, int iV)
    {
        //流常数由key派生 每次打包不同
        ulong uC1 = (uKey ^ 0x9E3779B97F4A7C15UL) | 1UL;
        ulong uC2 = ((uKey * 0x0100019301000193UL) ^ 0x85EBCA6B85EBCA6BUL) | 1UL;
        ulong uC3 = (Rol64(uKey, 9) ^ 0x9747B28C9747B28CUL) | 1UL;
        ulong uC4 = ((uKey * 0x85EBCA6B85EBCA6BUL) ^ 0xC2B2AE35C2B2AE35UL) | 1UL;
        byte[] rgPH, rgPL, rgIH, rgIL;
GenPerm(uKey, out rgPH, out rgPL, out rgIH, out rgIL);
        int iHalf = iLen / 2;
        ulong uS1 = uKey ^ (uint)iLen * uC1;
        ulong uS2 = uKey ^ uC2 ^ (uint)iLen;
        ulong uPrev = 0;//CFB 前一字节密文混入流
        for (int i = 0; i < iHalf; i++)
            Step(rgD, iOff + i, ref uS1, ref uS2, ref uPrev, uC1, uC2, uC3, uC4, iV, rgPH, rgPL);
        uS1 = uKey ^ (uint)iLen * uC1;//后半重新起流
        uS2 = uKey ^ uC2 ^ (uint)iLen;
        uPrev = 0;
        for (int i = iLen - 1; i >= iHalf; i--)
            Step(rgD, iOff + i, ref uS1, ref uS2, ref uPrev, uC1, uC2, uC3, uC4, iV, rgPH, rgPL);
    }

    //nibble置换表由key派生 高/低独立 含逆表
    private static void GenPerm(ulong uKey, out byte[] rgPH, out byte[] rgPL, out byte[] rgIH, out byte[] rgIL)
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
        rgPH = (byte[])rgP.Clone();//高nibble用主表
        //低nibble换种子重排
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

    private static ulong MaskOf(ulong uKey) => uKey ^ (uKey >> 16) ^ (uKey << 13) ^ 0x9E3779B97F4A7C15UL;

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