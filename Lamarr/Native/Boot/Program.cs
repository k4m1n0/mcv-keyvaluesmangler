using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace LamarrBoot;

internal static class Program
{
    private const uint uMB_ICONERROR = 0x10;
    private static readonly Dictionary<string, byte[]> sCache = new();

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

            //magic "Lamarr!!" + count + entry table + names + 压缩块
            byte[] rgMain = ParseLamApp(rgLamApp);

            //依赖已全部进.lamapp，CoreCLR在bundle里找不到 -> 走Resolving从这里喂
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                if (name.Name != null && sCache.TryGetValue(name.Name + ".dll", out var bytes))
                    return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
                return null;
            };

            Assembly asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgMain));
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

    private static byte[] ParseLamApp(byte[] rg)
    {
        uint uCount = BitConverter.ToUInt32(rg, 8);
        if (uCount == 0 || uCount > 0x10000 || 20 + (long)uCount * 20 > rg.Length)
            throw new InvalidOperationException("bad .lamapp entry count");
        int iNames = 20 + (int)uCount * 20;
        var rgNames = new string[uCount];
        for (int i = 0; i < uCount; i++)
        {
            int o = 20 + i * 20;
            uint uNameLen = BitConverter.ToUInt32(rg, o);
            if (uNameLen == 0 || iNames + uNameLen > rg.Length)
                throw new InvalidOperationException("bad .lamapp name");
            rgNames[i] = Encoding.UTF8.GetString(rg, iNames, (int)uNameLen);
            iNames += (int)uNameLen;
            while ((iNames & 3) != 0) iNames++;
        }
        byte[] rgMain = Array.Empty<byte>();
        for (int i = 0; i < uCount; i++)
        {
            int o = 20 + i * 20;
            uint uRawLen = BitConverter.ToUInt32(rg, o + 4);
            uint uCompLen = BitConverter.ToUInt32(rg, o + 8);
            uint uCompOff = BitConverter.ToUInt32(rg, o + 12);
            if (uRawLen == 0 || uCompLen == 0 || uRawLen > 0x10000000 || uCompLen >= uRawLen || uCompOff + uCompLen > rg.Length)
                throw new InvalidOperationException("bad .lamapp entry");
            byte[] rgLz = new byte[uCompLen];
            Array.Copy(rg, (int)uCompOff, rgLz, 0, (int)uCompLen);
            byte[] rgOut = new byte[uRawLen];
            uint pcb = uRawLen;
            if (Lamarr.LamarrDecoder.Decode(rgOut, ref pcb, rgLz, uCompLen) != 0 || pcb != uRawLen)
                throw new InvalidOperationException("Lamarr decode failed (lamapp entry)");
            if (i == 0) rgMain = rgOut;
            else sCache[rgNames[i]] = rgOut;
        }
        if (rgMain.Length == 0)
            throw new InvalidOperationException(".lamapp has no main entry");
        return rgMain;
    }

    private static int Fail(string sMsg)
    {
        try { MessageBoxW(IntPtr.Zero, sMsg, "Lamarr loader", uMB_ICONERROR); } catch { }
        return 1;
    }

    private static byte[] ReadLamAppSection()
    {
        //读自身exe的.lamapp段 数据区在末尾
        string sSelf = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");
        using var fs = new FileStream(sSelf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] rgHdr = new byte[0x400];
        int iN = fs.Read(rgHdr, 0, rgHdr.Length);
        if (iN < 0x400)
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
