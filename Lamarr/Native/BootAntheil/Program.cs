using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace A0;

internal static class P
{

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll")]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProc, ref bool pb);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr hProc, int cls, out IntPtr info, int len, out int ret);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr hProc, int cls, byte[] info, int len, out int ret);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint dwAccess, bool bInherit, uint dwThreadId);
    [DllImport("kernel32.dll")]
    private static extern bool GetThreadContext(IntPtr hThread, ref CONTEXT lpContext);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObj);
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationThread(IntPtr hThread, int cls, IntPtr info, int len);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceCounter(out long lp);
    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceFrequency(out long lp);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddr, UIntPtr dwSize, uint flAlloc, uint flProtect);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddr, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecPage(byte[] state, byte[] hist, byte[] page, byte[] src);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int JitInstall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitSetKey(uint key);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitAddSig(uint crc, ulong key);
    private delegate bool X1D(byte[] rgG, out List<(string, uint, uint, uint)> rgEntries);
  private delegate int JitVerifyHook();
  private delegate void JitSetAdFlag(uint f);
  private delegate void JitSetSlots(uint gmi, uint ci);

    [StructLayout(LayoutKind.Sequential)]
    private struct CONTEXT
    {
        public ulong P1Home, P2Home, P3Home, P4Home, P5Home, P6Home;
        public uint ContextFlags, MxCsr;
        public ushort SegCs, SegDs, SegEs, SegFs, SegGs, SegSs;
        public uint EFlags;
        public ulong Dr0, Dr1, Dr2, Dr3, Dr6, Dr7;
        public ulong Rax, Rcx, Rdx, Rbx, Rsp, Rbp, Rsi, Rdi, R8, R9, R10, R11, R12, R13, R14, R15;
        public ulong Rip;
    }

    //密钥派生 常量A^B参与运算
    private static readonly uint uLK0A = 0x12345678, uLK0B = 0x9328CBBD;//0x811C9DC5
    private static readonly uint uLK1A = 0x11111111, uLK1B = 0x111110A2;//0x01000193
    private static readonly uint uLGAA = 0x0F0F0F0F, uLGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint uLLCA = 0x0A0A0A0A, uLLCB = 0x0A136C07;//0x0019660D
    private static readonly uint uLQCA = 0x5A5A5A5A, uLQCB = 0x6634A905;//0x3C6EF35F
    //K_seed 16B 由KS常量A^B派生
    private static readonly uint uKS0A = 0x1A2B3C4D, uKS0B = 0x9A293C5D;//0x80020010
    private static readonly uint uKS1A = 0x12345678, uKS1B = 0xF9A45750;//0xEB900128
    private static readonly uint uKS2A = 0x5A5A5A5A, uKS2B = 0x584A5A99;//0x021000C3
    private static readonly uint uKS3A = 0x0F0F0F0F, uKS3B = 0x9F278F0E;//0x90288001

    private const int LB_SEED = 64, LB_HASH = 96, LB_HEAD = 100, LB_TBL = 116;
    private static int _iDec = -1, _iJit = -1, _iSig = -1;
    private static uint _skA, _skB, _skC, _skD;
    private static IntPtr _decPtr = IntPtr.Zero;
    private static IntPtr _jitBase = IntPtr.Zero;
    private static int _jitTextSz = 0;
    private static uint _uJitCrc = 0;
    private static uint _uJitTextVa = 0;
    private static JitInstall _fnJitInstall = null!;
    private static JitSetKey _fnJitSetKey = null!;
    private static JitAddSig _fnJitAddSig = null!;
  private static JitVerifyHook _fnJitVerifyHook = null!;
  private static JitSetAdFlag _fnJitSetAdFlag = null!;
  private static JitSetSlots _fnJitSetSlots = null!;

    //常量拆成XOR对 运行时派生
    private static readonly uint uQ1A = 0x12345678, uQ1B = 0xB7F1F3DD;//0xA5A5A5A5
    private static readonly uint uQ2A = 0x1A2B3C4D, uQ2B = 0x40716617;//0x5A5A5A5A
    private static readonly uint uQ3A = 0x12345678, uQ3B = 0x0938F4CD;//0x1B0CA2B5
    private static readonly uint uQ4A = 0x1A2B3C4D, uQ4B = 0x841C45F4;//0x9E3779B9
    private static readonly uint uR1A = 0x12345678, uR1B = 0x133457EB;//0x01000193
    private static readonly uint uR2A = 0x12345678, uR2B = 0x8C032FC1;//0x9E3779B9
    private static readonly uint uHs = 0x13579BDFu;
    //方法体hash委托引用 ldftn token不依赖名字 供元数据重命名
    private static readonly Action _ad = AD;
    private static readonly X1D _x1 = X1;
    private static readonly Func<byte[], (string, uint, uint, uint), bool, byte[]> _x3 = X3;
    private static readonly Func<byte[]> _x6 = X6;
    //AD状态
    private static uint _uAdCt = 0;
    private static long _llT = 0;
    private static uint _uX1H = 0;
    //调试器辅助DLL名单
    private static readonly string[] rgDbg =
    {
        S2(0x432BF782u, new uint[] { 0x41B3F49Au, 0xE2AB725Bu, 0x7CFAE9FCu, 0x1F9267E5u, 0xBF29DD4Eu, 0x5B315B3Fu, 0xFB18D2B8u, 0x96B04B91u }, uR2A, uR2B),
        S2(0x7579CA59u, new uint[] { 0x77E1C919u, 0x10B94782u, 0xB268BFB3u, 0x520036F4u, 0xED77B25Du, 0x8FEF2AF6u }, uR2A, uR2B),
        S2(0x234E5F7Eu, new uint[] { 0x206E5C0Eu, 0xC31DDAB7u, 0x5C755380u, 0xFED4CFC9u, 0x9F4C4662u }, uR2A, uR2B),
        S2(0x2B7E1516u, new uint[] { 0x28BE14A6u, 0xC8158DEFu, 0x64FD0BB0u, 0x07548169u, 0xA79BF8D2u, 0x429375B3u }, uR2A, uR2B),
        S2(0x28AED2A6u, new uint[] { 0x2B16D1EEu, 0xC5964F7Fu, 0x660DC520u, 0x02253CF9u, 0xA24CBAA2u, 0x3FC43343u }, uR2A, uR2B),
        S2(0xABF71588u, new uint[] { 0xA8EF16C8u, 0x49068C49u, 0xEBC60BD2u, 0x85ED818Bu, 0x279CFF1Cu, 0xC024774Du, 0x6283EE1Eu, 0xFECB6B6Fu, 0x9C02E2F0u, 0x3A9A5E21u, 0xD9E1D5EAu, 0x7859507Bu }, uR2A, uR2B),
    };

    //与k+i*delta异或 按UTF-16拼字符串
    private static string S1(uint uK, uint[] rgV)
    {
        char[] rgChars = new char[rgV.Length * 2];
        uint uD = (uK ^ 0x3C6EF372u) + (uint)rgV.Length;
        for (int i = 0; i < rgV.Length; i++)
        {
            uint uT = rgV[i] ^ (uK + (uLGAA ^ uLGAB) * (uint)i);
            rgChars[i * 2] = (char)(uT & 0xFFFF);
            rgChars[i * 2 + 1] = (char)(uT >> 16);
            uD = (uD * 0x01000193u) ^ (uT & 0xFF);
        }
        GC.KeepAlive(uD);
        int iLen = Array.IndexOf(rgChars, '\0');
        if (iLen < 0) iLen = rgChars.Length;
        return new string(rgChars, 0, iLen);
    }

    //K_seed 16B 由KS常量XOR填充
    private static byte[] KSeed()
    {
        byte[] rgKey = new byte[16];
        uint uFake = (uint)Environment.TickCount ^ 0x5A5A5A5Au;
        BitConverter.GetBytes(uKS0A ^ uKS0B ^ (uFake & 0)).CopyTo(rgKey, 0);
        BitConverter.GetBytes(uKS1A ^ uKS1B ^ ((uFake >> 8) & 0)).CopyTo(rgKey, 4);
        BitConverter.GetBytes(uKS2A ^ uKS2B ^ ((uFake >> 16) & 0)).CopyTo(rgKey, 8);
        BitConverter.GetBytes(uKS3A ^ uKS3B ^ ((uFake >> 24) & 0)).CopyTo(rgKey, 12);
        GC.KeepAlive(uFake);
        GC.KeepAlive((uint)(uKS0A ^ uKS1A ^ uKS2A ^ uKS3A));
        return rgKey;
    }

    private static byte[] SeedKey(byte[] rgSeed)
    {
        byte[] rgKey = new byte[16];
        uint uA = uLK0A ^ uLK0B, uB = uQ3A ^ uQ3B, uG = 0x5A5A5A5Au;
        for (int i = 0; i < 16; i++)
        {
            uA ^= rgSeed[i]; uA *= (uR1A ^ uR1B);
            uB ^= rgSeed[i + 16]; uB *= (uR2A ^ uR2B);
            uint uT = (uA ^ (uB << 1)) + (uB ^ (uA >> 3));
            rgKey[i] = (byte)((uT >> 16) ^ (uT >> 24) ^ rgSeed[i]);
            uG = (uG * 0x9E3779B9u) ^ rgKey[i];
        }
        GC.KeepAlive(uG);
        return rgKey;
    }

    private static string S2(uint uK, uint[] rgV, uint uR2A, uint uR2B)
    {
        char[] rgChars = new char[rgV.Length * 2];
        uint uE = (uR2A ^ uR2B) ^ (uint)rgV.Length;
        for (int i = 0; i < rgV.Length; i++)
        {
            uint uT = rgV[i] ^ (uK + (uR2A ^ uR2B) * (uint)i);
            uT = (uT << 13) | (uT >> 19);
            rgChars[i * 2] = (char)(uT & 0xFFFF);
            rgChars[i * 2 + 1] = (char)(uT >> 16);
            uE = (uE * 0x9E3779B9u) ^ (uT & 0x1F);
        }
        GC.KeepAlive(uE);
        int iLen = Array.IndexOf(rgChars, '\0');
        if (iLen < 0) iLen = rgChars.Length;
        return new string(rgChars, 0, iLen);
    }

    //分片密钥重组 用完清零
    private static byte[] Sk()
    {
        byte[] rg = new byte[16];
        BitConverter.GetBytes(_skA).CopyTo(rg, 0);
        BitConverter.GetBytes(_skB).CopyTo(rg, 4);
        BitConverter.GetBytes(_skC).CopyTo(rg, 8);
        BitConverter.GetBytes(_skD).CopyTo(rg, 12);
        return rg;
    }

    private static uint Mx(byte[] rgKey)
    {
        uint uA = rgKey[0] | ((uint)rgKey[1] << 8) | ((uint)rgKey[2] << 16) | ((uint)rgKey[3] << 24);
        uint uB = rgKey[4] | ((uint)rgKey[5] << 8) | ((uint)rgKey[6] << 16) | ((uint)rgKey[7] << 24);
        uint uM = uA ^ uB ^ 0x811C9DC5u;
        GC.KeepAlive((uint)(uA + uB));
        return uM != 0 ? uM : 0x811C9DC5u;
    }

    private static uint K1(byte[] rgSeed)
    {
        uint uH = uLK0A ^ uLK0B;
        uint uG = 0x9E3779B9u;
        foreach (byte byCh in rgSeed)
        {
            uH ^= byCh;
            uH *= uLK1A ^ uLK1B;
            uG = (uG * 0x01000193u) + byCh;
        }
        GC.KeepAlive(uG);
        return uH;
    }

    //perm 由k派生的4元素置换表
    private static int[] K2(uint uK)
    {
        int[] rgPerm = { 0, 1, 2, 3 };
        uint uS = uK, uS2 = uK ^ 0x5A5A5A5Au;
        for (int i = 3; i > 0; i--)
        {
            uS = uS * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            uS2 = (uS2 * 0x01000193u) + (uS >> 8);
            int j = (int)(uS % (uint)(i + 1));
            (rgPerm[i], rgPerm[j]) = (rgPerm[j], rgPerm[i]);
        }
        GC.KeepAlive(uS2);
        return rgPerm;
    }

    private static uint K3(uint uK, int i) => uK + (uLGAA ^ uLGAB) * (uint)i;

    private static uint Q(byte[] rgB, int iOff) => BitConverter.ToUInt32(rgB, iOff);

    [STAThread]
    private static int Main(string[] rgArgs) => B(rgArgs);

    private static int B(string[] rgArgs)
    {
        try
        {
            if (uLK0A == 0xDEADBEEFu) Environment.FailFast(null);
            H1(rgArgs);
            byte[] rgG;
            List<(string, uint, uint, uint)> rgEntries;

            AD();
            _llT = T0();
            var th = new Thread(() =>
            {
                try { IntPtr hTh = OpenThread(0x0040u, false, GetCurrentThreadId()); if (hTh != IntPtr.Zero) { NtSetInformationThread(hTh, 0x11, IntPtr.Zero, 0); CloseHandle(hTh); } } catch { }
        for (; ; ) { try { AD(); TCheck(_llT, 10000L); _llT = T0(); if ((_uAdCt & 0x3Fu) == 0) JitVerify(); if ((_uAdCt & 0x3Fu) == 0 && _fnJitVerifyHook != null && _fnJitVerifyHook() != 0) Environment.FailFast(null); } catch { } Thread.Sleep(120); }
            }) { IsBackground = true };
            th.Start();
            SelfCheck();

            if ((RuntimeSeed() & 0x20000000u) != 0)
            {
                //真分叉 路径A带诱饵计算
                uint uQ1 = (uint)Environment.TickCount ^ (uQ1A ^ uQ1B);
                byte[] rgGA = X6();
                GC.KeepAlive(uQ1);
                rgG = rgGA;
            }
            else
            {
                //真分叉 路径B不同诱饵
                uint uQ2 = RuntimeSeed() ^ (uQ2A ^ uQ2B);
                rgG = X6();
                GC.KeepAlive(uQ2);
            }
            if (rgG.Length < 128)
                goto QF;
            H2(rgG);
            GC.KeepAlive(X9(rgG));
            AD();
            long llParse = T0();
            if (!X1(rgG, out rgEntries))
                goto QF;
            X0(rgG);
            EnsureDecoder(rgG, rgEntries);
            EnsureJitHook(rgG, rgEntries);
            InjectSigs(rgG, rgEntries);
            if (_fnJitInstall != null)
            {
                byte[] rgJk = Sk();
                _fnJitSetKey(K1(rgJk));
                Array.Clear(rgJk, 0, rgJk.Length);
                if (_fnJitSetAdFlag != null) _fnJitSetAdFlag(0x13579BDFu);
                ApplyJitSlots();
                _fnJitInstall();
            }
            if ((RuntimeSeed() & 0x10000000u) == 0)
            {
                uint uQ3 = (uint)Process.GetCurrentProcess().Id ^ (uQ3A ^ uQ3B);
                W1(rgG, rgEntries);
                GC.KeepAlive(uQ3);
            }
            else
            {
                uint uQ4 = (uint)Environment.TickCount ^ (uQ4A ^ uQ4B);
                W1(rgG, rgEntries);
                GC.KeepAlive(uQ4);
            }
            byte[] rgMain = X3(rgG, rgEntries[0], true);
            TCheck(llParse, 8000L);
            Assembly asm = X4(AssemblyLoadContext.Default, rgMain);
            Array.Clear(rgMain, 0, rgMain.Length);

            MethodInfo? miEntry = asm.EntryPoint;
            if (miEntry == null) goto QF;
            object[] rgParams = miEntry.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { rgArgs };
            return miEntry.Invoke(null, rgParams) is int r ? r : 0;
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
        if (uLQCA == 0xFEEDFACEu) Environment.FailFast(null);
        uint uCt = ++_uAdCt;
        bool fDbg = IsDebuggerPresent();
        if (!fDbg && HwBp())
            fDbg = true;
        if (!fDbg && (uCt & 0x0Fu) == 0 && DllScan())
            fDbg = true;
        if (!fDbg && (uCt & 0x0Fu) == 0 && WndScan())
            fDbg = true;
        if (!fDbg)
        {
            IntPtr pPort = IntPtr.Zero;
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 7, out pPort, IntPtr.Size, out _) == 0 && pPort != IntPtr.Zero)
                fDbg = true;
        }
        if (!fDbg && PebDbg())
            fDbg = true;
        if (!fDbg && PfDetect())
            fDbg = true;
        if (!fDbg && (uCt & 0x07u) == 0 && (Environment.TickCount & 1) == 0)
            GC.KeepAlive((uint)Environment.TickCount ^ uCt);
        if (!fDbg)
        {
            bool fCr = false;
            if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref fCr) && fCr)
                fDbg = true;
        }
        if (!fDbg && (uCt & 0x3Fu) == 0 && _uX1H != 0 && MethodHash(_x1) != _uX1H)
            fDbg = true;
        if (fDbg)
            Environment.FailFast(null);
    }

    private static bool HwBp()
    {
        try
        {
            IntPtr hTh = OpenThread(0x0048u, false, GetCurrentThreadId());
            if (hTh == IntPtr.Zero) return false;
            try
            {
                var ctx = new CONTEXT();
                ctx.ContextFlags = 0x00100010u;
                if (!GetThreadContext(hTh, ref ctx)) return false;
                if ((ctx.Dr7 & 0xFFFFFF00) != 0) GC.KeepAlive(ctx.Dr6);
                if (ctx.Dr0 != 0 || ctx.Dr1 != 0 || ctx.Dr2 != 0 || ctx.Dr3 != 0 || (ctx.Dr7 & 0xFF) != 0)
                    return true;
            }
            finally { CloseHandle(hTh); }
        }
        catch { }
        return false;
    }

    private static bool PebDbg()
    {
        try
        {
            byte[] rgPbi = new byte[16];
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, rgPbi, rgPbi.Length, out _) != 0) return false;
            long lPeb = BitConverter.ToInt64(rgPbi, 8);
            if (lPeb == 0) return false;
            GC.KeepAlive(lPeb ^ BitConverter.ToInt64(rgPbi, 0));
            return Marshal.ReadByte(new IntPtr(lPeb), 2) != 0;
        }
        catch { }
        return false;
    }

    private static bool PfDetect()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("CORECLR_ENABLE_PROFILING") == "1") return true;
            if (Environment.GetEnvironmentVariable("CORECLR_PROFILER") != null) return true;
            if (Environment.GetEnvironmentVariable("CORECLR_PROFILER_PATH") != null) return true;
            if (Environment.GetEnvironmentVariable("COR_ENABLE_PROFILING") == "1") return true;
            if (Environment.GetEnvironmentVariable("COR_PROFILER") != null) return true;
        }
        catch { }
        return false;
    }

    //纯噪音 枚举窗口但永不FailFast
    private static bool WndScan()
    {
        try
        {
            bool fHit = false;
            EnumWindows((h, l) =>
            {
                int iLen = GetWindowTextLengthW(h);
                if (iLen > 0 && (iLen & 1) == 0)
                    fHit = true;
                return true;
            }, IntPtr.Zero);
            GC.KeepAlive(fHit);
        }
        catch { }
        return false;
    }
    private static bool DllScan()
    {
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                string s = m.ModuleName;
                if (s.Length == 0) GC.KeepAlive(s);
                for (int i = 0; i < rgDbg.Length; i++)
                {
                    if (string.Equals(s, rgDbg[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static long T0() { QueryPerformanceCounter(out long llT); return llT; }
    private static void TCheck(long llStart, long llMsLimit)
    {
        QueryPerformanceCounter(out long llEnd);
        QueryPerformanceFrequency(out long fFreq);
        if (fFreq > 0 && (llEnd - llStart) * 1000 / fFreq > llMsLimit)
            Environment.FailFast(null);
    }
    private static void SelfCheck()
    {
        //防patch密钥派生常量
        if ((uKS0A ^ uKS0B) != 0x80020010u) Environment.FailFast(null);
        if ((uKS1A ^ uKS1B) != 0xEB900128u) Environment.FailFast(null);
        if ((uKS2A ^ uKS2B) != 0x021000C3u) Environment.FailFast(null);
        if ((uKS3A ^ uKS3B) != 0x90288001u) Environment.FailFast(null);
        if ((uLK0A ^ uLK0B) != 0x811C9DC5u) Environment.FailFast(null);
        if ((uLK1A ^ uLK1B) != 0x000001B3u) Environment.FailFast(null);
        //防patch反调试/解密逻辑
        if (MethodHash(_ad) != (uHs ^ 0x42EEE08Bu)) Environment.FailFast(null);
        if (MethodHash(_x1) != (uHs ^ 0x75057167u)) Environment.FailFast(null);
        if (MethodHash(_x3) != (uHs ^ 0x63D62480u)) Environment.FailFast(null);
        if (MethodHash(_x6) != (uHs ^ 0x35215CF5u)) Environment.FailFast(null);
        _uX1H = MethodHash(_x1);
    }

    private static uint MethodHash(Delegate d)
    {
        byte[] il = d.Method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        uint uH = uLK0A ^ uLK0B;
        uint uC = (uint)il.Length ^ 0x9E3779B9u;
        foreach (byte byX in il) { uH ^= byX; uH *= uLK1A ^ uLK1B; uC = (uC * 0x01000193u) ^ byX; }
        GC.KeepAlive(uC);
        return uH;
    }

    //解析--参数 假的
    private static void H1(string[] rgArgs)
    {
        var rgMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rgArgs.Length && i < 8; i++)
        {
            string sArg = rgArgs[i];
            if (sArg.Length >= 2 && sArg[0] == '-' && sArg[1] == '-')
                rgMap[sArg] = sArg;
        }
        GC.KeepAlive(rgMap);
    }

    //计算g前0x100字节hash 也是诱饵
    private static void H2(byte[] rgG)
    {
        uint uH = uLK0A ^ uLK0B;
        int iN = Math.Min(rgG.Length, 0x100);
        for (int i = 0; i < iN; i++)
            uH = (uH ^ rgG[i]) * (uLK1A ^ uLK1B);
        GC.KeepAlive(uH);
    }

    //反正看起来像核心解密什么的
    private static uint X9(byte[] rgG)
    {
        uint uH = 0x811C9DC5u;
        int iN = Math.Min(rgG.Length, 0x40);
        for (int i = 0; i < iN; i++)
            uH = (uH ^ rgG[i]) * 0x01000193u;
        GC.KeepAlive(uH);
        return uH;
    }

    //写元数据 并注册AssemblyLoadContext.Resolving
    private static void W1(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uKS2A == 0x0BADF00Du) return;
        byte[] rgMeta = Encoding.Unicode.GetBytes(S1(0xCF7DA6AEu, new uint[] { 0xCF12A6CDu, 0x6DD02015u, 0x0B809A43u, 0xAA0513ABu, 0x48358DC7u, 0xE6FD0720u, 0x84BD816Bu, 0x234CFAD3u, 0xC15D7419u, 0x5F1CEE5Au, 0xFD86678Du, 0x9BB3E1C5u, 0x3A175B36u }));
        byte[] rgVer = Encoding.ASCII.GetBytes(S1(0x3EBB0388u, new uint[] { 0x3E9503B1u, 0xDCDC7D71u, 0x7B04F6CAu, 0x191370C3u, 0xB7EEEA09u, 0x55B5644Cu, 0xF429DDA9u, 0x921157A6u, 0x3042D162u, 0xCE964B39u, 0x6CCBC4F2u, 0x0B1D3E42u }));
        int iEnd = 0;
        foreach (var entry in rgEntries)
            iEnd = Math.Max(iEnd, (int)(entry.Item4 + entry.Item3));
        if (iEnd + 160 <= rgG.Length)
        {
            Array.Copy(rgMeta, 0, rgG, iEnd, Math.Min(rgMeta.Length, rgG.Length - iEnd));
            Array.Copy(rgVer, 0, rgG, iEnd + 128, Math.Min(rgVer.Length, rgG.Length - iEnd - 128));
        }

        var rgDeps = new Dictionary<string, (string, uint, uint, uint)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < rgEntries.Count; i++)
            rgDeps[X2(rgEntries[i].Item1)] = rgEntries[i];
        AssemblyLoadContext.Default.Resolving += (ctx, name) => X5(ctx, name, rgDeps, rgG);
    }

    //伪造MethodDesc 供CoreCLR调用
    private static void X0(byte[] rgG)
    {
        try
        {
            if (rgG.Length < 64) return;
            GC.KeepAlive((uint)rgG.Length);
            BitConverter.GetBytes(0x1000u).CopyTo(rgG, 0);
            BitConverter.GetBytes((ushort)0x0002).CopyTo(rgG, 8);
            rgG[0x10] = 1; rgG[0x11] = 0; rgG[0x12] = 2; rgG[0x13] = 0x80;
            BitConverter.GetBytes(0u).CopyTo(rgG, 0x18);
            BitConverter.GetBytes(0x28u).CopyTo(rgG, 0x20);
            byte[] rgCode = { 0x90, 0x90, 0x90, 0x90, 0xEB, 0x00, 0xC3 };
            Array.Copy(rgCode, 0, rgG, 0x28, rgCode.Length);
        }
        catch { }
    }

    private static int F()
    {
        if (uLLCA == 0xDEADBEEFu) return 7;
        Span<long> rgStack = stackalloc long[16];
        for (int i = 0; i < rgStack.Length; i++)
            rgStack[i] = unchecked((long)0xC300EB9090909090);
        GC.KeepAlive(rgStack.Length);
        return (int)(RuntimeSeed() & 0xF) + 1;
    }

    //解析bundle条目表 用seed派生key解密
    private static bool X1(byte[] rgG, out List<(string, uint, uint, uint)> rgEntries)
    {
        rgEntries = new List<(string, uint, uint, uint)>();
        if (uKS0A == 0xCAFEBABEu) return false;
        byte[] rgSeed = Array.Empty<byte>(), rgKseed = Array.Empty<byte>();
        uint uK = 0, uOff1 = 0, uOff2 = 0, uNameTotal = 0, uKk = 0;
        int iCount = 0, iDecoys = 0, iTotal = 0, i = 0, j = 0, o = 0;
        long llTEnd = 0, llNa = 0, llDs = 0, llNo = 0, llDoff = 0;
        string sName = "";
        int[] rgPerm = Array.Empty<int>();
        uint[] rgRow = Array.Empty<uint>();
        uint[] rgF = new uint[4];
        int iSt = 0;
        while (true)
        {
            switch (iSt)
            {
                case 0:
                    if (rgG.Length < 128) return false;
                    iSt = 1;
                    continue;
                case 1:
                    rgSeed = new byte[32];
                    rgKseed = KSeed();
                    uint uDm = 0x9E3779B9u ^ (uint)rgKseed[3];
                    for (i = 0; i < 32; i++)
                    {
                        rgSeed[i] = (byte)(rgG[LB_SEED + i] ^ rgKseed[i % 16]);
                        uDm = (uDm * 0x01000193u) ^ ((uint)rgSeed[i] << (i & 7));
                    }
                    uK = K1(rgSeed);
                    byte[] rgSk = SeedKey(rgSeed);
                    _skA = BitConverter.ToUInt32(rgSk, 0);
                    _skB = BitConverter.ToUInt32(rgSk, 4);
                    _skC = BitConverter.ToUInt32(rgSk, 8);
                    _skD = BitConverter.ToUInt32(rgSk, 12);
                    Array.Clear(rgSk, 0, rgSk.Length);





                    GC.KeepAlive(uDm);
                    Array.Clear(rgSeed, 0, rgSeed.Length);
                    Array.Clear(rgKseed, 0, rgKseed.Length);
                    iSt = 2;
                    continue;
                case 2:
                    if (rgG.Length == 0x7FFFFFFF) return false;
                    uint uD2 = uK ^ 0x3C6EF372u;
                    iCount = (int)(Q(rgG, LB_HEAD) ^ uK);
                    uOff1 = Q(rgG, LB_HEAD + 4) ^ uK;
                    uD2 = (uD2 * 0x9E3779B9u) + uOff1;
                    uOff2 = Q(rgG, LB_HEAD + 8) ^ uK;
                    iDecoys = (int)(Q(rgG, LB_HEAD + 12) ^ K3(uK, iCount));
                    GC.KeepAlive(uD2);
                    if (iCount <= 0 || iCount > 0x1000 || uOff1 == 0 || uOff1 > 0x10000000 || uOff2 == 0 || iDecoys < 0 || iDecoys > 0x100)
                        return false;
                    iTotal = iCount + iDecoys;
                    llTEnd = LB_TBL + (long)iTotal * 20;
                    if (llTEnd > rgG.Length) return false;
                    rgPerm = K2(uK);
                    rgRow = new uint[iTotal * 5];
                    uNameTotal = 0;
                    i = 0;
                    iSt = 3;
                    continue;
                case 3:
                    if (i >= iTotal) { iSt = 4; continue; }
                    uKk = K3(uK, i);
                    int iO5 = (i << 2) + i;
                    o = LB_TBL + (i << 4) + (i << 2);
                    for (int s = 0; s < 4; s++)
                        rgF[rgPerm[s]] = Q(rgG, o + (s << 2)) ^ uKk;
                    rgRow[iO5 + 0] = rgF[0];
                    rgRow[iO5 + 1] = rgF[1];
                    rgRow[iO5 + 2] = rgF[2];
                    rgRow[iO5 + 3] = rgF[3];
                    uNameTotal += rgF[0] + (uint)(iO5 & 0);
                    i++;
                    iSt = 3;
                    continue;
                case 4:
                    llNa = (uNameTotal + 3u) & ~3u;
                    llDs = llTEnd + llNa;
                    if (llDs > rgG.Length) return false;
                    i = 0;
                    iSt = 5;
                    continue;
                case 5:
                    if (i >= iCount) { iSt = 6; continue; }
                    int iR5 = (i << 2) + i;
                    if (rgRow[iR5 + 1] == 0) return false;
                    llNo = llTEnd;
                    for (j = 0; j < i; j++) llNo += rgRow[(j << 2) + j];
                    if (llNo + rgRow[iR5] > rgG.Length) return false;
                    sName = Encoding.UTF8.GetString(rgG, (int)llNo, (int)rgRow[iR5]);
                    llDoff = llDs + rgRow[iR5 + 3];
                    if (rgRow[iR5 + 2] > 0 && llDoff + rgRow[iR5 + 2] > rgG.Length) return false;
                    rgEntries.Add((sName, rgRow[iR5 + 1], rgRow[iR5 + 2], (uint)llDoff));
                    i++;
                    iSt = 5;
                    continue;
                case 6:
                    _iJit = iCount - 3;
                    _iSig = iCount - 2;
                    _iDec = -1;
                    return true;
            }
        }
    }

    private static string X2(string sName)
        => sName.EndsWith(S1(0x28CF58B4u, new uint[] { 0x28AB589Au, 0xC76AD201u }), StringComparison.OrdinalIgnoreCase)
            ? sName.Substring(0, sName.Length - 4) : sName;

    //从主程序压缩流收集解码器段重组Iamdec 以LoadBare加载
    private static void EnsureDecoder(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uKS3A == 0x8BADF00Du) return;
        if (_decPtr != IntPtr.Zero) return;
        if (rgEntries.Count == 0) return;
        //主程序条目 8B头+段表+压缩流(解码段混入)
        var entry = rgEntries[0];
        uint uCompLen = entry.Item3, uOff = entry.Item4;
        if (uCompLen == 0 || uCompLen > 0x800000) return;
        byte[] rgB = new byte[uCompLen];
        Array.Copy(rgG, (int)uOff, rgB, 0, (int)uCompLen);
        int nSeg = BitConverter.ToInt32(rgB, 0);
        int iSeg = BitConverter.ToInt32(rgB, 4);
        int iTable = 8 + nSeg * 8;
        if (nSeg <= 0 || nSeg > 0x1000 || iTable > rgB.Length) { Array.Clear(rgB, 0, rgB.Length); return; }
        //按段表收集解码段
        int n = 0;
        for (int j = 0; j < nSeg; j++) n += BitConverter.ToInt32(rgB, 12 + j * 8);
        byte[] rgDec = new byte[n];
        bool ok = true;
        for (int j = 0; j < nSeg && ok; j++)
        {
            int off = BitConverter.ToInt32(rgB, 8 + j * 8);
            int len = BitConverter.ToInt32(rgB, 12 + j * 8);
            if (off < 0 || len <= 0 || iTable + off + len > rgB.Length) { ok = false; break; }
            Array.Copy(rgB, iTable + off, rgDec, j * iSeg, len);
        }
        Array.Clear(rgB, 0, rgB.Length);
        if (!ok) { Array.Clear(rgDec, 0, rgDec.Length); return; }
        _decPtr = LoadBare(rgDec, true);
        Array.Clear(rgDec, 0, rgDec.Length);
    }

    //手工映射裸PE返回基址 .text/.rdata按RVA排布
    private static IntPtr LoadBare(byte[] rgDll, bool fRx)
    {
        if (uLGAA == 0xFEEDFACEu) return IntPtr.Zero;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        uint uTextVa = 0; int iTextRaw = 0, iTextSz = 0;
        uint uDataVa = 0; int iDataRaw = 0, iDataSz = 0;//.rdata
        uint uData2Va = 0; int iData2Raw = 0, iData2Sz = 0;//.data
        string sTxt = S2(0x15083CC2u, new uint[] { 0x14783F62u, 0xB017B5BBu, 0x52D73034u }, uR2A, uR2B);
        string sRda = S2(0x44F79C99u, new uint[] { 0x45879F09u, 0xE00F155Au, 0x82C69303u, 0x1F9E09C4u }, uR2A, uR2B);
        string sDat = S2(0x432BF782u, new uint[] { 0x425BF4A2u, 0xE26B729Bu, 0x7C92EAF4u }, uR2A, uR2B);
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            string sName = Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0');
            uint uVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uRsz = BitConverter.ToUInt32(rgDll, o + 16);
            if (sName.Length >= 4 && sName[0] == '.' && uRsz == 0)
                GC.KeepAlive(uVa);
            if (sName == sTxt) { uTextVa = uVa; iTextRaw = (int)uRaw; iTextSz = (int)uRsz; }
            else if (sName == sRda) { uDataVa = uVa; iDataRaw = (int)uRaw; iDataSz = (int)uRsz; }
            else if (sName == sDat) { uData2Va = uVa; iData2Raw = (int)uRaw; iData2Sz = (int)uRsz; }
        }
        if (iTextSz == 0)
            throw new InvalidDataException();
        int cbMap = iTextSz + 0x1000 + (iDataSz > 0 ? (int)(uDataVa - uTextVa) + iDataSz : 0) + (iData2Sz > 0 ? (int)(uData2Va - uTextVa) + iData2Sz : 0) + 0x1000;
        IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)cbMap, 0x3000, fRx ? 0x04u : 0x40u);
        if (p == IntPtr.Zero)
            throw new InvalidDataException();
        Marshal.Copy(rgDll, iTextRaw, p, iTextSz);
        if (iDataSz > 0)
            Marshal.Copy(rgDll, iDataRaw, new IntPtr(p.ToInt64() + (uDataVa - uTextVa)), iDataSz);
        if (iData2Sz > 0)
            Marshal.Copy(rgDll, iData2Raw, new IntPtr(p.ToInt64() + (uData2Va - uTextVa)), iData2Sz);
        if (fRx)
            VirtualProtect(p, (UIntPtr)iTextSz, 0x20, out _);//.text置RX
        return p;
    }

    //解析PE导出表
    private static Dictionary<string, uint> ParseExports(byte[] rgDll)
    {
        var rgExports = new Dictionary<string, uint>(StringComparer.Ordinal);
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        ushort usMagic = BitConverter.ToUInt16(rgDll, iPe + 24);
        int iDataDir = iPe + 24 + (usMagic == 0x20B ? 112 : 96);
        uint uExpRva = BitConverter.ToUInt32(rgDll, iDataDir);
        if (uExpRva == 0) return rgExports;
        int iExpOff = RvaToOff(rgDll, iPe, usOpt, uExpRva);
        if (iExpOff <= 0) return rgExports;
        int iNameCount = BitConverter.ToInt32(rgDll, iExpOff + 0x18);
        int iFuncCount = BitConverter.ToInt32(rgDll, iExpOff + 0x1C);
        int iNameOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x20));
        int iOrdOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x24));
        int iFuncOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x1C));
        if (iNameOff <= 0 || iOrdOff <= 0 || iFuncOff <= 0) return rgExports;
        for (int i = 0; i < iNameCount; i++)
        {
            int iNameRva = BitConverter.ToInt32(rgDll, iNameOff + (i << 2));
            int iNameOff2 = RvaToOff(rgDll, iPe, usOpt, (uint)iNameRva);
            if (iNameOff2 <= 0) continue;
            uint uDp = (uint)(iNameRva ^ i ^ 0x9E3779B9u);
            int iEnd = iNameOff2;
            while (iEnd < rgDll.Length && rgDll[iEnd] != 0) iEnd++;
            string sName = Encoding.ASCII.GetString(rgDll, iNameOff2, iEnd - iNameOff2);
            int usOrd = BitConverter.ToUInt16(rgDll, iOrdOff + (i << 1));
            if (usOrd >= iFuncCount) { GC.KeepAlive(uDp); continue; }
            uint uFnRva = BitConverter.ToUInt32(rgDll, iFuncOff + usOrd * 4);
            rgExports[sName] = uFnRva;
            GC.KeepAlive(uDp ^ uFnRva);
        }
        return rgExports;
    }

    private static int RvaToOff(byte[] rgDll, int iPe, ushort usOpt, uint uRva)
    {
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            uint uVs = BitConverter.ToUInt32(rgDll, o + 8);
            uint uVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRs = BitConverter.ToUInt32(rgDll, o + 16);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uEnd = Math.Max(uVs, uRs);
            if (uRva >= uVa && uRva < uVa + uEnd) return (int)(uRaw + (uRva - uVa));
        }
        return -1;
    }

    private static int TextSz(byte[] rgDll)
    {
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') == ".text")
                return BitConverter.ToInt32(rgDll, o + 16);
        }
        return 0;
    }
    private static uint PeTextVa(byte[] rgDll)
    {
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') == S2(0x15083CC2u, new uint[] { 0x14783F62u, 0xB017B5BBu, 0x52D73034u }, uR2A, uR2B))
                return BitConverter.ToUInt32(rgDll, o + 12);
        }
        return 0x1000;
    }

    private static T Mk<T>(uint uRva) where T : Delegate
    {
        var pAddr = new IntPtr(_jitBase.ToInt64() + (uRva - _uJitTextVa));
        return Marshal.GetDelegateForFunctionPointer<T>(pAddr);
    }

    private static void EnsureJitHook(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uLK1A == 0xDEADBEEFu) return;
        if (_iJit >= 0 && _iJit < rgEntries.Count)
        {
            var entry = rgEntries[_iJit];
            byte[] rgDll = X3(rgG, entry, false);
            uint uDh = (uint)rgDll.Length ^ 0x5A5A5A5Au;
            _jitBase = LoadBare(rgDll, true);//.text只读 .data可写
            _uJitTextVa = PeTextVa(rgDll);
            _jitTextSz = TextSz(rgDll);
            byte[] rgC = new byte[_jitTextSz];
            Marshal.Copy(_jitBase, rgC, 0, _jitTextSz);
            uint uC = uLK0A ^ uLK0B;
            foreach (byte b in rgC) { uC ^= b; uC *= uLK1A ^ uLK1B; }
            _uJitCrc = uC;
            var rgExports = ParseExports(rgDll);
            GC.KeepAlive(uDh);
            _fnJitInstall = Mk<JitInstall>(rgExports[S2(0x7579CA59u, new uint[] { 0x7731C929u, 0x102947B2u, 0xB2E0BEABu, 0x534035D4u, 0xED1FB29Du, 0x8ECF298Eu, 0x29BEA7F7u, 0xC8FE1E68u }, uR2A, uR2B)]);
            _fnJitSetKey = Mk<JitSetKey>(rgExports[S2(0x234E5F7Eu, new uint[] { 0x21D65C56u, 0xC225DB67u, 0x5CF55150u, 0xFFB4CFD1u, 0x9F54453Au, 0x383BC333u, 0xDB5339D4u }, uR2A, uR2B)]);
            _fnJitAddSig = Mk<JitAddSig>(rgExports[S2(0x29CE007Au, new uint[] { 0x2BC6035Au, 0xCB2578B3u, 0x6534F024u, 0x07146EDDu, 0xA1A3E47Eu, 0x427B625Fu, 0xDC22DAD0u }, uR2A, uR2B)]);
              if (rgExports.TryGetValue("VerifyJitHook", out uint uVj)) _fnJitVerifyHook = Mk<JitVerifyHook>(uVj);
              if (rgExports.TryGetValue("SetAntiDebugFlag", out uint uAd)) _fnJitSetAdFlag = Mk<JitSetAdFlag>(uAd);
              if (rgExports.TryGetValue("SetJitSlots", out uint uSj)) _fnJitSetSlots = Mk<JitSetSlots>(uSj);
            Array.Clear(rgDll, 0, rgDll.Length);
        }
    }

    //net5=gmi3/ci4 net6-7=4/5 net8=4/6 net9-10=5/8
    private static void ApplyJitSlots()
    {
        if (_fnJitSetSlots == null) return;
        int v = Environment.Version.Major;
        if (v == 5) { _fnJitSetSlots(0x18, 0x20); return; }
        if (v == 6 || v == 7) { _fnJitSetSlots(0x20, 0x28); return; }
        if (v == 9 || v == 10) { _fnJitSetSlots(0x28, 0x40); return; }
    }

    //校验jithook .text 防运行时patch
    private static void JitVerify()
    {
        if (_jitBase == IntPtr.Zero || _jitTextSz <= 0 || _uJitCrc == 0) return;
        try
        {
            byte[] rg = new byte[_jitTextSz];
            Marshal.Copy(_jitBase, rg, 0, _jitTextSz);
            uint uH = uLK0A ^ uLK0B;
            foreach (byte b in rg) { uH ^= b; uH *= uLK1A ^ uLK1B; }
            if (uH != _uJitCrc) Environment.FailFast(null);
        }
        catch { }
    }
    private static void InjectSigs(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uLK1B == 0xCAFEBABEu) return;
        if (_iSig >= 0 && _iSig < rgEntries.Count)
        {
            var entry = rgEntries[_iSig];
            byte[] rgSig = X3(rgG, entry, false);
            uint uDs = (uint)rgSig.Length;
            for (int i = 0; i + 16 <= rgSig.Length; i += 16)
            {
                uint uLo = BitConverter.ToUInt32(rgSig, i);      //低32=crc2^mask32
                ulong uHi = BitConverter.ToUInt64(rgSig, i + 8); //uKey2^mask64
                _fnJitAddSig(uLo, uHi);
                uDs = (uDs * 0x9E3779B9u) ^ uLo;
            }
            GC.KeepAlive(uDs);
            Array.Clear(rgSig, 0, rgSig.Length);
        }
    }

    //解压条目 压缩按4KB页调Iamdec 非压缩直接XOR
    private static byte[] X3(byte[] rgG, (string, uint, uint, uint) entry, bool bMain)
    {
        if (uKS1A == 0xF00DFACEu) return rgG;
        byte[] rgRaw = Array.Empty<byte>(), rgComp = Array.Empty<byte>(), rgOut = Array.Empty<byte>(), rgHist = Array.Empty<byte>(), rgPage = Array.Empty<byte>(), rgState = Array.Empty<byte>();
        DecPage fnDec = null!;
        uint uRawLen = 0, uPg = 0, uPs = 0, uPe = 0;
        int iDoff = 0, iCompLen = 0, iRc = 0, iC = 0;
        int iSt = 0;
        while (true)
        {
            switch (iSt)
            {
                case 0:
                    iDoff = (int)entry.Item4;
                    uRawLen = entry.Item2;
                    iCompLen = (int)entry.Item3;
                    if (uRawLen == 0x7FFFFFFFu) { iSt = 4; continue; }
                    iSt = (iCompLen == (int)uRawLen) ? 1 : 2;
                    continue;
                case 4:
                    byte[] rgPlain = new byte[iCompLen];
                    Array.Copy(rgG, iDoff, rgPlain, 0, iCompLen);
                    return rgPlain;
                case 1:
                    if (uRawLen == 0x7FFFFFFF) return rgG;
                    rgRaw = new byte[uRawLen];
                    byte[] rgK = Sk();
                    uint uAdj = Mx(rgK);
                    uint uDy = uAdj ^ 0x5A5A5A5Au;
                    for (int i = 0; i < rgRaw.Length; i++)
                    {
                        rgRaw[i] = (byte)(rgG[iDoff + i] ^ rgK[i & 15] ^ (byte)(uAdj >> (8 * (i & 3))));
                        uDy = (uDy * 0x9E3779B9u) ^ (uint)rgRaw[i];
                    }
                    GC.KeepAlive(uDy);
                    Array.Clear(rgK, 0, rgK.Length);
                    return rgRaw;
                case 2:
                    if (_decPtr == IntPtr.Zero)
                        throw new InvalidDataException();
                    fnDec = Marshal.GetDelegateForFunctionPointer<DecPage>(_decPtr);
                    rgComp = new byte[iCompLen];
                    Array.Copy(rgG, iDoff, rgComp, 0, iCompLen);
                    if (bMain)
                    {
                        //主程序条目 抽走解码段还原为纯压缩流
                        int nSeg = BitConverter.ToInt32(rgComp, 0);
                        int iSeg2 = BitConverter.ToInt32(rgComp, 4);
                        int iTable = 8 + nSeg * 8;
                        if (nSeg > 0 && nSeg <= 0x1000 && iTable <= rgComp.Length)
                        {
                            int nSegLen = 0;
                            for (int j = 0; j < nSeg; j++) nSegLen += BitConverter.ToInt32(rgComp, 12 + j * 8);
                            int L0 = rgComp.Length - iTable - nSegLen;
                            if (L0 > 0)
                            {
                                byte[] rgComp0 = new byte[L0];
                                int dst = L0, cur = rgComp.Length;
                                for (int k = nSeg - 1; k >= 0; k--)
                                {
                                    int off = BitConverter.ToInt32(rgComp, 8 + k * 8);
                                    int len = BitConverter.ToInt32(rgComp, 12 + k * 8);
                                    int iSegStart = iTable + off;
                                    int dataLen = cur - (iSegStart + len);
                                    dst -= dataLen;
                                    Array.Copy(rgComp, iSegStart + len, rgComp0, dst, dataLen);
                                    cur = iSegStart;
                                }
                                Array.Copy(rgComp, iTable, rgComp0, 0, cur - iTable);
                                Array.Clear(rgComp, 0, rgComp.Length);
                                rgComp = rgComp0;
                                iCompLen = L0;
                            }
                        }
                    }
                    rgOut = new byte[uRawLen];
                    rgHist = new byte[0x80000];
                    rgPage = new byte[0x1000];
                    rgState = new byte[0x38];
                    BitConverter.GetBytes(1u).CopyTo(rgState, 0x00);
                    BitConverter.GetBytes(1u).CopyTo(rgState, 0x04);
                    BitConverter.GetBytes(1u).CopyTo(rgState, 0x08);
                    BitConverter.GetBytes((uint)iCompLen).CopyTo(rgState, 0x28);
                    BitConverter.GetBytes(uRawLen).CopyTo(rgState, 0x34);
                    rgHist[0] = rgComp[0];
                    uPg = 0;
                    iSt = 3;
                    continue;
                case 3:
                    if (uPg << 12 >= uRawLen) { iSt = 5; continue; }
                    uPs = uPg << 12;
                    uPe = Math.Min(uPs + 0x1000, uRawLen);
                    if (uPg == 0) rgPage[0] = rgComp[0];
                    else Array.Clear(rgPage, 0, 0x1000);
                    BitConverter.GetBytes(uPs).CopyTo(rgState, 0x2C);
                    BitConverter.GetBytes(uPe).CopyTo(rgState, 0x30);
                    uint uD3 = (uPg ^ 0x0F0F0F0Fu) * 0x9E3779B9u;
                    iRc = fnDec(rgState, rgHist, rgPage, rgComp);
                    GC.KeepAlive(uD3);
                    if (iRc != 0)
                        throw new InvalidDataException();
                    iC = (int)(uPe - uPs);
                    Array.Copy(rgPage, 0, rgOut, (int)uPs, iC);
                    uPg++;
                    iSt = 3;
                    continue;
                case 5:
                    Array.Clear(rgComp, 0, rgComp.Length);
                    Array.Clear(rgState, 0, rgState.Length);
                    Array.Clear(rgHist, 0, rgHist.Length);
                    return rgOut;
            }
        }
    }

    private static Assembly X4(AssemblyLoadContext alc, byte[] rgB)
    {
        using var stream = new MemoryStream(rgB, writable: true);
        var asm = alc.LoadFromStream(stream);
        if (stream.TryGetBuffer(out var buf))
            Array.Clear(buf.Array!, buf.Offset, buf.Count);
        return asm;
    }

    private static Assembly? X5(AssemblyLoadContext alc, AssemblyName asmName,
                                Dictionary<string, (string, uint, uint, uint)> rgDeps, byte[] rgG)
    {
        if (asmName.Name == null || !rgDeps.TryGetValue(asmName.Name, out var entry))
            return null;
        byte[] rgB;
        try { rgB = X3(rgG, entry, false); }
        catch { return null; }
        try { return X4(alc, rgB); }
        finally { Array.Clear(rgB, 0, rgB.Length); }
    }

    //内存读节 命中返回数据 失败null回退文件
    private static byte[]? X6Mem(ProcessModule mm, string sSection)
    {
        IntPtr pBase = mm.BaseAddress;
        if (pBase == IntPtr.Zero) return null;
        int iPe = Marshal.ReadInt32(pBase, 0x3C);
        if (iPe < 0 || Marshal.ReadInt32(pBase, iPe) != 0x4550) return null;
        int iCnt = Marshal.ReadInt16(pBase, iPe + 6);
        int iOpt = Marshal.ReadInt16(pBase, iPe + 20);
        if (iCnt <= 0 || iOpt <= 0) return null;
        int iSec = iPe + 24 + iOpt;
        for (int i = 0; i < iCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            string sN = (Marshal.PtrToStringAnsi(new IntPtr(pBase.ToInt64() + o), 8) ?? "").TrimEnd('\0');
            if (sN != sSection) continue;
            int iVs = Marshal.ReadInt32(pBase, o + 8);
            int iVa = Marshal.ReadInt32(pBase, o + 12);
            if (iVs <= 0 || iVa <= 0) return null;
            byte[] rg = new byte[iVs];
            Marshal.Copy(new IntPtr(pBase.ToInt64() + iVa), rg, 0, iVs);
            return rg;
        }
        return null;
    }

    private static byte[] X6()
    {
        if (uLLCB == 0xCAFEBABEu) return Array.Empty<byte>();
        string sSection = S2(0x15083CC2u, new uint[] { 0x14783F52u, 0xB01FB573u, 0x52D7333Cu, 0xEFAEA9EDu }, uR2A, uR2B);
        //优先从内存模块读节 隐藏文件I/O痕迹
        try
        {
            var mm = Process.GetCurrentProcess().MainModule;
            if (mm != null)
            {
                byte[]? rgMem = X6Mem(mm, sSection);
                if (rgMem != null && rgMem.Length >= 128)
                    return rgMem;
            }
        }
        catch { }
        //回退为文件读取
        string sSelf = Process.GetCurrentProcess().MainModule?.FileName
#if NET6_0_OR_GREATER
            ?? Environment.ProcessPath
#endif
            ?? throw new InvalidOperationException();
        using var fs = new FileStream(sSelf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] rgHdr = new byte[0x400];
        int iRead = fs.Read(rgHdr, 0, rgHdr.Length);
        if (iRead < 0x400)
            throw new InvalidOperationException();

        int iPe = BitConverter.ToInt32(rgHdr, 0x3C);
        if (iPe < 0 || iPe + 0x18 > rgHdr.Length || BitConverter.ToUInt32(rgHdr, iPe) != 0x4550)
            throw new InvalidOperationException();

        ushort usCnt = BitConverter.ToUInt16(rgHdr, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgHdr, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        if (iSec < 0 || iSec + usCnt * 40 > rgHdr.Length)
            throw new InvalidOperationException();

        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            string sN = Encoding.ASCII.GetString(rgHdr, o, 8).TrimEnd('\0');
            uint uRaw = BitConverter.ToUInt32(rgHdr, o + 20);
            uint uRawSz = BitConverter.ToUInt32(rgHdr, o + 16);
            if (sN.Length == 4 && sN[0] == '.' && uRawSz == 0)
                GC.KeepAlive(uRaw);
            if (sN == sSection)
            {
                if (uRawSz == 0 || uRaw + uRawSz > (ulong)fs.Length)
                    throw new InvalidOperationException();
                byte[] rgLam = new byte[uRawSz];
                fs.Position = uRaw;
                int iGot = 0;
                while (iGot < uRawSz)
                {
                    int iRead2 = fs.Read(rgLam, iGot, (int)uRawSz - iGot);
                    if (iRead2 <= 0)
                        throw new InvalidOperationException();
                    iGot += iRead2;
                }
                return rgLam;
            }
        }
        throw new InvalidOperationException();
    }
}