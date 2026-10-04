namespace Lamarr.Packer;

using System.Text;
using System.Runtime.InteropServices;
using A0;

//打包期的引擎执行器 打包时必须真的把roll的每枚单元按序执行一遍来推进链
//单元i的at-rest键等于roll钥匙异或前序执行输出 所以要把引擎镜像映射进本进程并按其运行期布局调用
internal partial class PeWriterAntheil
{
    private delegate ulong vRunUnit(IntPtr pBc, IntPtr pRegs, IntPtr pMem, ulong cbM);

    [DllImport("kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
    private static extern IntPtr vAlloc(IntPtr p, UIntPtr cb, uint fl, uint pr);
    [DllImport("kernel32.dll", EntryPoint = "VirtualProtect", SetLastError = true)]
    private static extern bool vProt(IntPtr p, UIntPtr cb, uint np, out uint old);
    [DllImport("kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
    private static extern bool vFree(IntPtr p, UIntPtr cb, uint fl);

    private static int RvaOffX(byte[] rg, int iPe, ushort usOpt, uint uRva)
    {
        int iCnt = BitConverter.ToUInt16(rg, iPe + 6);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < iCnt; i++)
        {
            int o = iSec + i * 40;
            uint uVsz = BitConverter.ToUInt32(rg, o + 8);
            uint uVa = BitConverter.ToUInt32(rg, o + 12);
            uint uRsz = BitConverter.ToUInt32(rg, o + 16);
            uint uRaw = BitConverter.ToUInt32(rg, o + 20);
            uint uEnd = Math.Max(uVsz, uRsz);
            if (uRva >= uVa && uRva < uVa + uEnd) return (int)(uRaw + (uRva - uVa));
        }
        return -1;
    }

    private static IntPtr MapEngineX(byte[] rg, out int iTextVa, out int iTextSz)//手工映射 .text拷到分配基址 数据节按其RVA偏移
    {
        iTextVa = 0; iTextSz = 0;
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe <= 0 || iPe + 24 > rg.Length) return IntPtr.Zero;
        int iCnt = BitConverter.ToUInt16(rg, iPe + 6);
        int iSec = iPe + 24 + BitConverter.ToUInt16(rg, iPe + 20);
        int iTextRaw = 0, iDataRaw = 0, iDataSz = 0, iDataVa = 0, iData2Raw = 0, iData2Sz = 0, iData2Va = 0;
        for (int i = 0; i < iCnt; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rg.Length) return IntPtr.Zero;
            string sName = Encoding.ASCII.GetString(rg, o, 8).TrimEnd('\0');
            uint uVa = BitConverter.ToUInt32(rg, o + 12);
            int iRsz = (int)BitConverter.ToUInt32(rg, o + 16);
            int iRaw = (int)BitConverter.ToUInt32(rg, o + 20);
            if (sName == ".text") { iTextVa = (int)uVa; iTextSz = iRsz; iTextRaw = iRaw; }
            else if (sName == ".rdata") { iDataVa = (int)uVa; iDataSz = iRsz; iDataRaw = iRaw; }
            else if (sName == ".data") { iData2Va = (int)uVa; iData2Sz = iRsz; iData2Raw = iRaw; }
        }
        if (iTextSz <= 0) return IntPtr.Zero;
        if (iDataSz > 0 && iDataVa < iTextVa) return IntPtr.Zero;//数据节在.text之前时相对偏移为负 会写到分配之外
        if (iData2Sz > 0 && iData2Va < iTextVa) return IntPtr.Zero;
        int cbMap = iTextSz + 0x1000 + (iDataSz > 0 ? iDataVa - iTextVa + iDataSz : 0) + (iData2Sz > 0 ? iData2Va - iTextVa + iData2Sz : 0) + 0x1000;
        IntPtr p = vAlloc(IntPtr.Zero, (UIntPtr)cbMap, 0x3000, 0x40);
        if (p == IntPtr.Zero) return IntPtr.Zero;
        Marshal.Copy(rg, iTextRaw, p, Math.Min(iTextSz, Math.Max(0, rg.Length - iTextRaw)));//只拷文件里真实存在的字节 其余由零页补足
        if (iDataSz > 0) Marshal.Copy(rg, iDataRaw, new IntPtr(p.ToInt64() + (iDataVa - iTextVa)), Math.Min(iDataSz, Math.Max(0, rg.Length - iDataRaw)));
        if (iData2Sz > 0) Marshal.Copy(rg, iData2Raw, new IntPtr(p.ToInt64() + (iData2Va - iTextVa)), Math.Min(iData2Sz, Math.Max(0, rg.Length - iData2Raw)));
        vProt(p, (UIntPtr)iTextSz, 0x20, out _);
        int iDataAt = (iTextSz + 0xFFF) & ~0xFFF;
        if (iDataAt > iTextSz && iDataAt < cbMap) vProt(new IntPtr(p.ToInt64() + iTextSz), (UIntPtr)(iDataAt - iTextSz), 0x04, out _);//首段页尾余量不设RWX
        if (iDataAt < cbMap) vProt(new IntPtr(p.ToInt64() + iDataAt), (UIntPtr)(cbMap - iDataAt), 0x04, out _);
        return p;
    }

    private static uint EngineEntryRvaX(byte[] rg)
    {
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe <= 0 || iPe + 24 > rg.Length) return 0;
        int iDataDir = iPe + 24 + (BitConverter.ToUInt16(rg, iPe + 24) == 0x20B ? 112 : 96);
        uint uExpRva = BitConverter.ToUInt32(rg, iDataDir);
        if (uExpRva == 0) return 0;
        ushort usOpt = BitConverter.ToUInt16(rg, iPe + 20);
        int iExp = RvaOffX(rg, iPe, usOpt, uExpRva);
        if (iExp <= 0) return 0;
        int iNameCnt = BitConverter.ToInt32(rg, iExp + 0x18);
        int iFuncCnt = BitConverter.ToInt32(rg, iExp + 0x1C);
        int iFuncOff = RvaOffX(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x1C));
        if (iFuncOff <= 0 || iFuncCnt <= 0) return 0;
        if (iNameCnt > 0)
        {
            int iNameOff = RvaOffX(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x20));
            int iOrdOff = RvaOffX(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x24));
            if (iNameOff > 0 && iOrdOff > 0) return BitConverter.ToUInt32(rg, iFuncOff + BitConverter.ToUInt16(rg, iOrdOff) * 4);
        }
        return BitConverter.ToUInt32(rg, iFuncOff);
    }

    internal static bool ChainRunUnit(byte[] rgEngine, byte[] rgBlob, uint uKey, uint uChainIn, out uint uChainOut, out string sErr)//打包端单元执行器 解不出或执行失败即false让打包失败关闭
    {
        uChainOut = 0;
        sErr = "";
        int iTextVa = 0, iTextSz = 0;
        IntPtr pBase = MapEngineX(rgEngine, out iTextVa, out iTextSz);
        if (pBase == IntPtr.Zero) { sErr = "map engine failed"; return false; }
        IntPtr pRegs = IntPtr.Zero, pMem = IntPtr.Zero, pBlob = IntPtr.Zero;
        try
        {
            uint uRva = EngineEntryRvaX(rgEngine);
            if (uRva == 0 || uRva < (uint)iTextVa || uRva >= (uint)(iTextVa + iTextSz)) { sErr = "engine export rva outside .text"; return false; }
            var fn = Marshal.GetDelegateForFunctionPointer<vRunUnit>(new IntPtr(pBase.ToInt64() + (int)uRva - iTextVa));
            byte[] rgMat = Q.ChainMat();
            pRegs = Q.CtxAlloc();
            pMem = Marshal.AllocHGlobal(0x2000);
            pBlob = Marshal.AllocHGlobal(rgBlob.Length);
            for (int i = 0; i < 192; i++) Marshal.WriteByte(pRegs, i, 0);
            for (int i = 0; i < 0x2000; i++) Marshal.WriteByte(pMem, i, 0);
            Marshal.Copy(rgBlob, 0, pBlob, rgBlob.Length);
            Marshal.WriteInt64(pRegs, 2 * 8, 0L);
            Marshal.WriteInt64(pRegs, 3 * 8, rgMat.Length);
            Marshal.Copy(rgMat, 0, pMem, rgMat.Length);
            uint uFn = 0x811C9DC5u;
            for (int i = 0; i < iTextSz; i++) { uFn ^= Marshal.ReadByte(pBase, i); uFn *= 0x01000193u; }
            //引擎把ctx[128]与自身.text的FNV异或一次 所以传uKey^uFn后最终at-rest键正好是uKey
            Marshal.WriteInt32(pRegs, 128, unchecked((int)(uKey ^ uFn)));
            Marshal.WriteInt64(pRegs, 128 + 8, pBase.ToInt64());
            Marshal.WriteInt32(pRegs, 128 + 16, iTextSz);
            Marshal.WriteInt32(pRegs, 128 + 20, unchecked((int)uChainIn));
            Marshal.WriteInt32(pRegs, 128 + 32, Q.iWindowBytes);//声明材料窗口
            Marshal.WriteInt32(pRegs, 128 + 40, 1);//打包端走链 不进不出
            ulong uRet = fn(pBlob, pRegs, pMem, 0x2000);
            if (uRet == unchecked((ulong)-1)) { sErr = "h_bad (decode or execute failed)"; return false; }
            uChainOut = (uint)Marshal.ReadInt32(pRegs, 128 + 24);
            if (uChainOut == 0) { sErr = "chain out zero (run did not publish a state)"; return false; }
            return true;
        }
        catch (Exception ex) { sErr = ex.GetType().Name + ": " + ex.Message; return false; }
        finally
        {
            if (pBlob != IntPtr.Zero) { for (int i = 0; i < rgBlob.Length; i++) Marshal.WriteByte(pBlob, i, 0); Marshal.FreeHGlobal(pBlob); }
            if (pRegs != IntPtr.Zero) { for (int i = 0; i < 192; i++) Marshal.WriteByte(pRegs, i, 0); Marshal.FreeHGlobal(pRegs); }
            if (pMem != IntPtr.Zero) { for (int i = 0; i < 0x2000; i++) Marshal.WriteByte(pMem, i, 0); Marshal.FreeHGlobal(pMem); }
            vFree(pBase, UIntPtr.Zero, 0x8000);
        }
    }

    internal static NotenrolleComp.UnitRunner RunnerFor(byte[] rgEngine) =>//引擎镜像绑成链推进器 一次打包里复用同一份镜像
        (byte[] rgBlob, uint uKey, uint uChainIn, out uint uChainOut, out string sErr) => ChainRunUnit(rgEngine, rgBlob, uKey, uChainIn, out uChainOut, out sErr);

    internal static uint EngineFnMapped(byte[] rgEngine)//引擎.text的FNV 按映射后的字节算 运行期调用方折进ctx的就是这一份
    {
        int iTextVa = 0, iTextSz = 0;
        IntPtr pBase = MapEngineX(rgEngine, out iTextVa, out iTextSz);
        if (pBase == IntPtr.Zero) return 0;
        try
        {
            uint uFn = 0x811C9DC5u;
            for (int i = 0; i < iTextSz; i++) { uFn ^= Marshal.ReadByte(pBase, i); uFn *= 0x01000193u; }
            return uFn;
        }
        finally { vFree(pBase, UIntPtr.Zero, 0x8000); }
    }
}
