namespace A0;

//派生只用escrow与产物里已经记录的材料 这样重放机没有K也能算出同一份key与nonce
internal static class QStream
{
    internal const string sDomain = "c12:v1";//换算法必须抬域版本 否则新旧版本的流会混用

    public const int Key = 0x00;    //key32 状态槽8 索引槽8 开关槽8 空闲8拼出
    public const int Slot = 0x20;   //nonce12与ctr4 调完把新的ctr写回槽首4B
    public const int Data = 0x30;   //数据区 必须等于A0.Q.VmMatData
    public const int dm = 1984;     //单次块上限 必须等于驱动分块且为64的整数倍 名字固定2字符以免打包卫生检查拦下
    public const int Tbl = 0x9D0;   //四分之一轮状态下标表 128B放在材料之外 由调用方逐调用布好

    public static byte[] TblBytes()//表布局 8模式4项 每项w32 前4行是列轮后4行是对角轮 第q个QR取第q&7行
    {
        byte[] rgB = new byte[128];
        for (int p = 0; p < 8; p++)
            for (int r = 0; r < 4; r++)
            {
                int i = p < 4 ? p + 4 * r : (r == 0 ? (p - 4) : 4 * r + (((p - 4) + r) & 3));
                BitConverter.GetBytes(i).CopyTo(rgB, (p * 4 + r) * 4);
            }
        return rgB;
    }

    internal static void KeyNonceOf(byte[] rgHead, long lEntrySize, ulong uRollKey, int iKind, int cb,
        ulong uVmMix, ulong uSelfMix, out byte[] rgKey32, out byte[] rgNonce12)
    {
        byte[] rg = new byte[sDomain.Length + 16 + rgHead.Length + 32];
        int iAt = 0;
        for (int i = 0; i < sDomain.Length; i++) rg[iAt++] = (byte)sDomain[i];
        Put64(rg, ref iAt, uVmMix);//只有入口层传值 掩码路径传0
        Put64(rg, ref iAt, uSelfMix);
        Array.Copy(rgHead, 0, rg, iAt, rgHead.Length); iAt += rgHead.Length;
        Put64(rg, ref iAt, (ulong)lEntrySize);
        Put64(rg, ref iAt, (ulong)(uint)iKind);
        Put64(rg, ref iAt, (ulong)(uint)cb);
        Put64(rg, ref iAt, (ulong)uRollKey);//kind与cb与rollKey全进派生输入 保证每段的key与nonce唯一
        byte[] rgH = Lamarr.Packer.Sha512.Hash(rg);
        rgKey32 = new byte[32];
        rgNonce12 = new byte[12];
        Array.Copy(rgH, 0, rgKey32, 0, 32);
        Array.Copy(rgH, 32, rgNonce12, 0, 12);
        Array.Clear(rgH, 0, rgH.Length);
        Array.Clear(rg, 0, rg.Length);
    }

    private static void Put64(byte[] rg, ref int iAt, ulong uV)
    {
        for (int i = 0; i < 8; i++) rg[iAt++] = (byte)(uV >> (8 * i));
    }
}
