namespace Lamarr.Packer;

internal static class PeScrub
{
    public static void Scrub(byte[] rgPe)
    {
        if (!TryPe(rgPe, out int iPe, out int iOpt, out bool bPe32Plus)) return;
        BitConverter.GetBytes(0u).CopyTo(rgPe, iPe + 8);
        if (iOpt + 68 <= rgPe.Length) BitConverter.GetBytes(0u).CopyTo(rgPe, iOpt + 64);//CheckSum
        int iDd = iOpt + (bPe32Plus ? 112 : 96);
        if (iDd + 6 * 8 + 8 <= rgPe.Length) Array.Clear(rgPe, iDd + 6 * 8, 8);
        int iRich = Find(rgPe, 0x68636952u, 0x40, iPe - 4);             //"Rich"
        if (iRich < 0) return;
        uint uKey = BitConverter.ToUInt32(rgPe, iRich + 4);
        int iDan = Find(rgPe, 0x536E6144u ^ uKey, 0x40, iRich);         //DanS四个字节按序异或key
        int iFrom = iDan >= 0 ? iDan : 0x70;
        if (iFrom < 0x40) iFrom = 0x40;
        for (int i = iFrom; i < iPe; i++) rgPe[i] = 0;
    }

    public static bool HasRich(byte[] rgPe)
    {
        if (!TryPe(rgPe, out int iPe, out _, out _)) return false;
        return Find(rgPe, 0x68636952u, 0x40, iPe - 4) >= 0;
    }

    public static uint TimeStamp(byte[] rgPe)
    {
        if (!TryPe(rgPe, out int iPe, out _, out _)) return 0;
        return BitConverter.ToUInt32(rgPe, iPe + 8);
    }

    //blob布局ts(4) CheckSum(4) 调试目录RVA与Size(8) 有Rich时才有前缀区
    public static byte[] CaptureRestore(byte[] rgPe)//清痕只动四处 记下原值即可无损还原 材料进escrow密封区 不随产物分发
    {
        if (!TryPe(rgPe, out int iPe, out int iOpt, out bool bPe32Plus)) return Array.Empty<byte>();
        int iDd = iOpt + (bPe32Plus ? 112 : 96) + 6 * 8;
        if (iPe < 0x40 || iPe > rgPe.Length || iDd + 8 > rgPe.Length) return Array.Empty<byte>();
        bool bRich = HasRich(rgPe);
        byte[] rg = new byte[16 + (bRich ? iPe - 0x40 : 0)];
        BitConverter.GetBytes(BitConverter.ToUInt32(rgPe, iPe + 8)).CopyTo(rg, 0);
        if (iOpt + 68 <= rgPe.Length) BitConverter.GetBytes(BitConverter.ToUInt32(rgPe, iOpt + 64)).CopyTo(rg, 4);
        Array.Copy(rgPe, iDd, rg, 8, 8);
        if (bRich) Array.Copy(rgPe, 0x40, rg, 16, iPe - 0x40);
        return rg;
    }

    public static bool ApplyRestore(byte[] rgPe, byte[] rgBlob)//写回CaptureRestore的原始字节 调用方负责再清痕一次自校验
    {
        if (rgBlob == null || rgBlob.Length < 16) return false;
        if (!TryPe(rgPe, out int iPe, out int iOpt, out bool bPe32Plus)) return false;
        int iDd = iOpt + (bPe32Plus ? 112 : 96) + 6 * 8;
        if (iPe < 0x40 || iPe > rgPe.Length || iDd + 8 > rgPe.Length) return false;
        if (rgBlob.Length != 16 && rgBlob.Length != 16 + (iPe - 0x40)) return false;
        Array.Copy(rgBlob, 0, rgPe, iPe + 8, 4);
        if (iOpt + 68 <= rgPe.Length) Array.Copy(rgBlob, 4, rgPe, iOpt + 64, 4);
        Array.Copy(rgBlob, 8, rgPe, iDd, 8);
        if (rgBlob.Length > 16) Array.Copy(rgBlob, 16, rgPe, 0x40, iPe - 0x40);
        return true;
    }

    public static bool HasDebugDir(byte[] rgPe)//Debug目录项是否存在DataDirectory[6].Size > 0
    {
        if (!TryPe(rgPe, out _, out int iOpt, out bool bPe32Plus)) return false;
        int iDd = iOpt + (bPe32Plus ? 112 : 96) + 6 * 8;
        if (iDd + 8 > rgPe.Length) return false;
        return BitConverter.ToUInt32(rgPe, iDd + 4) > 0;
    }

    static bool TryPe(byte[] rgPe, out int iPe, out int iOpt, out bool bPe32Plus)
    {
        iPe = 0; iOpt = 0; bPe32Plus = false;
        if (rgPe == null || rgPe.Length < 0x80) return false;
        iPe = BitConverter.ToInt32(rgPe, 0x3C);
        if (iPe <= 0 || iPe + 0x18 > rgPe.Length || BitConverter.ToUInt32(rgPe, iPe) != 0x4550) return false;
        iOpt = iPe + 24;
        bPe32Plus = BitConverter.ToUInt16(rgPe, iOpt) == 0x20B;
        return true;
    }

    static int Find(byte[] rgB, uint uVal, int iFrom, int iTo)
    {
        for (int i = iFrom; i + 4 <= iTo + 4 && i + 4 <= rgB.Length; i++)
            if (BitConverter.ToUInt32(rgB, i) == uVal) return i;
        return -1;
    }
}
