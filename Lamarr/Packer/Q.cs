namespace A0;

internal static class Q
{
    private const ulong uDomainA = 0xA3F1C7D95E2B6849uL;//固定域 密钥不只依赖产物尺寸
    private const ulong uDomainB = 0x5D2E8B4173C9AF06uL;
    public const ulong uLenMask = 0x1F2E3D4C5B6A7988uL;//条目尾部长度字段的掩码与密文校验 各占8字节
    public const ulong uChkMask = 0x88796A5B4C3D2E1FuL;//条目尾部密文校验用的掩码
    public const int iTailLen = 16;//条目尾部固定16字节 [密文长度 ^ 掩码][密文校验 ^ 掩码]
    public const int iHeadSample = 4096;//密钥材料取镜像头部的字节数

    public static ulong Hash64(byte[] rg, int iOff, int iLen)//FNV-1a 64头部样本取摘要密文取校验都用它
    {
        ulong uHash = 0xCBF29CE484222325uL;
        for (int i = 0; i < iLen; i++)
        {
            uHash ^= rg[iOff + i];
            uHash *= 0x100000001B3uL;
        }
        return uHash;
    }

    public static ulong TableDigest(long[] rgSz)//条目数与各条目声明长度都进密钥材料
    {
        ulong uHash = 0xCBF29CE484222325uL ^ (ulong)rgSz.Length;
        for (int i = 0; i < rgSz.Length; i++)
        {
            uHash = (uHash ^ (ulong)rgSz[i]) * 0x100000001B3uL;
            uHash ^= (ulong)(uint)i * 0x9E3779B97F4A7C15uL;
        }
        return uHash ^ uDomainB;
    }

    public static byte[] KeyStream(byte[] rgHead, long lEntrySize, int iCipherLen, ulong uTableDigest)//密钥流按splitmix64派生
    {
        ulong uState = Hash64(rgHead, 0, rgHead.Length) ^ uDomainA ^ uTableDigest;
        uState ^= (ulong)lEntrySize * 0x9E3779B97F4A7C15uL;
        uState ^= (ulong)(uint)iCipherLen << 17;
        byte[] rgKey = new byte[iCipherLen];
        for (int i = 0; i < rgKey.Length; i += 8)
        {
            uState += 0x9E3779B97F4A7C15uL;
            ulong uZ = uState;
            uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9uL;
            uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBuL;
            uZ ^= uZ >> 31;
            uZ ^= (ulong)(uint)i * 0xD6E8FEB86659FD93uL;
            int n = Math.Min(8, rgKey.Length - i);
            for (int k = 0; k < n; k++) rgKey[i + k] = (byte)(uZ >> (k * 8));
        }
        return rgKey;
    }

    public static void Xor(byte[] rgData, int iOff, byte[] rgKey)
    {
        for (int i = 0; i < rgKey.Length; i++) rgData[iOff + i] ^= rgKey[i];
    }
}
