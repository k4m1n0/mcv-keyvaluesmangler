namespace Lamarr.Packer;

internal static class ChMask
{
    internal static byte[] Mask(byte[] rgHead, long lEntrySize, ulong uRollKey, int iKind, int cb, ulong uVmMix, ulong uSelfMix)//kind 1=roll掩码 2=引擎掩码 3=boot镜像流 0=预留引导清钥
    {
        A0.QStream.KeyNonceOf(rgHead, lEntrySize, uRollKey, iKind, cb, uVmMix, uSelfMix, out byte[] rgKey, out byte[] rgNonce);
        byte[] rg = new byte[cb];
        byte[] rgBlk = new byte[64];
        for (int iAt = 0; iAt < cb; iAt += 64)
        {
            ChaCha.Block(rgKey, rgNonce, (uint)(iAt / 64), rgBlk, 12);//计数器按块序号推进 使分段加密与一次性加密得到同一结果
            int n = Math.Min(64, cb - iAt);
            for (int k = 0; k < n; k++) rg[iAt + k] = rgBlk[k];
        }
        Array.Clear(rgKey, 0, rgKey.Length);
        Array.Clear(rgNonce, 0, rgNonce.Length);
        Array.Clear(rgBlk, 0, rgBlk.Length);
        return rg;
    }

    internal static void Xor(byte[] rgD, int iOff, byte[] rgMask)
    {
        if (iOff < 0 || rgMask.Length > rgD.Length - iOff) throw new ArgumentOutOfRangeException(nameof(iOff), "mask does not fit the destination buffer");
        for (int i = 0; i < rgMask.Length; i++) rgD[iOff + i] ^= rgMask[i];
    }
}