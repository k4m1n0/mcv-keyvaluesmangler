using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.Packer;

public static class MethodEncryptor
{
    //这批方法的全局签名表起始序号 掩码域与解密密钥无函数关系
private static readonly int iVariantShift =
        int.TryParse(Environment.GetEnvironmentVariable("LAMARR_VARIANT_SHIFT") ?? "", out int iVs) ? (iVs & 3) : 0;

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
                //第二层密钥由钩子按全局序号现算 表里不存密钥 全局序号=本批序号+iBase 先过全空间置换再派生 缺省恒等
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

    private static void Xor(byte[] rgD, int iOff, int iLen, ulong uKey, int iV, int iLayer)//ChaCha20 keystream先nibble置换再XOR带CFB轮数由变体选
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
        ChaChaBlock(ckey, rgNonce, uCounter, ks, iRounds);
        ulong uPrev = 0;
        for (int i = 0; i < iLen; i++)
        {
            if (ki >= 64)
            {
                uCounter++;
                ChaChaBlock(ckey, rgNonce, uCounter, ks, iRounds);
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

    private static void ChaChaBlock(byte[] rgKey, byte[] rgNonce, uint uCounter, byte[] rgOut, int iRounds)//RFC8439 ChaCha20块生成rounds=8/12/20
    {
        uint[] st = new uint[16];
        st[0] = 0x61707865; st[1] = 0x3320646e; st[2] = 0x79622d32; st[3] = 0x6b206574;
        for (int i = 0; i < 8; i++) st[4 + i] = ReadU32(rgKey, i * 4);
        st[12] = uCounter;
        st[13] = ReadU32(rgNonce, 0); st[14] = ReadU32(rgNonce, 4); st[15] = ReadU32(rgNonce, 8);
        uint[] ws = (uint[])st.Clone();
        int iDr = iRounds / 2;
        for (int r = 0; r < iDr; r++)
        {
            Qr(ref ws[0], ref ws[4], ref ws[8], ref ws[12]);
            Qr(ref ws[1], ref ws[5], ref ws[9], ref ws[13]);
            Qr(ref ws[2], ref ws[6], ref ws[10], ref ws[14]);
            Qr(ref ws[3], ref ws[7], ref ws[11], ref ws[15]);
            Qr(ref ws[0], ref ws[5], ref ws[10], ref ws[15]);
            Qr(ref ws[1], ref ws[6], ref ws[11], ref ws[12]);
            Qr(ref ws[2], ref ws[7], ref ws[8], ref ws[13]);
            Qr(ref ws[3], ref ws[4], ref ws[9], ref ws[14]);
        }
        for (int i = 0; i < 16; i++) ws[i] += st[i];
        for (int i = 0; i < 16; i++) W32(rgOut, i * 4, ws[i]);
    }

    private static void Qr(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a += b; d ^= a; d = (d << 16) | (d >> 16);
        c += d; b ^= c; b = (b << 12) | (b >> 20);
        a += b; d ^= a; d = (d << 8) | (d >> 24);
        c += d; b ^= c; b = (b << 7) | (b >> 25);
    }

    internal static byte[] KdfChaCha(ulong uKey)//KDF取ChaCha块的key=0 counter=0 nonce=[key,域常数] 前32字节
    {
        byte[] key0 = new byte[32];
        byte[] rgNonce = new byte[12];
        W32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
        W32(rgNonce, 4, (uint)(uKey >> 32));
        W32(rgNonce, 8, 0x017A9C53u);
        byte[] blk = new byte[64];
        ChaChaBlock(key0, rgNonce, 0, blk, 20);
        byte[] rgOut = new byte[32];
        Array.Copy(blk, 0, rgOut, 0, 32);
        return rgOut;
    }

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

    private static ulong MaskOf(ulong uKey) => uKey ^ (uKey >> 16) ^ (uKey << 13) ^ 0x9E3779B97F4A7C15UL;

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
