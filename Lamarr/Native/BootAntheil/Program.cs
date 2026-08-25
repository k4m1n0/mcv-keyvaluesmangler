using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace A0;

internal static class P
{
    private const uint U0 = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int M0(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll")]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProc, ref bool pb);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr hProc, int cls, out IntPtr info, int len, out int ret);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddr, UIntPtr dwSize, uint flAlloc, uint flProtect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecPage(byte[] state, byte[] hist, byte[] page, byte[] src);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int JitInstall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitSetKey(uint key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitAddSig(uint crc);

    //密钥派生 常量A^B参与运算
    private static readonly uint LK0A = 0x12345678, LK0B = 0x9328CBBD;//0x811C9DC5
    private static readonly uint LK1A = 0x11111111, LK1B = 0x111110A2;//0x01000193
    private static readonly uint LGAA = 0x0F0F0F0F, LGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint LLCA = 0x0A0A0A0A, LLCB = 0x0A136C07;//0x0019660D
    private static readonly uint LQCA = 0x5A5A5A5A, LQCB = 0x6634A905;//0x3C6EF35F
    //K_seed 16B 由KS常量A^B派生
    private static readonly uint KS0A = 0x1A2B3C4D, KS0B = 0x9A293C5D;//0x80020010
    private static readonly uint KS1A = 0x12345678, KS1B = 0xF9A45750;//0xEB900128
    private static readonly uint KS2A = 0x5A5A5A5A, KS2B = 0x584A5A99;//0x021000C3
    private static readonly uint KS3A = 0x0F0F0F0F, KS3B = 0x9F278F0E;//0x90288001

    private const int LB_SEED = 64, LB_HASH = 96, LB_HEAD = 100, LB_TBL = 116;
    private const string S_DECODER = "Iamdec";//真解码器条目名
    private const string S_JIT = "!!jhk";
    private const string S_SIG = "!!sig";
    private static IntPtr _decPtr = IntPtr.Zero;
    private static IntPtr _jitBase = IntPtr.Zero;
    private static uint _jitTextVa = 0;
    private static JitInstall _jitInstall = null!;
    private static JitSetKey _jitSetKey = null!;
    private static JitAddSig _jitAddSig = null!;

    //uint[]与k+i*delta异或 按UTF-16拼字符串
    private static string S1(uint k, uint[] v)
    {
        char[] c = new char[v.Length * 2];
        for (int i = 0; i < v.Length; i++)
        {
            uint t = v[i] ^ (k + (LGAA ^ LGAB) * (uint)i);
            c[i * 2] = (char)(t & 0xFFFF);
            c[i * 2 + 1] = (char)(t >> 16);
        }
        int n = Array.IndexOf(c, '\0');
        if (n < 0) n = c.Length;
        return new string(c, 0, n);
    }

    //K_seed 16B 由KS常量XOR填充
    private static byte[] KSeed()
    {
        byte[] k = new byte[16];
        BitConverter.GetBytes(KS0A ^ KS0B).CopyTo(k, 0);
        BitConverter.GetBytes(KS1A ^ KS1B).CopyTo(k, 4);
        BitConverter.GetBytes(KS2A ^ KS2B).CopyTo(k, 8);
        BitConverter.GetBytes(KS3A ^ KS3B).CopyTo(k, 12);
        return k;
    }

    private static uint K1(byte[] seed)
    {
        uint h = LK0A ^ LK0B;
        foreach (byte ch in seed)
        {
            h ^= ch;
            h *= LK1A ^ LK1B;
        }
        return h;
    }

    //perm 由k派生的4元素置换表
    private static int[] K2(uint k)
    {
        int[] a = { 0, 1, 2, 3 };
        uint s = k;
        for (int i = 3; i > 0; i--)
        {
            s = s * (LLCA ^ LLCB) + (LQCA ^ LQCB);
            int j = (int)(s % (uint)(i + 1));
            (a[i], a[j]) = (a[j], a[i]);
        }
        return a;
    }

    private static uint K3(uint k, int i) => k + (LGAA ^ LGAB) * (uint)i;

    private static uint Q(byte[] b, int o) => BitConverter.ToUInt32(b, o);

    [STAThread]
    private static int Main(string[] a) => B(a);

    private static int B(string[] a)
    {
        try
        {
            if (a.Length == 1 && a[0] == "--lamarr-selftest")
                return Selftest();
            H1(a);
            byte[] g;
            List<(string, uint, uint, uint)> l;

            AD();

            if ((RuntimeSeed() & 0x20000000u) != 0)
                goto Q1;
            g = X6();
            goto Q2;
        Q1:
            g = X6();
        Q2:
            if (g.Length < 128)
                goto QF;
            H2(g);
            AD();
            if (!X1(g, out l))
                goto QF;
            X0(g);
            EnsureDecoder(g, l);
            EnsureJitHook(g, l);
            InjectSigs(g, l);
            if (_jitInstall != null)
            {
                _jitSetKey(K1(KSeed()));
                _jitInstall();
            }
            if ((RuntimeSeed() & 0x10000000u) == 0)
                goto Q3;
            W1(g, l);
            goto Q4;
        Q3:
            W1(g, l);
        Q4:
            byte[] b = X3(g, l[0]);
            Assembly asm = X4(AssemblyLoadContext.Default, b);
            Array.Clear(b, 0, b.Length);

            MethodInfo? ep = asm.EntryPoint;
            if (ep == null) goto QF;
            object[] pa = ep.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { a };
            return ep.Invoke(null, pa) is int r ? r : 0;
        QF:
            return F();
        }
        catch (Exception)
        {
            return F();
        }
    }

    private static uint RuntimeSeed() => unchecked((uint)Environment.TickCount ^ (uint)Process.GetCurrentProcess().Id);

    //反调试 检测到调试器即FailFast
    private static void AD()
    {
        bool dbg = IsDebuggerPresent();
        if (!dbg)
        {
            bool cr = false;
            if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref cr) && cr)
                dbg = true;
        }
        if (!dbg)
        {
            IntPtr port = IntPtr.Zero;
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 7, out port, IntPtr.Size, out _) == 0 && port != IntPtr.Zero)
                dbg = true;
        }
        if (dbg)
            Environment.FailFast(null);
    }

    private static int Selftest()
    {
        byte[] g = X6();
        if (!X1(g, out var l) || l.Count == 0) return 2;
        EnsureDecoder(g, l);
        byte[] b = X3(g, l[0]);
        uint exp = BitConverter.ToUInt32(g, LB_HASH);
        uint act = Fnv1a(b);
        Array.Clear(b, 0, b.Length);
        return act == exp ? 0 : 3;
    }

    private static uint Fnv1a(byte[] d)
    {
        uint h = LK0A ^ LK0B;
        foreach (byte x in d) { h ^= x; h *= LK1A ^ LK1B; }
        return h;
    }

    //解析--参数 诱饵
    private static void H1(string[] a)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < a.Length && i < 8; i++)
        {
            string s = a[i];
            if (s.Length >= 2 && s[0] == '-' && s[1] == '-')
                m[s] = s;
        }
        GC.KeepAlive(m);
    }

    //计算g前0x100字节hash 诱饵
    private static void H2(byte[] g)
    {
        uint h = LK0A ^ LK0B;
        int n = Math.Min(g.Length, 0x100);
        for (int i = 0; i < n; i++)
            h = (h ^ g[i]) * (LK1A ^ LK1B);
        GC.KeepAlive(h);
    }

    //写元数据 并注册AssemblyLoadContext.Resolving
    private static void W1(byte[] g, List<(string, uint, uint, uint)> l)
    {
        byte[] m = Encoding.Unicode.GetBytes(S1(0xCF7DA6AEu, new uint[] { 0xCF12A6CDu, 0x6DD02015u, 0x0B809A43u, 0xAA0513ABu, 0x48358DC7u, 0xE6FD0720u, 0x84BD816Bu, 0x234CFAD3u, 0xC15D7419u, 0x5F1CEE5Au, 0xFD86678Du, 0x9BB3E1C5u, 0x3A175B36u }));
        byte[] v = Encoding.ASCII.GetBytes(S1(0x3EBB0388u, new uint[] { 0x3E9503B1u, 0xDCDC7D71u, 0x7B04F6CAu, 0x191370C3u, 0xB7EEEA09u, 0x55B5644Cu, 0xF429DDA9u, 0x921157A6u, 0x3042D162u, 0xCE964B39u, 0x6CCBC4F2u, 0x0B1D3E42u }));
        int e = 0;
        foreach (var t in l)
            e = Math.Max(e, (int)(t.Item4 + t.Item3));
        if (e + 160 <= g.Length)
        {
            Array.Copy(m, 0, g, e, Math.Min(m.Length, g.Length - e));
            Array.Copy(v, 0, g, e + 128, Math.Min(v.Length, g.Length - e - 128));
        }

        var d = new Dictionary<string, (string, uint, uint, uint)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < l.Count; i++)
            d[X2(l[i].Item1)] = l[i];
        AssemblyLoadContext.Default.Resolving += (ctx, name) => X5(ctx, name, d, g);
    }

    //伪造MethodDesc 供CoreCLR调用
    private static void X0(byte[] g)
    {
        try
        {
            if (g.Length < 64) return;
            BitConverter.GetBytes(0x1000u).CopyTo(g, 0);
            BitConverter.GetBytes((ushort)0x0002).CopyTo(g, 8);
            g[0x10] = 1; g[0x11] = 0; g[0x12] = 2; g[0x13] = 0x80;
            BitConverter.GetBytes(0u).CopyTo(g, 0x18);
            BitConverter.GetBytes(0x28u).CopyTo(g, 0x20);
            byte[] s = { 0x90, 0x90, 0x90, 0x90, 0xEB, 0x00, 0xC3 };
            Array.Copy(s, 0, g, 0x28, s.Length);
        }
        catch { }
    }

    private static int F()
    {
        Span<long> s = stackalloc long[16];
        for (int i = 0; i < s.Length; i++)
            s[i] = unchecked((long)0xC300EB9090909090);
        try
        {
            M0(IntPtr.Zero,
               S1(0x2ABF5FF6u, new uint[] { 0x2ADE5FB0u, 0xC897D9DBu, 0x67145304u, 0x0526CD01u, 0xA3EF46B5u, 0x4197C0F6u, 0xE05E3A00u, 0x7E2AB425u, 0x1C122DD0u, 0xBADBA703u, 0x58862151u, 0xF75B9A80u, 0x952D14C3u, 0x33FF8E32u, 0xD1E8087Au, 0x6F9E81ABu, 0x0E5AFBEFu, 0xAC0A755Au }),
               S1(0x29CE007Au, new uint[] { 0x29A10012u, 0xC8717A40u, 0x6644F38Au, 0x04746DD7u }),
               U0);
        }
        catch { }
        return 1;
    }

    //解析bundle条目表 用seed派生key解密
    private static bool X1(byte[] g, out List<(string, uint, uint, uint)> r)
    {
        r = new List<(string, uint, uint, uint)>();
        if (g.Length < 128) return false;

        uint k;
        {
            byte[] seed = new byte[32];
            byte[] kseed = KSeed();
            for (int i = 0; i < 32; i++)
                seed[i] = (byte)(g[LB_SEED + i] ^ kseed[i % 16]);
            k = K1(seed);
            Array.Clear(seed, 0, seed.Length);
            Array.Clear(kseed, 0, kseed.Length);
        }

        int n = (int)(Q(g, LB_HEAD) ^ k);
        uint o1 = Q(g, LB_HEAD + 4) ^ k;
        uint o2 = Q(g, LB_HEAD + 8) ^ k;
        int nd = (int)(Q(g, LB_HEAD + 12) ^ K3(k, n));
        if (n <= 0 || n > 0x1000 || o1 == 0 || o1 > 0x10000000 || o2 == 0 || nd < 0 || nd > 0x100)
            return false;

        int tn = n + nd;
        long tEnd = LB_TBL + (long)tn * 20;
        if (tEnd > g.Length) return false;

        int[] pm = K2(k);
        var row = new uint[tn * 5];
        uint nt = 0;
        for (int i = 0; i < tn; i++)
        {
            uint kk = K3(k, i);
            int o = LB_TBL + i * 20;
            uint[] f = new uint[4];
            for (int s = 0; s < 4; s++)
                f[pm[s]] = Q(g, o + s * 4) ^ kk;
            row[i * 5 + 0] = f[0];
            row[i * 5 + 1] = f[1];
            row[i * 5 + 2] = f[2];
            row[i * 5 + 3] = f[3];
            nt += f[0];
        }

        long na = (nt + 3u) & ~3u;
        long ds = tEnd + na;
        if (ds > g.Length) return false;

        for (int i = 0; i < n; i++)
        {
            if (row[i * 5 + 1] == 0) return false;
            long no = tEnd;
            for (int j = 0; j < i; j++) no += row[j * 5];
            if (no + row[i * 5] > g.Length) return false;
            string s = Encoding.UTF8.GetString(g, (int)no, (int)row[i * 5]);
            long doff = ds + row[i * 5 + 3];
            if (row[i * 5 + 2] > 0 && doff + row[i * 5 + 2] > g.Length) return false;
            r.Add((s, row[i * 5 + 1], row[i * 5 + 2], (uint)doff));
        }
        return true;
    }

    private static string X2(string s)
        => s.EndsWith(S1(0x28CF58B4u, new uint[] { 0x28AB589Au, 0xC76AD201u }), StringComparison.OrdinalIgnoreCase)
            ? s.Substring(0, s.Length - 4) : s;

    //提取Iamdec解码器 以LoadBare加载
    private static void EnsureDecoder(byte[] g, List<(string, uint, uint, uint)> l)
    {
        if (_decPtr != IntPtr.Zero) return;
        foreach (var e in l)
        {
            if (e.Item1 == S_DECODER)
            {
                byte[] kseed = KSeed();
                byte[] dll = new byte[e.Item2];
                for (int i = 0; i < dll.Length; i++)
                    dll[i] = (byte)(g[(int)e.Item4 + i] ^ kseed[i % 16]);
                Array.Clear(kseed, 0, kseed.Length);
                _decPtr = LoadBare(dll);
                Array.Clear(dll, 0, dll.Length);
                return;
            }
        }
        throw new InvalidDataException(S1(0x7EF1D782u, new uint[] { 0x7E99D7F1u, 0x1D5B5154u }));
    }

    //手工映射裸PE返回基址 .text/.rdata按RVA排布
    private static IntPtr LoadBare(byte[] dll)
    {
        int iPe = BitConverter.ToInt32(dll, 0x3C);
        ushort cnt = BitConverter.ToUInt16(dll, iPe + 6);
        ushort opt = BitConverter.ToUInt16(dll, iPe + 20);
        int iSec = iPe + 24 + opt;
        uint textVa = 0; int textRaw = 0, textSz = 0;
        uint dataVa = 0; int dataRaw = 0, dataSz = 0;
        for (int i = 0; i < cnt; i++)
        {
            int o = iSec + i * 40;
            string nm = Encoding.ASCII.GetString(dll, o, 8).TrimEnd('\0');
            uint va = BitConverter.ToUInt32(dll, o + 12);
            uint raw = BitConverter.ToUInt32(dll, o + 20);
            uint rsz = BitConverter.ToUInt32(dll, o + 16);
            if (nm == ".text") { textVa = va; textRaw = (int)raw; textSz = (int)rsz; }
            else if (nm == ".rdata") { dataVa = va; dataRaw = (int)raw; dataSz = (int)rsz; }
        }
        if (textSz == 0)
            throw new InvalidDataException("bad decoder");
        IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)(textSz + 0x1000 + (dataSz > 0 ? (int)(dataVa - textVa) + dataSz : 0) + 0x1000), 0x3000, 0x40);
        if (p == IntPtr.Zero)
            throw new InvalidDataException("valloc");
        Marshal.Copy(dll, textRaw, p, textSz);
        if (dataSz > 0)
            Marshal.Copy(dll, dataRaw, new IntPtr(p.ToInt64() + (dataVa - textVa)), dataSz);
        return p;
    }
    //解析PE导出表
    private static Dictionary<string, uint> ParseExports(byte[] dll)
    {
        var res = new Dictionary<string, uint>(StringComparer.Ordinal);
        int iPe = BitConverter.ToInt32(dll, 0x3C);
        ushort opt = BitConverter.ToUInt16(dll, iPe + 20);
        ushort magic = BitConverter.ToUInt16(dll, iPe + 24);
        int dd = iPe + 24 + (magic == 0x20B ? 112 : 96);
        uint expRva = BitConverter.ToUInt32(dll, dd);
        if (expRva == 0) return res;
        int eo = RvaToOff(dll, iPe, opt, expRva);
        if (eo <= 0) return res;
        int nn = BitConverter.ToInt32(dll, eo + 0x18);
        int no = RvaToOff(dll, iPe, opt, BitConverter.ToUInt32(dll, eo + 0x20));
        int oo = RvaToOff(dll, iPe, opt, BitConverter.ToUInt32(dll, eo + 0x24));
        int fo = RvaToOff(dll, iPe, opt, BitConverter.ToUInt32(dll, eo + 0x1C));
        for (int i = 0; i < nn; i++)
        {
            int nr = BitConverter.ToInt32(dll, no + i * 4);
            int n2 = RvaToOff(dll, iPe, opt, (uint)nr);
            if (n2 <= 0) continue;
            int e2 = n2;
            while (e2 < dll.Length && dll[e2] != 0) e2++;
            string name = Encoding.ASCII.GetString(dll, n2, e2 - n2);
            int ord = BitConverter.ToUInt16(dll, oo + i * 2);
            uint fnRva = BitConverter.ToUInt32(dll, fo + ord * 4);
            res[name] = fnRva;
        }
        return res;
    }

    private static int RvaToOff(byte[] dll, int iPe, ushort opt, uint rva)
    {
        ushort cnt = BitConverter.ToUInt16(dll, iPe + 6);
        int iSec = iPe + 24 + opt;
        for (int i = 0; i < cnt; i++)
        {
            int o = iSec + i * 40;
            uint vs = BitConverter.ToUInt32(dll, o + 8);
            uint va = BitConverter.ToUInt32(dll, o + 12);
            uint rs = BitConverter.ToUInt32(dll, o + 16);
            uint po = BitConverter.ToUInt32(dll, o + 20);
            uint end = Math.Max(vs, rs);
            if (rva >= va && rva < va + end) return (int)(po + (rva - va));
        }
        return -1;
    }

    private static uint PeTextVa(byte[] dll)
    {
        int iPe = BitConverter.ToInt32(dll, 0x3C);
        ushort cnt = BitConverter.ToUInt16(dll, iPe + 6);
        ushort opt = BitConverter.ToUInt16(dll, iPe + 20);
        int iSec = iPe + 24 + opt;
        for (int i = 0; i < cnt; i++)
        {
            int o = iSec + i * 40;
            if (Encoding.ASCII.GetString(dll, o, 8).TrimEnd('\0') == ".text")
                return BitConverter.ToUInt32(dll, o + 12);
        }
        return 0x1000;
    }

    private static T Mk<T>(uint rva) where T : Delegate
    {
        var addr = new IntPtr(_jitBase.ToInt64() + (rva - _jitTextVa));
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    private static void EnsureJitHook(byte[] g, List<(string, uint, uint, uint)> l)
    {
        foreach (var e in l)
        {
            if (e.Item1 == S_JIT)
            {
                byte[] dll = X3(g, e);
                _jitBase = LoadBare(dll);
                _jitTextVa = PeTextVa(dll);
                var ex = ParseExports(dll);
                _jitInstall = Mk<JitInstall>(ex["InstallJitHook"]);
                _jitSetKey = Mk<JitSetKey>(ex["SetJitHookKey"]);
                _jitAddSig = Mk<JitAddSig>(ex["AddPayloadSig"]);
                Array.Clear(dll, 0, dll.Length);
                return;
            }
        }
    }

    private static void InjectSigs(byte[] g, List<(string, uint, uint, uint)> l)
    {
        foreach (var e in l)
        {
            if (e.Item1 == S_SIG)
            {
                byte[] sig = X3(g, e);
                for (int i = 0; i + 4 <= sig.Length; i += 4)
                    _jitAddSig(BitConverter.ToUInt32(sig, i));
                Array.Clear(sig, 0, sig.Length);
                return;
            }
        }
    }

    //解压条目 压缩按4KB页调Iamdec 非压缩直接XOR
    private static byte[] X3(byte[] g, (string, uint, uint, uint) e)
    {
        int doff = (int)e.Item4;
        uint rawLen = e.Item2;
        int compLen = (int)e.Item3;
        if (compLen == (int)rawLen)
        {
            //非压缩 直接XOR K_seed还原
            byte[] r = new byte[rawLen];
            byte[] kseed = KSeed();
            for (int i = 0; i < r.Length; i++)
                r[i] = (byte)(g[doff + i] ^ kseed[i % 16]);
            Array.Clear(kseed, 0, kseed.Length);
            return r;
        }
        if (_decPtr == IntPtr.Zero)
            throw new InvalidDataException("decoder not loaded");
        var dec = Marshal.GetDelegateForFunctionPointer<DecPage>(_decPtr);
        byte[] comp = new byte[compLen];
        Array.Copy(g, doff, comp, 0, compLen);
        byte[] outBuf = new byte[rawLen];
        byte[] hist = new byte[0x80000];
        byte[] page = new byte[0x1000];
        byte[] st = new byte[0x38];
        BitConverter.GetBytes(1u).CopyTo(st, 0x00);//uInPos
        BitConverter.GetBytes(1u).CopyTo(st, 0x04);//iHist
        BitConverter.GetBytes(1u).CopyTo(st, 0x08);//uOutPos
        BitConverter.GetBytes((uint)compLen).CopyTo(st, 0x28);//srcLen
        BitConverter.GetBytes(rawLen).CopyTo(st, 0x34);//dstLen
        hist[0] = comp[0];
        for (uint pg = 0; pg * 0x1000 < rawLen; pg++)
        {
            uint ps = pg * 0x1000;
            uint pe = Math.Min(ps + 0x1000, rawLen);
            if (pg == 0) page[0] = comp[0];
            else Array.Clear(page, 0, 0x1000);
            BitConverter.GetBytes(ps).CopyTo(st, 0x2C);
            BitConverter.GetBytes(pe).CopyTo(st, 0x30);
            int rc = dec(st, hist, page, comp);
            if (rc != 0)
                throw new InvalidDataException("dec rc=" + rc.ToString("X"));
            int c = (int)(pe - ps);
            Array.Copy(page, 0, outBuf, (int)ps, c);
        }
        Array.Clear(comp, 0, comp.Length);
        Array.Clear(st, 0, st.Length);
        Array.Clear(hist, 0, hist.Length);
        return outBuf;
    }

    private static Assembly X4(AssemblyLoadContext c, byte[] b)
    {
        using var ms = new MemoryStream(b, writable: true);
        var asm = c.LoadFromStream(ms);
        if (ms.TryGetBuffer(out var buf))
            Array.Clear(buf.Array!, buf.Offset, buf.Count);
        return asm;
    }

    private static Assembly? X5(AssemblyLoadContext c, AssemblyName n,
                                Dictionary<string, (string, uint, uint, uint)> d, byte[] g)
    {
        if (n.Name == null || !d.TryGetValue(n.Name, out var e))
            return null;
        byte[] b;
        try { b = X3(g, e); }
        catch { return null; }
        try { return X4(c, b); }
        finally { Array.Clear(b, 0, b.Length); }
    }

    private static byte[] X6()
    {
        string sSelf = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException(S1(0x5B5DDEAEu, new uint[] { 0x5B2FDEFEu, 0xF9F65808u, 0x97BFD245u, 0x36544BAAu, 0xD44FC5F3u, 0x72533F23u, 0x10C4B971u, 0xAE9432DCu, 0x4D70AC17u, 0xEB302643u, 0x89E49F8Au, 0x27C019C4u }));
        string sSec = S1(0xD8727B8Bu, new uint[] { 0xD8007BA5u, 0x76C8F520u, 0x14806E89u });
        using var fs = new FileStream(sSelf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] h = new byte[0x400];
        int n = fs.Read(h, 0, h.Length);
        if (n < 0x400)
            throw new InvalidOperationException(S1(0x338B2FA6u, new uint[] { 0x33E32FD5u, 0xD1B0A930u, 0x6FDA236Cu, 0x0E749C81u, 0xAC0116AAu, 0x4AC19026u, 0xE8BD0998u, 0x870F83C7u }));

        int iPe = BitConverter.ToInt32(h, 0x3C);
        if (iPe < 0 || iPe + 0x18 > h.Length || BitConverter.ToUInt32(h, iPe) != 0x4550)
            throw new InvalidOperationException(S1(0x45ADEC6Bu, new uint[] { 0x45C2EC03u, 0xE3916657u, 0x8275DFFDu, 0x207459E5u, 0xBEE4D321u, 0x5CE34D7Cu, 0xFADAC6A0u, 0x9977402Au, 0x3700BA13u, 0xD5C03381u, 0x73BDADC2u }));

        ushort usCnt = BitConverter.ToUInt16(h, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(h, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        if (iSec < 0 || iSec + usCnt * 40 > h.Length)
            throw new InvalidOperationException(S1(0x29B16E1Au, new uint[] { 0x29D96E69u, 0xC79AE7BCu, 0x660061F8u, 0x0432DB36u, 0xA2FB549Du, 0x40A9CEDEu, 0xDEDE481Eu, 0x7D54C25Du, 0x1B013B80u, 0xB9A4B5FEu }));

        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            if (Encoding.ASCII.GetString(h, o, 8).TrimEnd('\0') == sSec)
            {
                uint uRaw = BitConverter.ToUInt32(h, o + 20);
                uint uRawSz = BitConverter.ToUInt32(h, o + 16);
                if (uRawSz == 0 || uRaw + uRawSz > (ulong)fs.Length)
                    throw new InvalidOperationException(S1(0x555FFA2Eu, new uint[] { 0x553EFA4Cu, 0xF3B77383u, 0x91BCED8Eu, 0x3067673Du, 0xCE5CE166u, 0x6C065AEBu, 0x0ACFD4E1u, 0xA88D4E49u, 0x4775C799u, 0xE531418Fu, 0x83FFBB07u, 0x21A6354Fu, 0xBFF9AEA9u }));
                byte[] g = new byte[uRawSz];
                fs.Position = uRaw;
                int got = 0;
                while (got < uRawSz)
                {
                    int r = fs.Read(g, got, (int)uRawSz - got);
                    if (r <= 0)
                        throw new InvalidOperationException(S1(0xE8E251BAu, new uint[] { 0xE88A51C9u, 0x876BCB1Cu, 0x25714558u, 0xC3EDBE97u, 0x61A438FFu, 0xFF91B277u, 0x9E402C62u, 0x3C46A5A4u, 0xDAFB1FF1u, 0x78B39957u }));
                    got += r;
                }
                return g;
            }
        }
        throw new InvalidOperationException(S1(0x820212E1u, new uint[] { 0x827012CFu, 0x20588CFEu, 0xBE100627u, 0x5CDB802Cu, 0xFABCF9A0u, 0x997E730Au, 0x3720ED58u, 0xD5E866D0u, 0x73C9E0C6u, 0x11935A42u, 0xB059D474u, 0x4E004DBAu }));
    }
}
