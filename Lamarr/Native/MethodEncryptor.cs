using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

//方法体IL保长流密码加密 密文CRC32供jithook判定 s=s*0x9E3779B1+0x9747B28C 取s>>24
public static class MethodEncryptor
{
    public static List<uint> EncryptAll(byte[] rgD, uint uKey, IReadOnlyCollection<uint>? rgOnly = null)
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

        //用官方MetadataReader枚举MethodDef RVA 免手写解析
        var rgCrcs = new List<uint>();
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
                    rgCrcs.Add(Crc32(rgD, iIlStart, iEb) ^ 0x9E3779B9u);//密文CRC^常量 防签名表直接dump匹配
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

    private static int EncryptBody(byte[] rgD, int iOff, uint uKey)
    {
        byte b0 = rgD[iOff];
        if ((b0 & 3) == 2)//tiny头1字节 codeSize=b0>>2
        {
            int iSize = b0 >> 2;
            if (iSize == 0) return 0;
            Xor(rgD, iOff + 1, iSize, uKey);
            return iSize;
        }
        if ((b0 & 3) == 3)//fat头12字节
        {
            uint uCodeSize = ReadU32(rgD, iOff + 4);
            if (uCodeSize == 0 || uCodeSize > 0x00FFFFFF) return 0;
            Xor(rgD, iOff + 12, (int)uCodeSize, uKey);
            return (int)uCodeSize;
        }
        return 0;//保留格式
    }

    //保长流密码 每方法从key起流
    private static void Xor(byte[] rgD, int iOff, int iLen, uint uKey)
    {
        uint uS = uKey;
        for (int i = 0; i < iLen; i++)
        {
            uS = uS * 2654435761u + 0x9747B28Cu;
            rgD[iOff + i] ^= (byte)(uS >> 24);
        }
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