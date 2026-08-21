using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace LamarrBoot;

internal static class Program
{
    private const uint uMB_ICONERROR = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [STAThread]
    private static int Main(string[] rgArgs)
    {
        try
        {
            byte[] rgLamApp = ReadLamAppSection();
            if (rgLamApp.Length < 8)
                return Fail(".lamapp section too small");

            uint cbOrig = BitConverter.ToUInt32(rgLamApp, 0);
            uint cbComp = BitConverter.ToUInt32(rgLamApp, 4);
            if (cbOrig == 0 || cbComp == 0 || cbOrig > 0x10000000 || cbComp >= cbOrig || 16UL + cbComp > (ulong)rgLamApp.Length)
                return Fail("bad .lamapp payload header");

            byte[] rgLz = new byte[cbComp];
            Array.Copy(rgLamApp, 8 + 8, rgLz, 0, (int)cbComp);

            byte[] rgDll = new byte[cbOrig];
            uint pcbOut = cbOrig;
            int iRes = Lamarr.LamarrDecoder.Decode(rgDll, ref pcbOut, rgLz, cbComp);
            if (iRes != 0 || pcbOut != cbOrig)
                return Fail($"Lamarr decode failed (0x{iRes:X})");

            Assembly asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgDll));
            MethodInfo? entry = asm.EntryPoint;
            if (entry == null)
                return Fail("payload has no entry point");

            object[] rgInvoke = entry.GetParameters().Length == 0 ? [] : [rgArgs];
            object? oRet = entry.Invoke(null, rgInvoke);
            return oRet is int iRet ? iRet : 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.ToString());
        }
    }

    private static int Fail(string sMsg)
    {
        try { MessageBoxW(IntPtr.Zero, sMsg, "Lamarr loader", uMB_ICONERROR); } catch { }
        return 1;
    }

    private static byte[] ReadLamAppSection()
    {
        string sSelf = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");
        using var fs = new FileStream(sSelf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] rgHdr = new byte[0x400];
        int n = fs.Read(rgHdr, 0, rgHdr.Length);
        if (n < 0x400)
            throw new InvalidOperationException("short PE header");

        int iPe = BitConverter.ToInt32(rgHdr, 0x3C);
        if (iPe < 0 || iPe + 0x18 > rgHdr.Length || BitConverter.ToUInt32(rgHdr, iPe) != 0x4550)
            throw new InvalidOperationException("host is not a PE image");

        ushort usCnt = BitConverter.ToUInt16(rgHdr, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgHdr, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        if (iSec < 0 || iSec + usCnt * 40 > rgHdr.Length)
            throw new InvalidOperationException("short section table");

        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            string sName = Encoding.ASCII.GetString(rgHdr, o, 8).TrimEnd('\0');
            if (sName == ".lamapp")
            {
                uint uRaw = BitConverter.ToUInt32(rgHdr, o + 20);
                uint uRawSz = BitConverter.ToUInt32(rgHdr, o + 16);
                if (uRawSz == 0 || uRaw + uRawSz > (ulong)fs.Length)
                    throw new InvalidOperationException("bad .lamapp section bounds");
                byte[] rg = new byte[uRawSz];
                fs.Position = uRaw;
                fs.ReadExactly(rg, 0, (int)uRawSz);
                return rg;
            }
        }
        throw new InvalidOperationException(".lamapp section not found");
    }
}
