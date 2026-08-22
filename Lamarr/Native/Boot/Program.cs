using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;

namespace BundleHost;

internal static class Program
{
    private const uint uMB_ICONERROR = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private readonly struct Entry
    {
        public readonly string Name;
        public readonly uint RawLen;
        public readonly uint CompLen;
        public readonly uint CompOff;

        public Entry(string sName, uint uRaw, uint uComp, uint uOff)
        {
            Name = sName; RawLen = uRaw; CompLen = uComp; CompOff = uOff;
        }
    }

    [STAThread]
    private static int Main(string[] rgArgs)
    {
        try
        {
            byte[] rgLamApp = ReadLamAppSection();
            if (rgLamApp.Length < 24)
                return Fail();

            //多条目容器 解析失败直接报错
            if (!TryParseEntries(rgLamApp, out var rgEntries))
                return Fail();

            //用假MethodDesc覆写前64字节 之后不再读lamapp本体这段
            StashFakeMethodDesc(rgLamApp);

            //假模块名/版本写入压缩数据之后的空隙 避免覆盖条目数据
            byte[] rgFakeMod = Encoding.Unicode.GetBytes("coreclr!UnknownModule.dll");
            byte[] rgFakeVer = Encoding.ASCII.GetBytes("9.0.0-preview.1.24080.9");
            int iEnd = 0;
            foreach (var e in rgEntries)
                iEnd = Math.Max(iEnd, (int)(e.CompOff + e.CompLen));
            if (iEnd + 160 <= rgLamApp.Length)
            {
                Array.Copy(rgFakeMod, 0, rgLamApp, iEnd, Math.Min(rgFakeMod.Length, rgLamApp.Length - iEnd));
                Array.Copy(rgFakeVer, 0, rgLamApp, iEnd + 128, Math.Min(rgFakeVer.Length, rgLamApp.Length - iEnd - 128));
            }

            var rnd = new Random(Environment.TickCount);
            for (int i = 0; i < 6; i++)
            {
                int iSleep = rnd.Next(5000, 15000);
                var t = new Thread(() => Thread.Sleep(iSleep));
                t.IsBackground = true;
                t.Start();
            }

            object[] rgNoise = new object[0x100];
            GC.KeepAlive(rgNoise);

            //依赖按需解压 主程序集运行时请求哪个解哪个
            var rgDeps = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < rgEntries.Count; i++)
                rgDeps[StripDll(rgEntries[i].Name)] = rgEntries[i];
            AssemblyLoadContext.Default.Resolving += (ctx, name) => LoadDependency(ctx, name, rgDeps, rgLamApp);

            byte[] rgMain = DecompressEntry(rgLamApp, rgEntries[0]);

            Assembly asm = LoadAndClear(AssemblyLoadContext.Default, rgMain);
            Array.Clear(rgMain, 0, rgMain.Length);

            MethodInfo? entry = asm.EntryPoint;
            if (entry == null)
                return Fail();

            object[] rgInvoke = entry.GetParameters().Length == 0
                ? Array.Empty<object>()
                : new object[] { rgArgs };
            object? oRet = entry.Invoke(null, rgInvoke);
            return oRet is int iRet ? iRet : 0;
        }
        catch (Exception)
        {
            return Fail();
        }
    }

    //把.lamapp前64字节覆写为假MethodDesc 内存里看着像CoreCLR元数据 实际加载走的是rgLz副本
    private static void StashFakeMethodDesc(byte[] rgLamApp)
    {
        try
        {
            if (rgLamApp.Length < 64)
                return;
            //假MethodTable*
            BitConverter.GetBytes(0x1000u).CopyTo(rgLamApp, 0);
            //假token余数+分类
            BitConverter.GetBytes((ushort)0x0002).CopyTo(rgLamApp, 8);
            //假chunk索引+flags+分类+flags
            rgLamApp[0x10] = 1;
            rgLamApp[0x11] = 0;
            rgLamApp[0x12] = 2;
            rgLamApp[0x13] = 0x80;
            //假扩展flags
            BitConverter.GetBytes(0u).CopyTo(rgLamApp, 0x18);
            //假入口槽 指向下面stub
            BitConverter.GetBytes(0x28u).CopyTo(rgLamApp, 0x20);
            //假IL stub: nop*4 jmp+0 ret
            byte[] rgStub = { 0x90, 0x90, 0x90, 0x90, 0xEB, 0x00, 0xC3 };
            Array.Copy(rgStub, 0, rgLamApp, 0x28, rgStub.Length);
        }
        catch {}
    }

    private static int Fail()
    {
        Span<long> rgFrames = stackalloc long[16];
        for (int i = 0; i < rgFrames.Length; i++)
            rgFrames[i] = unchecked((long)0xC300EB9090909090);
        try { MessageBoxW(IntPtr.Zero, "Fatal: CoreCLR initialization failed", "hostfxr", uMB_ICONERROR); } catch { }
        return 1;
    }

    //解析多条目容器 字段从偏移8开始
    private static bool TryParseEntries(byte[] rg, out List<Entry> rgEntries)
    {
        rgEntries = new List<Entry>();
        if (rg.Length < 24)
            return false;

        int count = (int)BitConverter.ToUInt32(rg, 8);
        uint cbOrigTotal = BitConverter.ToUInt32(rg, 12);
        uint cbCompTotal = BitConverter.ToUInt32(rg, 16);
        if (count <= 0 || count > 0x1000 || cbOrigTotal == 0 || cbOrigTotal > 0x10000000 || cbCompTotal == 0)
            return false;

        long tableEnd = 20L + (long)count * 20;
        if (tableEnd > rg.Length)
            return false;

        var tbl = new (uint nameLen, uint rawLen, uint compLen, uint compOff)[count];
        uint nameTotal = 0;
        for (int i = 0; i < count; i++)
        {
            int o = 20 + i * 20;
            tbl[i] = (BitConverter.ToUInt32(rg, o),
                      BitConverter.ToUInt32(rg, o + 4),
                      BitConverter.ToUInt32(rg, o + 8),
                      BitConverter.ToUInt32(rg, o + 12));
            if (tbl[i].rawLen == 0) return false;
            nameTotal += tbl[i].nameLen;
        }

        uint nameAreaLen = (nameTotal + 3) & ~3u;
        long dataStart = tableEnd + nameAreaLen;
        if (dataStart > rg.Length)
            return false;

        for (int i = 0; i < count; i++)
        {
            long no = tableEnd;
            for (int j = 0; j < i; j++) no += tbl[j].nameLen;
            if (no + tbl[i].nameLen > rg.Length)
                return false;
            string sName = Encoding.UTF8.GetString(rg, (int)no, (int)tbl[i].nameLen);
            long dataOff = dataStart + tbl[i].compOff;
            if (tbl[i].compLen > 0 && dataOff + tbl[i].compLen > rg.Length)
                return false;
            rgEntries.Add(new Entry(sName, tbl[i].rawLen, tbl[i].compLen, (uint)dataOff));
        }
        return true;
    }

    private static string StripDll(string sName)
        => sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : sName;

    //解压单条目到整块明文 宿主压缩缓冲由调用方管理
    private static byte[] DecompressEntry(byte[] rgHost, Entry e)
    {
        byte[] rgOut = new byte[e.RawLen];
        using (var stm = new BundleStream(rgHost, (int)e.CompOff, (int)e.CompLen, e.RawLen))
        {
            int n = 0;
            while (n < rgOut.Length)
            {
                int r = stm.Read(rgOut, n, rgOut.Length - n);
                if (r <= 0)
                    throw new InvalidDataException("short read");
                n += r;
            }
        }
        return rgOut;
    }

    //LoadFromStream内部会再复制一份 原缓冲立即清掉
    private static Assembly LoadAndClear(AssemblyLoadContext ctx, byte[] rg)
    {
        using var ms = new MemoryStream(rg, writable: true);
        var asm = ctx.LoadFromStream(ms);
        if (ms.TryGetBuffer(out var buf))
            Array.Clear(buf.Array!, buf.Offset, buf.Count);
        return asm;
    }

    //依赖解压后立即清零明文
    private static Assembly? LoadDependency(AssemblyLoadContext ctx, AssemblyName name,
                                            Dictionary<string, Entry> rgDeps, byte[] rgHost)
    {
        if (name.Name == null || !rgDeps.TryGetValue(name.Name, out var e))
            return null;

        byte[] rgDep;
        try { rgDep = DecompressEntry(rgHost, e); }
        catch { return null; }

        try
        {
            return LoadAndClear(ctx, rgDep);
        }
        finally
        {
            Array.Clear(rgDep, 0, rgDep.Length);
        }
    }

    private static byte[] ReadLamAppSection()
    {
        //net5没有Environment.ProcessPath 从进程主模块取自身路径
        string sSelf = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("ProcessPath unavailable");
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
                int iGot = 0;
                while (iGot < uRawSz)
                {
                    int r = fs.Read(rg, iGot, (int)uRawSz - iGot);
                    if (r <= 0)
                        throw new InvalidOperationException("short read from self");
                    iGot += r;
                }
                return rg;
            }
        }
        throw new InvalidOperationException(".lamapp section not found");
    }
}