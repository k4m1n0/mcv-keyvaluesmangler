using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace A0;

internal static class P
{
    private const uint uU0 = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int M0(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll")]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr hProc, ref bool pb);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr hProc, int cls, out IntPtr info, int len, out int ret);

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
    private delegate void JitAddSig(uint crc);

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
    private static byte[] _rgSeedKey = new byte[16];
    private static IntPtr _decPtr = IntPtr.Zero;
    private static IntPtr _jitBase = IntPtr.Zero;
    private static uint _uJitTextVa = 0;
    private static JitInstall _fnJitInstall = null!;
    private static JitSetKey _fnJitSetKey = null!;
    private static JitAddSig _fnJitAddSig = null!;

    //uint[]与k+i*delta异或 按UTF-16拼字符串
    private static string S1(uint uK, uint[] rgV)
    {
        char[] rgChars = new char[rgV.Length * 2];
        for (int i = 0; i < rgV.Length; i++)
        {
            uint uT = rgV[i] ^ (uK + (uLGAA ^ uLGAB) * (uint)i);
            rgChars[i * 2] = (char)(uT & 0xFFFF);
            rgChars[i * 2 + 1] = (char)(uT >> 16);
        }
        int iLen = Array.IndexOf(rgChars, '\0');
        if (iLen < 0) iLen = rgChars.Length;
        return new string(rgChars, 0, iLen);
    }

    //K_seed 16B 由KS常量XOR填充
    private static byte[] KSeed()
    {
        byte[] rgKey = new byte[16];
        BitConverter.GetBytes(uKS0A ^ uKS0B).CopyTo(rgKey, 0);
        BitConverter.GetBytes(uKS1A ^ uKS1B).CopyTo(rgKey, 4);
        BitConverter.GetBytes(uKS2A ^ uKS2B).CopyTo(rgKey, 8);
        BitConverter.GetBytes(uKS3A ^ uKS3B).CopyTo(rgKey, 12);
        return rgKey;
    }

    private static byte[] SeedKey(byte[] rgSeed)
    {
        byte[] rgKey = new byte[16];
        uint uA = 0x811C9DC5u, uB = 0x1B0CA2B5u;
        for (int i = 0; i < 16; i++)
        {
            uA ^= rgSeed[i]; uA *= 0x01000193u;
            uB ^= rgSeed[i + 16]; uB *= 0x9E3779B9u;
            uint uT = (uA ^ (uB << 1)) + (uB ^ (uA >> 3));
            rgKey[i] = (byte)((uT >> 16) ^ (uT >> 24) ^ rgSeed[i]);
        }
        return rgKey;
    }

    private static uint K1(byte[] rgSeed)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte byCh in rgSeed)
        {
            uH ^= byCh;
            uH *= uLK1A ^ uLK1B;
        }
        return uH;
    }

    //perm 由k派生的4元素置换表
    private static int[] K2(uint uK)
    {
        int[] rgPerm = { 0, 1, 2, 3 };
        uint uS = uK;
        for (int i = 3; i > 0; i--)
        {
            uS = uS * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            int j = (int)(uS % (uint)(i + 1));
            (rgPerm[i], rgPerm[j]) = (rgPerm[j], rgPerm[i]);
        }
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
            if (rgArgs.Length == 1 && rgArgs[0] == S1(0x11223344u, new uint[] { 0x110F3369u, 0xAF38AC91u, 0x4DF026DBu, 0xEBBAA01Du, 0x8A731A05u, 0x285B9384u, 0xC61B0DFCu, 0x64D58736u, 0x02DE0178u }))
                return Selftest();
            H1(rgArgs);
            byte[] rgG;
            List<(string, uint, uint, uint)> rgEntries;

            AD();
            var th = new Thread(() => { for (; ; ) { try { AD(); } catch { } Thread.Sleep(120); } }) { IsBackground = true };
            th.Start();
            SelfCheck();

            if ((RuntimeSeed() & 0x20000000u) != 0)
            {
                //真分叉：路径A带诱饵计算
                uint uQ1 = (uint)Environment.TickCount ^ 0xA5A5A5A5u;
                byte[] rgGA = X6();
                GC.KeepAlive(uQ1);
                rgG = rgGA;
            }
            else
            {
                //真分叉：路径B不同诱饵
                uint uQ2 = RuntimeSeed() ^ 0x5A5A5A5Au;
                rgG = X6();
                GC.KeepAlive(uQ2);
            }
            if (rgG.Length < 128)
                goto QF;
            H2(rgG);
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
                _fnJitSetKey(K1(KSeed()));
                _fnJitInstall();
            }
            if ((RuntimeSeed() & 0x10000000u) == 0)
            {
                uint uQ3 = (uint)Process.GetCurrentProcess().Id ^ 0x1B0CA2B5u;
                W1(rgG, rgEntries);
                GC.KeepAlive(uQ3);
            }
            else
            {
                uint uQ4 = (uint)Environment.TickCount ^ 0x9E3779B9u;
                W1(rgG, rgEntries);
                GC.KeepAlive(uQ4);
            }
            byte[] rgMain = X3(rgG, rgEntries[0]);
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
        bool fDbg = IsDebuggerPresent();
        if (!fDbg)
        {
            bool fCr = false;
            if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref fCr) && fCr)
                fDbg = true;
        }
        if (!fDbg)
        {
            IntPtr pPort = IntPtr.Zero;
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 7, out pPort, IntPtr.Size, out _) == 0 && pPort != IntPtr.Zero)
                fDbg = true;
        }
        if (fDbg)
            Environment.FailFast(null);
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
        //关键常量完整性：防patch密钥派生常量
        if ((uKS0A ^ uKS0B) != 0x80020010u) Environment.FailFast(null);
        if ((uKS1A ^ uKS1B) != 0xEB900128u) Environment.FailFast(null);
        if ((uKS2A ^ uKS2B) != 0x021000C3u) Environment.FailFast(null);
        if ((uKS3A ^ uKS3B) != 0x90288001u) Environment.FailFast(null);
        if ((uLK0A ^ uLK0B) != 0x811C9DC5u) Environment.FailFast(null);
        if ((uLK1A ^ uLK1B) != 0x000001B3u) Environment.FailFast(null);
    }

    private static int Selftest()
    {
        byte[] rgG = X6();
        if (!X1(rgG, out var rgEntries) || rgEntries.Count == 0) return 2;
        EnsureDecoder(rgG, rgEntries);
        byte[] rgMain = X3(rgG, rgEntries[0]);
        uint uExp = BitConverter.ToUInt32(rgG, LB_HASH);
        uint uAct = Fnv1a(rgMain);
        Array.Clear(rgMain, 0, rgMain.Length);
        return uAct == uExp ? 0 : 3;
    }

    private static uint Fnv1a(byte[] rgData)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte byX in rgData) { uH ^= byX; uH *= uLK1A ^ uLK1B; }
        return uH;
    }

    //解析--参数 诱饵
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

    //计算g前0x100字节hash 诱饵
    private static void H2(byte[] rgG)
    {
        uint uH = uLK0A ^ uLK0B;
        int iN = Math.Min(rgG.Length, 0x100);
        for (int i = 0; i < iN; i++)
            uH = (uH ^ rgG[i]) * (uLK1A ^ uLK1B);
        GC.KeepAlive(uH);
    }

    //写元数据 并注册AssemblyLoadContext.Resolving
    private static void W1(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
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
        Span<long> rgStack = stackalloc long[16];
        for (int i = 0; i < rgStack.Length; i++)
            rgStack[i] = unchecked((long)0xC300EB9090909090);
        try
        {
            M0(IntPtr.Zero,
               S1(0x2ABF5FF6u, new uint[] { 0x2ADE5FB0u, 0xC897D9DBu, 0x67145304u, 0x0526CD01u, 0xA3EF46B5u, 0x4197C0F6u, 0xE05E3A00u, 0x7E2AB425u, 0x1C122DD0u, 0xBADBA703u, 0x58862151u, 0xF75B9A80u, 0x952D14C3u, 0x33FF8E32u, 0xD1E8087Au, 0x6F9E81ABu, 0x0E5AFBEFu, 0xAC0A755Au }),
               S1(0x29CE007Au, new uint[] { 0x29A10012u, 0xC8717A40u, 0x6644F38Au, 0x04746DD7u }),
               uU0);
        }
        catch { }
        return 1;
    }

    //解析bundle条目表 用seed派生key解密
    private static bool X1(byte[] rgG, out List<(string, uint, uint, uint)> rgEntries)
    {
        rgEntries = new List<(string, uint, uint, uint)>();
        if (rgG.Length < 128) return false;

        uint uK;
        {
            byte[] rgSeed = new byte[32];
            byte[] rgKseed = KSeed();
            for (int i = 0; i < 32; i++)
                rgSeed[i] = (byte)(rgG[LB_SEED + i] ^ rgKseed[i % 16]);
            uK = K1(rgSeed);
            _rgSeedKey = SeedKey(rgSeed);
            Array.Clear(rgSeed, 0, rgSeed.Length);
            Array.Clear(rgKseed, 0, rgKseed.Length);
        }

        int iCount = (int)(Q(rgG, LB_HEAD) ^ uK);
        uint uOff1 = Q(rgG, LB_HEAD + 4) ^ uK;
        uint uOff2 = Q(rgG, LB_HEAD + 8) ^ uK;
        int iDecoys = (int)(Q(rgG, LB_HEAD + 12) ^ K3(uK, iCount));
        if (iCount <= 0 || iCount > 0x1000 || uOff1 == 0 || uOff1 > 0x10000000 || uOff2 == 0 || iDecoys < 0 || iDecoys > 0x100)
            return false;

        int iTotal = iCount + iDecoys;
        long llTEnd = LB_TBL + (long)iTotal * 20;
        if (llTEnd > rgG.Length) return false;

        int[] rgPerm = K2(uK);
        var rgRow = new uint[iTotal * 5];
        uint uNameTotal = 0;
        for (int i = 0; i < iTotal; i++)
        {
            uint uKk = K3(uK, i);
            int o = LB_TBL + i * 20;
            uint[] rgF = new uint[4];
            for (int s = 0; s < 4; s++)
                rgF[rgPerm[s]] = Q(rgG, o + s * 4) ^ uKk;
            rgRow[i * 5 + 0] = rgF[0];
            rgRow[i * 5 + 1] = rgF[1];
            rgRow[i * 5 + 2] = rgF[2];
            rgRow[i * 5 + 3] = rgF[3];
            uNameTotal += rgF[0];
        }

        long llNa = (uNameTotal + 3u) & ~3u;
        long llDs = llTEnd + llNa;
        if (llDs > rgG.Length) return false;

        for (int i = 0; i < iCount; i++)
        {
            if (rgRow[i * 5 + 1] == 0) return false;
            long llNo = llTEnd;
            for (int j = 0; j < i; j++) llNo += rgRow[j * 5];
            if (llNo + rgRow[i * 5] > rgG.Length) return false;
            string sName = Encoding.UTF8.GetString(rgG, (int)llNo, (int)rgRow[i * 5]);
            long llDoff = llDs + rgRow[i * 5 + 3];
            if (rgRow[i * 5 + 2] > 0 && llDoff + rgRow[i * 5 + 2] > rgG.Length) return false;
            rgEntries.Add((sName, rgRow[i * 5 + 1], rgRow[i * 5 + 2], (uint)llDoff));
        }
        _iDec = iCount - 4;
        _iJit = _iDec + 1;
        _iSig = _iDec + 2;
        return true;
    }

    private static string X2(string sName)
        => sName.EndsWith(S1(0x28CF58B4u, new uint[] { 0x28AB589Au, 0xC76AD201u }), StringComparison.OrdinalIgnoreCase)
            ? sName.Substring(0, sName.Length - 4) : sName;

    //提取Iamdec解码器 以LoadBare加载
    private static void EnsureDecoder(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (_decPtr != IntPtr.Zero) return;
        if (_iDec >= 0 && _iDec < rgEntries.Count)
        {
            var entry = rgEntries[_iDec];
            byte[] rgDll = new byte[entry.Item2];
            for (int i = 0; i < rgDll.Length; i++)
                rgDll[i] = (byte)(rgG[(int)entry.Item4 + i] ^ _rgSeedKey[i % 16]);
            _decPtr = LoadBare(rgDll, true);
            Array.Clear(rgDll, 0, rgDll.Length);
            return;
        }
        throw new InvalidDataException(S1(0x7EF1D782u, new uint[] { 0x7E99D7F1u, 0x1D5B5154u }));
    }

    //手工映射裸PE返回基址 .text/.rdata按RVA排布
    private static IntPtr LoadBare(byte[] rgDll, bool fRx)
    {
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        uint uTextVa = 0; int iTextRaw = 0, iTextSz = 0;
        uint uDataVa = 0; int iDataRaw = 0, iDataSz = 0;      // .rdata
        uint uData2Va = 0; int iData2Raw = 0, iData2Sz = 0;    // .data
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            string sName = Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0');
            uint uVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uRsz = BitConverter.ToUInt32(rgDll, o + 16);
            if (sName == ".text") { uTextVa = uVa; iTextRaw = (int)uRaw; iTextSz = (int)uRsz; }
            else if (sName == ".rdata") { uDataVa = uVa; iDataRaw = (int)uRaw; iDataSz = (int)uRsz; }
            else if (sName == ".data") { uData2Va = uVa; iData2Raw = (int)uRaw; iData2Sz = (int)uRsz; }
        }
        if (iTextSz == 0)
            throw new InvalidDataException(S1(0x55667788u, new uint[] { 0x550777EAu, 0xF3BDF125u, 0x91B06A9Eu, 0x3063E4D0u, 0xCE215E08u, 0x6C7BD857u }));
        int cbMap = iTextSz + 0x1000 + (iDataSz > 0 ? (int)(uDataVa - uTextVa) + iDataSz : 0) + (iData2Sz > 0 ? (int)(uData2Va - uTextVa) + iData2Sz : 0) + 0x1000;
        IntPtr p = VirtualAlloc(IntPtr.Zero, (UIntPtr)cbMap, 0x3000, fRx ? 0x04u : 0x40u);
        if (p == IntPtr.Zero)
            throw new InvalidDataException(S1(0x99AABBCCu, new uint[] { 0x99CBBBBAu, 0x378E35E9u, 0xD67AAF51u }));
        Marshal.Copy(rgDll, iTextRaw, p, iTextSz);
        if (iDataSz > 0)
            Marshal.Copy(rgDll, iDataRaw, new IntPtr(p.ToInt64() + (uDataVa - uTextVa)), iDataSz);
        if (iData2Sz > 0)
            Marshal.Copy(rgDll, iData2Raw, new IntPtr(p.ToInt64() + (uData2Va - uTextVa)), iData2Sz);
        if (fRx)
            VirtualProtect(p, (UIntPtr)iTextSz, 0x20, out _);   // .text -> RX (no W)
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
        int iNameOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x20));
        int iOrdOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x24));
        int iFuncOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x1C));
        for (int i = 0; i < iNameCount; i++)
        {
            int iNameRva = BitConverter.ToInt32(rgDll, iNameOff + i * 4);
            int iNameOff2 = RvaToOff(rgDll, iPe, usOpt, (uint)iNameRva);
            if (iNameOff2 <= 0) continue;
            int iEnd = iNameOff2;
            while (iEnd < rgDll.Length && rgDll[iEnd] != 0) iEnd++;
            string sName = Encoding.ASCII.GetString(rgDll, iNameOff2, iEnd - iNameOff2);
            int usOrd = BitConverter.ToUInt16(rgDll, iOrdOff + i * 2);
            uint uFnRva = BitConverter.ToUInt32(rgDll, iFuncOff + usOrd * 4);
            rgExports[sName] = uFnRva;
        }
        return rgExports;
    }

    private static int RvaToOff(byte[] rgDll, int iPe, ushort usOpt, uint uRva)
    {
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            uint uVs = BitConverter.ToUInt32(rgDll, o + 8);
            uint uVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRs = BitConverter.ToUInt32(rgDll, o + 16);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uEnd = Math.Max(uVs, uRs);
            if (uRva >= uVa && uRva < uVa + uEnd) return (int)(uRaw + (uRva - uVa));
        }
        return -1;
    }

    private static uint PeTextVa(byte[] rgDll)
    {
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') == ".text")
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
        if (_iJit >= 0 && _iJit < rgEntries.Count)
        {
            var entry = rgEntries[_iJit];
            byte[] rgDll = X3(rgG, entry);
            _jitBase = LoadBare(rgDll, true);   // jithook .data now mapped RW, .text RX
            _uJitTextVa = PeTextVa(rgDll);
            var rgExports = ParseExports(rgDll);
            _fnJitInstall = Mk<JitInstall>(rgExports["InstallJitHook"]);
            _fnJitSetKey = Mk<JitSetKey>(rgExports["SetJitHookKey"]);
            _fnJitAddSig = Mk<JitAddSig>(rgExports["AddPayloadSig"]);
            Array.Clear(rgDll, 0, rgDll.Length);
        }
    }

    private static void InjectSigs(byte[] rgG, List<(string, uint, uint, uint)> rgEntries)
    {
        if (_iSig >= 0 && _iSig < rgEntries.Count)
        {
            var entry = rgEntries[_iSig];
            byte[] rgSig = X3(rgG, entry);
            for (int i = 0; i + 4 <= rgSig.Length; i += 4)
                _fnJitAddSig(BitConverter.ToUInt32(rgSig, i));
            Array.Clear(rgSig, 0, rgSig.Length);
        }
    }

    //解压条目 压缩按4KB页调Iamdec 非压缩直接XOR
    private static byte[] X3(byte[] rgG, (string, uint, uint, uint) entry)
    {
        int iDoff = (int)entry.Item4;
        uint uRawLen = entry.Item2;
        int iCompLen = (int)entry.Item3;
        if (iCompLen == (int)uRawLen)
        {
            //非压缩 直接XOR seed key还原
            byte[] rgRaw = new byte[uRawLen];
            for (int i = 0; i < rgRaw.Length; i++)
                rgRaw[i] = (byte)(rgG[iDoff + i] ^ _rgSeedKey[i % 16]);
            return rgRaw;
        }
        if (_decPtr == IntPtr.Zero)
            throw new InvalidDataException(S1(0xDDEEFF00u, new uint[] { 0xDD8BFF64u, 0x7C4978DAu, 0x1A38F216u, 0xB8B56C59u, 0x56A3E58Au, 0xF5245FE9u, 0x9354D93Au, 0x3117536Eu, 0xCFCECCADu }));
        var fnDec = Marshal.GetDelegateForFunctionPointer<DecPage>(_decPtr);
        byte[] rgComp = new byte[iCompLen];
        Array.Copy(rgG, iDoff, rgComp, 0, iCompLen);
        byte[] rgOut = new byte[uRawLen];
        byte[] rgHist = new byte[0x80000];
        byte[] rgPage = new byte[0x1000];
        byte[] rgState = new byte[0x38];
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x00);//uInPos
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x04);//iHist
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x08);//uOutPos
        BitConverter.GetBytes((uint)iCompLen).CopyTo(rgState, 0x28);//srcLen
        BitConverter.GetBytes(uRawLen).CopyTo(rgState, 0x34);//dstLen
        rgHist[0] = rgComp[0];
        for (uint uPg = 0; uPg * 0x1000 < uRawLen; uPg++)
        {
            uint uPs = uPg * 0x1000;
            uint uPe = Math.Min(uPs + 0x1000, uRawLen);
            if (uPg == 0) rgPage[0] = rgComp[0];
            else Array.Clear(rgPage, 0, 0x1000);
            BitConverter.GetBytes(uPs).CopyTo(rgState, 0x2C);
            BitConverter.GetBytes(uPe).CopyTo(rgState, 0x30);
            int iRc = fnDec(rgState, rgHist, rgPage, rgComp);
            if (iRc != 0)
                throw new InvalidDataException(S1(0x12345678u, new uint[] { 0x1251561Cu, 0xB04BD052u, 0x4EC04998u, 0xECDAC39Eu }) + iRc.ToString("X"));
            int iC = (int)(uPe - uPs);
            Array.Copy(rgPage, 0, rgOut, (int)uPs, iC);
        }
        Array.Clear(rgComp, 0, rgComp.Length);
        Array.Clear(rgState, 0, rgState.Length);
        Array.Clear(rgHist, 0, rgHist.Length);
        return rgOut;
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
        try { rgB = X3(rgG, entry); }
        catch { return null; }
        try { return X4(alc, rgB); }
        finally { Array.Clear(rgB, 0, rgB.Length); }
    }

    private static byte[] X6()
    {
        string sSelf = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException(S1(0x5B5DDEAEu, new uint[] { 0x5B2FDEFEu, 0xF9F65808u, 0x97BFD245u, 0x36544BAAu, 0xD44FC5F3u, 0x72533F23u, 0x10C4B971u, 0xAE9432DCu, 0x4D70AC17u, 0xEB302643u, 0x89E49F8Au, 0x27C019C4u }));
        string sSection = S1(0xD8727B8Bu, new uint[] { 0xD8007BA5u, 0x76C8F520u, 0x14806E89u });
        using var fs = new FileStream(sSelf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] rgHdr = new byte[0x400];
        int iRead = fs.Read(rgHdr, 0, rgHdr.Length);
        if (iRead < 0x400)
            throw new InvalidOperationException(S1(0x338B2FA6u, new uint[] { 0x33E32FD5u, 0xD1B0A930u, 0x6FDA236Cu, 0x0E749C81u, 0xAC0116AAu, 0x4AC19026u, 0xE8BD0998u, 0x870F83C7u }));

        int iPe = BitConverter.ToInt32(rgHdr, 0x3C);
        if (iPe < 0 || iPe + 0x18 > rgHdr.Length || BitConverter.ToUInt32(rgHdr, iPe) != 0x4550)
            throw new InvalidOperationException(S1(0x45ADEC6Bu, new uint[] { 0x45C2EC03u, 0xE3916657u, 0x8275DFFDu, 0x207459E5u, 0xBEE4D321u, 0x5CE34D7Cu, 0xFADAC6A0u, 0x9977402Au, 0x3700BA13u, 0xD5C03381u, 0x73BDADC2u }));

        ushort usCnt = BitConverter.ToUInt16(rgHdr, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgHdr, iPe + 20);
        int iSec = iPe + 24 + usOpt;
        if (iSec < 0 || iSec + usCnt * 40 > rgHdr.Length)
            throw new InvalidOperationException(S1(0x29B16E1Au, new uint[] { 0x29D96E69u, 0xC79AE7BCu, 0x660061F8u, 0x0432DB36u, 0xA2FB549Du, 0x40A9CEDEu, 0xDEDE481Eu, 0x7D54C25Du, 0x1B013B80u, 0xB9A4B5FEu }));

        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + i * 40;
            if (Encoding.ASCII.GetString(rgHdr, o, 8).TrimEnd('\0') == sSection)
            {
                uint uRaw = BitConverter.ToUInt32(rgHdr, o + 20);
                uint uRawSz = BitConverter.ToUInt32(rgHdr, o + 16);
                if (uRawSz == 0 || uRaw + uRawSz > (ulong)fs.Length)
                    throw new InvalidOperationException(S1(0x555FFA2Eu, new uint[] { 0x553EFA4Cu, 0xF3B77383u, 0x91BCED8Eu, 0x3067673Du, 0xCE5CE166u, 0x6C065AEBu, 0x0ACFD4E1u, 0xA88D4E49u, 0x4775C799u, 0xE531418Fu, 0x83FFBB07u, 0x21A6354Fu, 0xBFF9AEA9u }));
                byte[] rgLam = new byte[uRawSz];
                fs.Position = uRaw;
                int iGot = 0;
                while (iGot < uRawSz)
                {
                    int iRead2 = fs.Read(rgLam, iGot, (int)uRawSz - iGot);
                    if (iRead2 <= 0)
                        throw new InvalidOperationException(S1(0xE8E251BAu, new uint[] { 0xE88A51C9u, 0x876BCB1Cu, 0x25714558u, 0xC3EDBE97u, 0x61A438FFu, 0xFF91B277u, 0x9E402C62u, 0x3C46A5A4u, 0xDAFB1FF1u, 0x78B39957u }));
                    iGot += iRead2;
                }
                return rgLam;
            }
        }
        throw new InvalidOperationException(S1(0x820212E1u, new uint[] { 0x827012CFu, 0x20588CFEu, 0xBE100627u, 0x5CDB802Cu, 0xFABCF9A0u, 0x997E730Au, 0x3720ED58u, 0xD5E866D0u, 0x73C9E0C6u, 0x11935A42u, 0xB059D474u, 0x4E004DBAu }));
    }
}
