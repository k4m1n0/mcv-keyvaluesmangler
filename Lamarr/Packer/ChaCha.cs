namespace Lamarr.Packer;

//打包侧唯一的ChaCha核心 轮数是参数 20用于方法体层 12用于内核层 常数与nonce拼法不得改动
internal static class ChaCha
{
    internal static void Block(byte[] rgKey, byte[] rgNonce, uint uCounter, byte[] rgOut, int iRounds)//key 32B nonce 12B out 64B 轮数只取 8 12 20
    {
        uint[] st = new uint[16];
        st[0] = 0x61707865; st[1] = 0x3320646e; st[2] = 0x79622d32; st[3] = 0x6b206574;
        for (int i = 0; i < 8; i++) st[4 + i] = Rd32(rgKey, i * 4);
        st[12] = uCounter;
        st[13] = Rd32(rgNonce, 0); st[14] = Rd32(rgNonce, 4); st[15] = Rd32(rgNonce, 8);
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
        for (int i = 0; i < 16; i++) Wr32(rgOut, i * 4, ws[i]);
    }

    private static void Qr(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a += b; d ^= a; d = (d << 16) | (d >> 16);
        c += d; b ^= c; b = (b << 12) | (b >> 20);
        a += b; d ^= a; d = (d << 8) | (d >> 24);
        c += d; b ^= c; b = (b << 7) | (b >> 25);
    }

    internal static byte[] Kdf(ulong uKey)//按第二层密钥派生 key全0 counter取0 nonce低8字节放uKey后4字节放域常数 取首块前32字节
    {
        byte[] key0 = new byte[32];
        byte[] rgNonce = new byte[12];
        Wr32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
        Wr32(rgNonce, 4, (uint)(uKey >> 32));
        Wr32(rgNonce, 8, 0x017A9C53u);
        byte[] blk = new byte[64];
        Block(key0, rgNonce, 0, blk, 20);
        byte[] rgOut = new byte[32];
        Array.Copy(blk, 0, rgOut, 0, 32);
        return rgOut;
    }

    private static uint Rd32(byte[] rg, int iOff)//按小端读 放在本类以免私有工具跨类暴露
        => (uint)(rg[iOff] | (rg[iOff + 1] << 8) | (rg[iOff + 2] << 16) | (rg[iOff + 3] << 24));

    private static void Wr32(byte[] rg, int iOff, uint uV)
    {
        rg[iOff] = (byte)uV; rg[iOff + 1] = (byte)(uV >> 8);
        rg[iOff + 2] = (byte)(uV >> 16); rg[iOff + 3] = (byte)(uV >> 24);
    }
}
