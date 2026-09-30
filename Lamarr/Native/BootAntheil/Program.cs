using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Numerics;

namespace A0;

internal static partial class P
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
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    //时间源用内核单调计数 本进程调时钟不影响它
    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, uint uSamDesired, out IntPtr phkResult);
    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DecodePageFunc(byte[] state, byte[] hist, byte[] page, byte[] src);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int JitInstall(int bScrubAllowed, IntPtr pTrace);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitSetKey(ulong key, ulong split);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void JitSetSigTable(IntPtr pSig, uint uCount, uint uMaskLo, IntPtr pVars);
    private delegate bool BundleParseFunc(byte[] rgBundle, out List<(string, uint, uint, uint)> rgEntries);
    private delegate int JitVerifyHook();
    private delegate void JitSetAdFlag(uint f);
    private delegate int JitSetSlots(uint gmi, uint ci, uint mode);
    //返回六个入口交接表的下标 对应原序号加一
    private delegate IntPtr JitTable();
    private delegate ulong EngineRun(IntPtr pBc, IntPtr pRegs, IntPtr pMem, ulong cbM);//完成态Pianola VM入口

    //这个结构必须正好1232字节 内核按同样的宽度写进去
    [StructLayout(LayoutKind.Sequential, Size = 1232)]
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

    private static readonly uint uLK0A = 0x12345678, uLK0B = 0x9328CBBD;//0x811C9DC5
    private static readonly uint uLK1A = 0x11111111, uLK1B = 0x111110A2;//0x000001B3
    //内核拿固定向量上的派生值自校验 期望值只含常量
    private static readonly uint uExpJitKeyA = 0x7E1B93C5, uExpJitKeyB = 0x09C9E078;//0x77D273BD
    //签名表掩码派生的期望值 与密钥域分开 不能改
    private static readonly uint uExpMaskKeyA = 0x3C6EA419, uExpMaskKeyB = 0xECFC4EC6;//0xD092EADF
    private static readonly uint uLGAA = 0x0F0F0F0F, uLGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint uLLCA = 0x0A0A0A0A, uLLCB = 0x0A136C07;//0x0019660D
    private static readonly uint uLQCA = 0x5A5A5A5A, uLQCB = 0x6634A905;//0x3C6EF35F
    private static readonly uint uKS0A = 0x1A2B3C4D, uKS0B = 0x9A293C5D;//0x80020010
    private static readonly uint uKS1A = 0x12345678, uKS1B = 0xF9A45750;//0xEB900128
    private static readonly uint uKS2A = 0x5A5A5A5A, uKS2B = 0x584A5A99;//0x021000C3
    private static readonly uint uKS3A = 0x0F0F0F0F, uKS3B = 0x9F278F0E;//0x90288001

    private static int _iDecSlot = -1, _iJitSlot = -1, _iSigSlot = -1;
    private static uint _uSiteKeyA, _uSiteKeyB, _uSiteKeyC, _uSiteKeyD;
    private static IntPtr _pDecPage = IntPtr.Zero;
    private static IntPtr _pJitBase = IntPtr.Zero;
    private static int _jitTextSize = 0;
    private static uint _jitTextCrc = 0;
    private static int _decoderTextSize = 0;
    private static uint _decoderTextCrc = 0;
    private const uint _uExpHashKey = 0x9E3779B9u;
    private static uint _X(uint uValue) => ((uValue ^ _uExpHashKey) >> 7) | ((uValue ^ _uExpHashKey) << 25);
    //打包器在最终镜像上量出期望值写进占位块 哨兵还在就是补丁没生效
    private static readonly byte[] _expBlob = {
        0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,
        0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,
        0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,
        0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,
        0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5,0xA5 };
    private static uint Exp(ExpSlot s) => BitConverter.ToUInt32(_expBlob, (int)s * 4);
    private static uint _uJitTextRva = 0;
    private static bool _bHeadWiped;      //PatchRegionHeader是否已经把容器里的根材料清掉
    private static int _iFanTick;         //宽扇入守卫的循环计数 等约5秒才第一次生效
    private static IntPtr _pTrace = IntPtr.Zero;      //jithook编译轨迹环
    private static int _iTraceLogged = 0;
    private static JitInstall _fnJitInstall = null!;
    private static JitSetKey _fnJitSetKey = null!;
    private static JitSetSigTable _fnJitSetTable = null!;
    private static JitVerifyHook _fnJitVerifyHook = null!;
    private static JitSetAdFlag _fnJitSetAdFlag = null!;
    private static JitSetSlots _fnJitSetSlots = null!;
    private static JitTable _fnJitTable = null!;   //从JitHookTable取回的交接表
  private static bool _bJitReady;
  private static bool _engineOk;
  private static uint _engineLen;
  private static IntPtr _engineBase = IntPtr.Zero;
  private static uint _engineTextVa = 0;
  private static int _engineTextSz = 0;
  private static EngineRun _fnEngine = null!;
  private static IntPtr _pRoll = IntPtr.Zero;
  private static int _cbRoll = 0;
    //容器与各内核入口布局见装载流程 内核入口按容器顺序存放 最后一颗只做编码与解析 没有调用点
  private static readonly IntPtr[] _pRollKern = new IntPtr[7];
  const int KI_JIT = 0, KI_MASK = 1, KI_SEED = 2, KI_K1 = 3, KI_DIST = 4, KI_MX = 5, KI_DEC = 6;
  private static uint _uRollKey = 0;//roll的at-rest密钥由根材料派生 在RestoreData里算好 引擎里不带

    private static readonly uint uQ1A = 0x12345678, uQ1B = 0xB7F1F3DD;//0xA5A5A5A5
    private static readonly uint uQ2A = 0x1A2B3C4D, uQ2B = 0x40716617;//0x5A5A5A5A
    private static readonly uint uQ3A = 0x12345678, uQ3B = 0x0938F4CD;//0x1B0CA2B5
    private static readonly uint uQ4A = 0x1A2B3C4D, uQ4B = 0x841C45F4;//0x9E3779B9
    private static readonly uint uR1A = 0x12345678, uR1B = 0x133457EB;//0x01000193
    private static readonly uint uR2A = 0x12345678, uR2B = 0x8C032FC1;//0x9E3779B9
    private static readonly uint uHs = 0x13579BDFu;
    private static readonly uint uV1A = 0xD39F08D9u, uV1B = 0x539D08C9u;//0x80020010
    private static readonly uint uV2A = 0x1D1E0402u, uV2B = 0xF68E052Au;//0xEB900128
    private static readonly uint uV3A = 0x23D2A242u, uV3B = 0x21C2A281u;//0x021000C3
    private static readonly uint uV4A = 0x27AF7AC7u, uV4B = 0xB787FAC6u;//0x90288001
    private static readonly uint uV5A = 0x75663258u, uV5B = 0xF47AAF9Du;//0x811C9DC5
    private static readonly uint uV6A = 0x9F6755EFu, uV6B = 0x9F67545Cu;//0x000001B3
    private static readonly uint uV7A = 0xF58C5705u, uV7B = 0xE6DBCCDAu;//0x13579BDF
    //方法体哈希用方法指针与委托引用 靠令牌不靠名字 元数据重命名后仍有效
    private static readonly Action _dgAd = AD;
    private static readonly BundleParseFunc _dgX1 = X1;
    private static readonly Func<byte[], (string, uint, uint, uint), bool, byte[]> _dgX3 = X3;
    private static readonly Func<byte[]> _dgX6 = X6;
    private static uint _uAdTick = 0;
    private static long _llTick = 0;
    private static uint _uX1Hash = 0;
    private static readonly string[] rgDebugDllNames =
    {
        DecodeNameRot(0x432BF782u, new uint[] { 0x41B3F49Au, 0xE2AB725Bu, 0x7CFAE9FCu, 0x1F9267E5u, 0xBF29DD4Eu, 0x5B315B3Fu, 0xFB18D2B8u, 0x96B04B91u }, uR2A, uR2B),
        DecodeNameRot(0x7579CA59u, new uint[] { 0x77E1C919u, 0x10B94782u, 0xB268BFB3u, 0x520036F4u, 0xED77B25Du, 0x8FEF2AF6u }, uR2A, uR2B),
        DecodeNameRot(0x234E5F7Eu, new uint[] { 0x206E5C0Eu, 0xC31DDAB7u, 0x5C755380u, 0xFED4CFC9u, 0x9F4C4662u }, uR2A, uR2B),
        DecodeNameRot(0x2B7E1516u, new uint[] { 0x28BE14A6u, 0xC8158DEFu, 0x64FD0BB0u, 0x07548169u, 0xA79BF8D2u, 0x429375B3u }, uR2A, uR2B),
        DecodeNameRot(0x28AED2A6u, new uint[] { 0x2B16D1EEu, 0xC5964F7Fu, 0x660DC520u, 0x02253CF9u, 0xA24CBAA2u, 0x3FC43343u }, uR2A, uR2B),
        DecodeNameRot(0xABF71588u, new uint[] { 0xA8EF16C8u, 0x49068C49u, 0xEBC60BD2u, 0x85ED818Bu, 0x279CFF1Cu, 0xC024774Du, 0x6283EE1Eu, 0xFECB6B6Fu, 0x9C02E2F0u, 0x3A9A5E21u, 0xD9E1D5EAu, 0x7859507Bu }, uR2A, uR2B),
        DecodeNameRot(0x97F28BD7u, new uint[] { 0x946A88CFu, 0x35E206F0u, 0xD7017C41u, 0x73E8FA22u, 0x13B071DBu, 0xAF07EC74u }, uR2A, uR2B),
        DecodeNameRot(0x1657D22Au, new uint[] { 0x15CFD132u, 0xB7474883u, 0x51A6C694u, 0xF2063C95u, 0x8E85B8AEu, 0x2C1D31E7u, 0xC8C4AFE0u, 0x69DC2639u }, uR2A, uR2B),
        DecodeNameRot(0xD8A14A8Fu, new uint[] { 0xDB394997u, 0x7510C728u, 0x16703D09u, 0xB007B4F2u, 0x525F325Bu, 0xEEC6A80Cu, 0x8E8E2785u, 0x2C259E9Eu }, uR2A, uR2B),
        DecodeNameRot(0xE9AA0189u, new uint[] { 0xEAC202C1u, 0x84F978D2u, 0x2560F763u, 0xC7286D84u, 0x6127E91Du, 0x039F616Eu, 0x9DFED8E7u, 0x3E5E56E0u, 0xD8FDCCF1u, 0x7AD54A12u, 0x144CC3B3u, 0xB59C3FD4u, 0x5733B595u, 0xF1332C86u, 0x939AA8D7u, 0x2DCA2000u, 0xCE419D19u }, uR2A, uR2B),
    };

    private static readonly string[] rgDumpDllNames =
    {
        DecodeNameRot(0x5A3C9E11u, new uint[] { 0x591C9D01u, 0xFB4C14D2u, 0x95D39213u, 0x37CB0A4Cu, 0xD03A8795u, 0x7231FEAEu }, uR2A, uR2B),//dbgcore.dll
        DecodeNameRot(0x71B4E2D3u, new uint[] { 0x7294E1C3u, 0x0CD45FCCu, 0xAD0BD525u, 0x4FDB4E8Eu, 0xE9B2CAD7u, 0x8BAA4370u }, uR2A, uR2B),//dbghelp.dll
        DecodeNameRot(0x2C8F41A7u, new uint[] { 0x2FAF42B7u, 0xC9FEB848u, 0x6B8E3621u, 0x0645ADF2u, 0xA60D2BEBu }, uR2A, uR2B),//dbgeng.dll
        DecodeNameRot(0x43D1A8FFu, new uint[] { 0x40F1ABEFu, 0xE13121D0u, 0x83389F51u, 0x1D50154Au, 0xBDDF8CC3u, 0x59870AFCu }, uR2A, uR2B),//dbgmodel.dll
        DecodeNameRot(0x9E27D5B3u, new uint[] { 0x9DA7D62Bu, 0x3FC74C64u, 0xD916CA6Du, 0x79BE41FEu, 0x1465BFF7u }, uR2A, uR2B),//pssapi.dll
        DecodeNameRot(0xB6E309C5u, new uint[] { 0xB5C30AD5u, 0x562280E6u, 0xF011FE7Fu, 0x92E17780u, 0x2CE0F3C9u, 0xCE986A62u }, uR2A, uR2B),//dbgshim.dll
        DecodeNameRot(0x1D7A44E9u, new uint[] { 0x1E124771u, 0xB8A9BDDAu, 0x5A793B7Bu, 0xFB28B10Cu, 0x954028B5u, 0x371FA6AEu, 0xD3B71C1Fu, 0x739E9B98u }, uR2A, uR2B),//mscordaccore.dll
    };
    private static readonly string sLocalDumpsKey = DecodeNameRot(0x3F5C81B7u, new uint[] { 0x3DC483CFu, 0xDFA3F9D0u, 0x79737721u, 0x1892ECCAu, 0xBADA6AF3u, 0x5539E14Cu, 0xF7395F75u, 0x9178D6BEu, 0x32284CDFu, 0xCDAFCB80u, 0x6ECF4181u, 0x089EBFD2u, 0xAA4E35FBu, 0x4ACDB2A4u, 0xE52D2AA5u, 0x87BCA0F6u, 0x216C1EDFu, 0xC00B9528u, 0x5CD31329u, 0xFE0289E2u, 0x9AB206BBu, 0x3AC17E64u, 0xDB58F40Du, 0x75F8721Eu, 0x17FFE837u, 0xB02766A8u, 0x5386DD99u, 0xEC3E5B5Au, 0x8F4DD25Bu, 0x28CD482Cu, 0xCA44C565u }, uR2A, uR2B);

    private static string DecodeName(uint uXorKey, uint[] rgWords)
    {
        char[] rgChars = new char[rgWords.Length * 2];
        uint uVerify = (uXorKey ^ 0x3C6EF372u) + (uint)rgWords.Length;
        for (int i = 0; i < rgWords.Length; i++)
        {
            uint uWord = rgWords[i] ^ (uXorKey + (uLGAA ^ uLGAB) * (uint)i);
            rgChars[i * 2] = (char)(uWord & 0xFFFF);
            rgChars[i * 2 + 1] = (char)(uWord >> 16);
            uVerify = (uVerify * 0x01000193u) ^ (uWord & 0xFF);
        }
        GC.KeepAlive(uVerify);
        int iLen = Array.IndexOf(rgChars, '\0');
        if (iLen < 0) iLen = rgChars.Length;
        return new string(rgChars, 0, iLen);
    }

    private static void WriteU32(byte[] rgBuf, int iOff, uint uValue)
    {
        rgBuf[iOff] = (byte)uValue; rgBuf[iOff + 1] = (byte)(uValue >> 8);
        rgBuf[iOff + 2] = (byte)(uValue >> 16); rgBuf[iOff + 3] = (byte)(uValue >> 24);
    }

    private static void ChaChaQuarterRound(ref uint uA, ref uint uB, ref uint uC, ref uint uD)
    {
        uA += uB; uD ^= uA; uD = (uD << 16) | (uD >> 16);
        uC += uD; uB ^= uC; uB = (uB << 12) | (uB >> 20);
        uA += uB; uD ^= uA; uD = (uD << 8) | (uD >> 24);
        uC += uD; uB ^= uC; uB = (uB << 7) | (uB >> 25);
    }

    private static void ChaChaBlock(byte[] rgKey, byte[] rgNonce, uint uCounter, byte[] rgOut)
    {
        uint[] rgState = new uint[16];
        rgState[0] = 0x61707865; rgState[1] = 0x3320646e; rgState[2] = 0x79622d32; rgState[3] = 0x6b206574;
        for (int i = 0; i < 8; i++) rgState[4 + i] = ReadU32(rgKey, i * 4);
        rgState[12] = uCounter;
        rgState[13] = ReadU32(rgNonce, 0); rgState[14] = ReadU32(rgNonce, 4); rgState[15] = ReadU32(rgNonce, 8);
        uint[] rgWork = (uint[])rgState.Clone();
        for (int r = 0; r < 10; r++)
        {
            ChaChaQuarterRound(ref rgWork[0], ref rgWork[4], ref rgWork[8], ref rgWork[12]);
            ChaChaQuarterRound(ref rgWork[1], ref rgWork[5], ref rgWork[9], ref rgWork[13]);
            ChaChaQuarterRound(ref rgWork[2], ref rgWork[6], ref rgWork[10], ref rgWork[14]);
            ChaChaQuarterRound(ref rgWork[3], ref rgWork[7], ref rgWork[11], ref rgWork[15]);
            ChaChaQuarterRound(ref rgWork[0], ref rgWork[5], ref rgWork[10], ref rgWork[15]);
            ChaChaQuarterRound(ref rgWork[1], ref rgWork[6], ref rgWork[11], ref rgWork[12]);
            ChaChaQuarterRound(ref rgWork[2], ref rgWork[7], ref rgWork[8], ref rgWork[13]);
            ChaChaQuarterRound(ref rgWork[3], ref rgWork[4], ref rgWork[9], ref rgWork[14]);
        }
        for (int i = 0; i < 16; i++) rgWork[i] += rgState[i];
        for (int i = 0; i < 16; i++) WriteU32(rgOut, i * 4, rgWork[i]);
    }

    private static byte[] KdfChaCha(ulong uKey)
    {
        byte[] rgKey = new byte[32];
        byte[] rgNonce = new byte[12];
        WriteU32(rgNonce, 0, (uint)(uKey & 0xFFFFFFFFu));
        WriteU32(rgNonce, 4, (uint)(uKey >> 32));
        WriteU32(rgNonce, 8, 0x017A9C53u);
        byte[] rgBlock = new byte[64];
        ChaChaBlock(rgKey, rgNonce, 0, rgBlock);
        byte[] rgOut = new byte[32];
        Array.Copy(rgBlock, 0, rgOut, 0, 32);
        return rgOut;
    }

    private static byte[] PolyTagKey()
    {
        byte[] rgSiteKey = SiteKey();
        ulong uSiteKey64 = BitConverter.ToUInt64(rgSiteKey, 0);
        Array.Clear(rgSiteKey, 0, rgSiteKey.Length);
        return KdfChaCha(uSiteKey64);
    }

    private static bool PolyVerify(byte[] key, byte[] msg, byte[] tag)
    {
        byte[] got = Poly1305(key, msg);
        bool ok = true;
        for (int i = 0; i < 16; i++) ok &= got[i] == tag[i];
        return ok;
    }

    private static byte[] Poly1305(byte[] key, byte[] msg)
    {
        var rgClamp = new byte[16];
        Array.Copy(key, rgClamp, 16);
        rgClamp[3] &= 15; rgClamp[7] &= 15; rgClamp[11] &= 15; rgClamp[15] &= 15;
        rgClamp[4] &= 252; rgClamp[8] &= 252; rgClamp[12] &= 252;
        var r = new BigInteger(rgClamp, isUnsigned: true);
        var s = new BigInteger(key[16..32], isUnsigned: true);
        var bigPrime = (BigInteger.One << 130) - 5;
        var h = BigInteger.Zero;
        for (int i = 0; i < msg.Length; i += 16)
        {
            int n = Math.Min(16, msg.Length - i);
            var rgBlock = new byte[n + 1];
            Array.Copy(msg, i, rgBlock, 0, n);
            rgBlock[n] = 1;
            h = ((h + new BigInteger(rgBlock, isUnsigned: true)) * r) % bigPrime;
        }
        h = (h + s) % (BigInteger.One << 128);
        var tag = new byte[16];
        var rgTagBytes = h.ToByteArray();
        for (int i = 0; i < 16 && i < rgTagBytes.Length; i++) tag[i] = rgTagBytes[i];
        return tag;
    }

    private static void WipeFree(IntPtr pBlock, int n)
    {
        if (pBlock == IntPtr.Zero) return;
        for (int i = 0; i < n; i++) Marshal.WriteByte(pBlock, i, 0);
        Marshal.FreeHGlobal(pBlock);
    }

    private static void WriteU32Hex(IntPtr pDst, int iOff, uint uValue)
    {
        for (int i = 0; i < 8; i++)
        {
            byte c = (byte)("0123456789ABCDEF"[(int)((uValue >> ((7 - i) * 4)) & 0xF)]);
            Marshal.WriteByte(pDst, iOff + i, c);
        }
    }

    private static void DeriveSeedN(byte[] rgIn, IntPtr pOut)
    {
        uint uH1 = 0x811C9DC5, uH2 = 0x15050001, uH3 = 0x1234567, uH4 = 0x9E3779B9;
        for (int k = 0; k < 64; k++)
        {
            byte b = rgIn[(k * 23 + 7) & 63];
            if ((k & 1) == 0) { uH1 ^= b; uH1 *= 0x01000193u; uH3 ^= b; uH3 *= 0x01000193u; }
            else { uH2 ^= b; uH2 *= 0x01000193u; uH4 ^= b; uH4 *= 0x01000193u; }
        }
        WriteU32Hex(pOut, 0, uH1); WriteU32Hex(pOut, 8, uH2);
        WriteU32Hex(pOut, 16, uH3); WriteU32Hex(pOut, 24, uH4);
    }

    private static void SeedKeyN(IntPtr pSeed, IntPtr pKey)
    {
        uint uAccA = uLK0A ^ uLK0B, uAccB = uQ3A ^ uQ3B, uDecoyMix = 0x5A5A5A5Au;
        for (int i = 0; i < 16; i++)
        {
            byte byShard0 = Marshal.ReadByte(pSeed, i), byShard1 = Marshal.ReadByte(pSeed, i + 16);
            uAccA ^= byShard0; uAccA *= (uR1A ^ uR1B);
            uAccB ^= byShard1; uAccB *= (uR2A ^ uR2B);
            uint uWord = (uAccA ^ (uAccB << 1)) + (uAccB ^ (uAccA >> 3));
            byte byCur = (byte)((uWord >> 16) ^ (uWord >> 24) ^ byShard0);
            Marshal.WriteByte(pKey, i, byCur);
            uDecoyMix = (uDecoyMix * 0x9E3779B9u) ^ byCur;
        }
        GC.KeepAlive(uDecoyMix);
    }

    private static uint K1N(IntPtr pSeed, int n)
    {
        uint uHash = uLK0A ^ uLK0B, uDecoyMix = 0x9E3779B9u;
        for (int i = 0; i < n; i++)
        {
            byte byCur = Marshal.ReadByte(pSeed, i);
            uHash ^= byCur; uHash *= uLK1A ^ uLK1B;
            uDecoyMix = (uDecoyMix * 0x01000193u) + byCur;
        }
        GC.KeepAlive(uDecoyMix);
        return uHash;
    }

    private static void GenDisturbN(byte[] rgHeader, IntPtr pKey)
    {
        uint uAccA = 0x6E5A1F2Bu, uAccB = 0x4D7C9E35u;
        for (int k = 0; k < 64; k++)
        {
            byte b = rgHeader[(k * 19 + 11) & 63];
            if ((k & 1) == 0) uAccA = (uAccA ^ b) * 0x85EBCA6Bu;
            else uAccB = (uAccB ^ b) * 0xC2B2AE35u;
        }
        uint uStream = uAccA ^ uAccB;
        for (int i = 0; i < 16; i++)
        {
            uStream = uStream * 0x01000193u + 0x9E3779B9u;
            Marshal.WriteByte(pKey, i, (byte)(uStream >> 24));
        }
    }

    private static uint MxN(IntPtr pKey)
    {
        uint uAccA = (uint)Marshal.ReadInt32(pKey, 0);
        uint uAccB = (uint)Marshal.ReadInt32(pKey, 4);
        uint uMix = uAccA ^ uAccB ^ 0x811C9DC5u;
        GC.KeepAlive((uint)(uAccA + uAccB));
        return uMix != 0 ? uMix : 0x811C9DC5u;
    }

    private static string DecodeNameRot(uint uXorKey, uint[] rgWords, uint uR2A, uint uR2B)
    {
        char[] rgChars = new char[rgWords.Length * 2];
        uint uVerify = (uR2A ^ uR2B) ^ (uint)rgWords.Length;
        for (int i = 0; i < rgWords.Length; i++)
        {
            uint uWord = rgWords[i] ^ (uXorKey + (uR2A ^ uR2B) * (uint)i);
            uWord = (uWord << 13) | (uWord >> 19);
            rgChars[i * 2] = (char)(uWord & 0xFFFF);
            rgChars[i * 2 + 1] = (char)(uWord >> 16);
            uVerify = (uVerify * 0x9E3779B9u) ^ (uWord & 0x1F);
        }
        GC.KeepAlive(uVerify);
        int iLen = Array.IndexOf(rgChars, '\0');
        if (iLen < 0) iLen = rgChars.Length;
        return new string(rgChars, 0, iLen);
    }

    private static byte[] SiteKey()
    {
        byte[] rgBuf = new byte[16];
        BitConverter.GetBytes(_uSiteKeyA).CopyTo(rgBuf, 0);
        BitConverter.GetBytes(_uSiteKeyB).CopyTo(rgBuf, 4);
        BitConverter.GetBytes(_uSiteKeyC).CopyTo(rgBuf, 8);
        BitConverter.GetBytes(_uSiteKeyD).CopyTo(rgBuf, 12);
        return rgBuf;
    }

    private static uint MixKey(byte[] rgKey)
    {
        uint uAccA = rgKey[0] | ((uint)rgKey[1] << 8) | ((uint)rgKey[2] << 16) | ((uint)rgKey[3] << 24);
        uint uAccB = rgKey[4] | ((uint)rgKey[5] << 8) | ((uint)rgKey[6] << 16) | ((uint)rgKey[7] << 24);
        uint uMix = uAccA ^ uAccB ^ 0x811C9DC5u;
        GC.KeepAlive((uint)(uAccA + uAccB));
        return uMix != 0 ? uMix : 0x811C9DC5u;
    }

    //虚拟机自持内存由数据页与代码窗口组成 宿主私有上下文不进虚拟机地址空间
    const int VmMemBytes = 0x2000;
    const int VmDataBytes = 0x1000;
    const int VmRegBytes = 192;
    const int VmCtxOff = 128;
    //内核通用调用 返回长度 只写内核需要时回读整个内存比对 无引擎或入口返回0
    private static uint KernCall(IntPtr pBc, byte[] rgMat, int iOff, byte[] rgMOut)
    {
        if (_fnEngine == null || pBc == IntPtr.Zero) return 0;
        IntPtr pRegs = Marshal.AllocHGlobal(VmRegBytes);
        IntPtr pMem = Marshal.AllocHGlobal(VmMemBytes);
        uint uRet;
        try
        {
            for (int i = 0; i < VmRegBytes; i++) Marshal.WriteByte(pRegs, i, 0);
            for (int i = 0; i < VmMemBytes; i++) Marshal.WriteByte(pMem, i, 0);
            Marshal.WriteInt64(pRegs, 2 * 8, 0L);//v2=i
            Marshal.WriteInt64(pRegs, 3 * 8, rgMat.Length);//v3=len
            for (int i = 0; i < rgMat.Length && iOff + i < VmDataBytes; i++) Marshal.WriteByte(pMem, iOff + i, rgMat[i]);
            //私有上下文不进虚拟机内存 依次存放流密钥与引擎代码基址与代码长度 代码被拷到映射基址偏移零处
            Marshal.WriteInt32(pRegs, VmCtxOff, unchecked((int)_uRollKey));
            Marshal.WriteInt64(pRegs, VmCtxOff + 8, _engineBase.ToInt64());
            Marshal.WriteInt32(pRegs, VmCtxOff + 16, _engineTextSz);
            uRet = (uint)_fnEngine(pBc, pRegs, pMem, VmMemBytes);
            if (rgMOut != null) Marshal.Copy(pMem, rgMOut, 0, Math.Min(rgMOut.Length, VmMemBytes));
        }
        finally
        {
            WipeFree(pRegs, VmRegBytes);//连私有上下文一起擦 释放前擦 WipeFree内部已含FreeHGlobal
            WipeFree(pMem, VmMemBytes);
        }
        return uRet;
    }
    private static uint KernCall(IntPtr pBc, byte[] rgMat) => KernCall(pBc, rgMat, 0, null!);

    //密钥派生的VM版本算法在首个内核里由引擎执行 缺失就放弃
    private static uint JitKeyVm(byte[] rgSeed)
    {
        if (rgSeed == null || rgSeed.Length == 0) return 0;//内核按v3计次空输入直接拒绝
        return KernCall(_pRollKern[KI_JIT], rgSeed);
    }

    //签名表掩码派生的VM版本在第二内核 缺失就不注入签名表
    private static uint MaskVm(byte[] rgMat)
    {
        if (rgMat == null || rgMat.Length == 0 || rgMat.Length > VmDataBytes) return 0;
        if (_pRollKern[KI_MASK] == IntPtr.Zero) return 0;
        return KernCall(_pRollKern[KI_MASK], rgMat);
    }

    private static uint Mix32(uint u)
    {
        u ^= u >> 16; u *= (uLLCA ^ uLLCB);
        u ^= u >> 13; u *= (uLQCA ^ uLQCB);
        u ^= u >> 16;
        return u;
    }

    private static void DerivePermAt(uint uXorKey, int i, int[] rgPerm)
    {
        rgPerm[0] = 0; rgPerm[1] = 1; rgPerm[2] = 2; rgPerm[3] = 3;
        uint uMixA = Mix32(KeyAt(uXorKey, i + 1)), uMixB = uXorKey ^ 0x5A5A5A5Au;
        for (int k = 3; k > 0; k--)
        {
            uMixA = uMixA * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            uMixB = (uMixB * 0x01000193u) + (uMixA >> 8);
            int j = (int)(uMixA % (uint)(k + 1));
            (rgPerm[k], rgPerm[j]) = (rgPerm[j], rgPerm[k]);
        }
        GC.KeepAlive(uMixB);
    }

    private static uint KeyAt(uint uXorKey, int i) => uXorKey + (uLGAA ^ uLGAB) * (uint)i;

    private static uint FieldKeyAt(uint uXorKey, int i, int iS) => Mix32(KeyAt(uXorKey, i * 4 + iS + 1));

    private static uint ReadU32(byte[] rgBytes, int iOff) => BitConverter.ToUInt32(rgBytes, iOff);

    [STAThread]
    private static int Main(string[] rgArgs) => RunBoot(rgArgs);

    private static int RunBoot(string[] rgArgs)
    {
        try
        {
            if (uLK0A == 0xDEADBEEFu) Environment.FailFast(null);
            DecoyArgs(rgArgs);
            byte[] rgBundle;
            List<(string, uint, uint, uint)> rgEntries;

            AD();
            _llTick = PerfTick();
#if LAMARR_TRIAGE
            bool bNoMon = Environment.GetEnvironmentVariable("LAMARR_NO_MON") == "1";
#else
            bool bNoMon = false;
#endif
#if LAMARR_TRIAGE
            if (Environment.GetEnvironmentVariable("LAMARR_BOOT_EXC") == "1")
            {
                int iSeen = 0;
                AppDomain.CurrentDomain.FirstChanceException += (o, e) =>
                {
                    if (Interlocked.Increment(ref iSeen) > 200) return;
                    try { DiagBoot.Log("exc: " + e.Exception.GetType().FullName + " :: " + e.Exception.Message + " @ " + e.Exception.StackTrace); } catch { }
                };
                DiagBoot.LogStage(1);//首次机会异常日志开启
            }
#endif
            DiagBoot.Log("rt: " + Environment.Version + " tiered: " + (IsTieredOff() ? "off" : "on") + " env=" + (Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<null>") + " mark=" + TierMark());
            var thMonitor = new Thread(() =>
            {
                try { IntPtr hThread = OpenThread(0x0040u, false, GetCurrentThreadId()); if (hThread != IntPtr.Zero) { NtSetInformationThread(hThread, 0x11, IntPtr.Zero, 0); CloseHandle(hThread); } } catch { }
        //就绪前不得进入解密校验路径 两条校验会走到加密体 所以先查就绪标志
        for (; ; ) { try { if (_iIntegFail != 0) { DiagBoot.LogGuard(1); Environment.FailFast(null); } WipeWhenJitQuiet(); CheckJitAfterWipe(); AD(); LogJitTrace(); AntiFreezeTick(); TickGuard(_llTick, 10000L, 60000L); _llTick = PerfTick(); _iFanTick++; if (_iFanTick > 40 && (_iFanTick & 7) == 0) { if (!FanCheck()) { DiagBoot.LogGuard(2); WreckAndFailLater(); } } if ((_uAdTick & 0x3Fu) == 0 && _bJitReady) { JitVerify(); DecVerify(); } if ((_uAdTick & 0x3Fu) == 0 && _bJitReady) VerifyBodies(); if ((_uAdTick & 0x3Fu) == 0 && _fnJitVerifyHook != null) { int iVf = _fnJitVerifyHook(); if ((iVf & 0xFF) != 0)
                {
#if LAMARR_TRIAGE
                DiagBoot.LogGuard(3, "=" + iVf);
#else
                DiagBoot.Log("guard: g17=" + iVf);//发行态用同一套判断 只写编号 不写可读文本
#endif
                Environment.FailFast(null);
                }
                else if (iVf != 0)
                {
#if LAMARR_TRIAGE
                DiagBoot.Log("jithook: variant retry recovered " + ((iVf >> 8) & 0xFF) + " body decrypt(s), none-plausible " + ((iVf >> 16) & 0xFF));
#else
                DiagBoot.Log("guard: g18=" + ((iVf >> 8) & 0xFF));
#endif
                } } } catch { } Thread.Sleep(120); }
            }) { IsBackground = true };
            if (!bNoMon) thMonitor.Start();
            SelfCheck();
            DiagBoot.LogStage(2);
            //启动期一次性预算放宽 冷启动与首次编译在慢机器上可能超过10秒
            CheckElapsed(_llTick, 30000L);

            if ((RuntimeSeed() & 0x20000000u) != 0)
            {
                uint uDecoy1 = (uint)Environment.TickCount ^ (uQ1A ^ uQ1B);
                byte[] rgBundleAlt = X6();
                GC.KeepAlive(uDecoy1);
                rgBundle = rgBundleAlt;
            }
            else
            {
                uint uDecoy2 = RuntimeSeed() ^ (uQ2A ^ uQ2B);
                rgBundle = X6();
                GC.KeepAlive(uDecoy2);
            }
            if (rgBundle.Length < 128)
                goto QF;
            DecoyHashA(rgBundle);
            GC.KeepAlive(DecoyHashB(rgBundle));
            AD();
            AD();//解bundle前复检
            long llParse = PerfTick();
            if (!X1(rgBundle, out rgEntries))
                goto QF;
            DiagBoot.LogStage(3);
            PatchRegionHeader(rgBundle);
            HeadWipeCheck(rgBundle);
            AD();//载入原生解码器前
            EnsureDecoder(rgBundle, rgEntries);
            DiagBoot.LogStage(4);
            AD();//安装jithook前
            EnsureJitHook(rgBundle, rgEntries);
            AD();//载入引擎与roll前 密钥材料就位前再查一次
            EnsureEngine(rgBundle, rgEntries);
            DiagBoot.LogStage(5);
            EnsureRoll(rgBundle, rgEntries);
            HeadWipeCheck(rgBundle);//第二个站点 防止中途有代码把头部材料复制回来
            DiagBoot.LogStage(6);
            if (!RollSelfCheck())
            {
                //引擎与内核都已装起却对不上固定向量时按完整性失败处理 环境类仍走诱饵路径 缺运行库的机器正常退出
                bool bEngineLive = _engineOk && _fnEngine != null && _pRoll != IntPtr.Zero
                    && _pRollKern[KI_JIT] != IntPtr.Zero && _pRollKern[KI_MASK] != IntPtr.Zero;
#if LAMARR_TRIAGE
                if (Environment.GetEnvironmentVariable("LAMARR_NO_INTEG_FAIL") == "1") bEngineLive = false;
#endif
                if (bEngineLive)
                {
                    DiagBoot.LogGuard(4);
                    WreckAndFail();
                }
                DiagBoot.LogStage(7);
                goto QF;
            }
            DiagBoot.LogStage(8);
            InjectSigs(rgBundle, rgEntries);
            if (_fnJitInstall != null)
            {
                if (_fnEngine == null || _pRoll == IntPtr.Zero) goto QF;//无明文回退 VM内核必备
                byte[] rgJitKey = SiteKey();
                uint uXorKey = JitKeyVm(rgJitKey);
                if (uXorKey == 0u) goto QF;
                _uRollKey = 0;//引擎不会再被调用 roll密钥就地清零 不留静态残留
                //每次运行重新做密钥切分 不用新式随机源 因为引导要能编到net5
                _fnJitSetKey(uXorKey, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0));
                Array.Clear(rgJitKey, 0, rgJitKey.Length);
                if (_fnJitSetAdFlag != null) _fnJitSetAdFlag(uV7A ^ uV7B);
                ApplyJitSlots();
                //轨迹环始终映射 环偏移12是编译活动计数器 抹除时机依赖它 记录数在上限处冻结
                _bTraceDump = false;
#if LAMARR_TRIAGE
                _bTraceDump = Environment.GetEnvironmentVariable("LAMARR_TRACE") == "1";
#endif
                _pTrace = IntPtr.Zero;
                if (_bTraceDump)
                    _pTrace = MapTraceRing(Environment.GetEnvironmentVariable("TEMP") + "\\lamarr_ring.bin", 16 + 8192 * 16 + 128);
                if (_pTrace == IntPtr.Zero)
                {
                    _pTrace = Marshal.AllocHGlobal(16 + 8192 * 16 + 128);
                    for (int i = 0; i < 16 + 8192 * 16 + 128; i++) Marshal.WriteByte(_pTrace, i, 0);
                    if (_bTraceDump)
                    {
                        Marshal.WriteInt32(_pTrace, 0, 1000);
                        Marshal.WriteInt32(_pTrace, 4, 0x5EED0001);
                        Marshal.WriteInt32(_pTrace, 16, 1001);
                        Marshal.WriteInt32(_pTrace, 20, unchecked((int)0x5EED0002u));
                        Marshal.WriteInt32(_pTrace, 24, 8);
                        Marshal.WriteInt32(_pTrace, 8, 2);
                    }
                }
                DiagBoot.LogStage(9, "=" + (_bTraceDump ? "on" : "quiet") + " ring=" + (_pTrace == IntPtr.Zero ? "off" : "on") + " env=" + TraceEnv());
                _fnJitInstall(IsTieredOff() ? 1 : 0, _pTrace); //惰性抹除与缓存 只在每个方法只编译一次时才安全
                DiagBoot.LogStage(10);
#if LAMARR_TRIAGE
            if (Environment.GetEnvironmentVariable("LAMARR_SELFDUMP") == "1" || Environment.GetEnvironmentVariable("LAMARR_SELFDUMP") == "pss")
            {
                //按开关走转储库或快照库 两条进程内取样路径都要处理
                string sLib = Environment.GetEnvironmentVariable("LAMARR_SELFDUMP") == "pss" ? "pssapi.dll" : "dbgcore.dll";
                IntPtr hDbg = LoadLibraryW(sLib);
                if (hDbg == IntPtr.Zero && sLib == "dbgcore.dll") hDbg = LoadLibraryW("dbghelp.dll");
                DiagBoot.Log("probe: selfdump lib=" + sLib + " " + (hDbg == IntPtr.Zero ? "not-loaded" : "loaded"));
                if (DumpModuleLoaded()) SampleRespond("sample-dump");
            }
#endif
                if (_bTraceDump) StartTraceDumper();
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                WarmExitPath();
                _bJitReady = true;
            }
            if ((RuntimeSeed() & 0x10000000u) == 0)
            {
                uint uDecoy3 = (uint)Process.GetCurrentProcess().Id ^ (uQ3A ^ uQ3B);
                W1(rgBundle, rgEntries);
                GC.KeepAlive(uDecoy3);
            }
            else
            {
                uint uDecoy4 = (uint)Environment.TickCount ^ (uQ4A ^ uQ4B);
                W1(rgBundle, rgEntries);
                GC.KeepAlive(uDecoy4);
            }
            AD();//解主程序明文前
            byte[] rgMain = X3(rgBundle, rgEntries[0], true);
            DiagBoot.LogStage(11, ", len=" + rgMain.Length + " ms=" + ElapsedMs(llParse) + " budgetX=" + BudgetX());
            //载荷区解密与装载的一次性预算同样放宽 慢机器或重负载下8秒偏紧
            TCheck(llParse, 20000L);
            Assembly asm = X4(AssemblyLoadContext.Default, rgMain);
            Array.Clear(rgMain, 0, rgMain.Length);

            MethodInfo? miEntry = asm.EntryPoint;
            if (miEntry == null) goto QF;
            //清头推迟到监视线程 界面初始化会读宿主模块信息 启动期就清会让载荷起不来
            DiagBoot.LogStage(12, " " + miEntry.Name);
            object[] rgParams = miEntry.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { rgArgs };
            int iExit = miEntry.Invoke(null, rgParams) is int r ? r : 0;
            _iPayloadExit = iExit;
            //载荷已经返回 清头与自终止都在这里做 让运行时走它自己的关闭流程会崩溃或挂住
            FinishAndTerminate(iExit);
            return iExit;
        QF:
            DiagBoot.LogStage(13);
            return DecoyStackFill();
        }
        catch (Exception ex)
        {
            DiagBoot.Log("RunBoot catch: " + ex);
            return DecoyStackFill();
        }
    }

    //载荷返回后的末尾做安全级与签名级清头并立刻自行终止 正常路径已在返回处完成
    private static void OnProcessExit(object? oSender, EventArgs eArgs) => FinishAndTerminate(_iPayloadExit);

    private static void FinishAndTerminate(int iExitCode)
    {
        WipeHeadersSafe();
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_WIPE_EXIT") == "0") return;
#endif
        if (_sWipeQuiet == "1") return;//静默窗口模式已经在运行期抹过签名级
        WipeSelfHeaders(true);
        int iCode = Environment.ExitCode;
        TerminateProcess(GetCurrentProcess(), (uint)(iCode != 0 ? iCode : iExitCode));
    }

    //退出路径上的代码必须在运行期就编译好 否则末尾期首次编译会失败 平台调用桩也先用无损方式各走一遍
    private static void WarmExitPath()
    {
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_NO_WARM") == "1") return;
#endif
        try
        {
            foreach (var mi in new[]
            {
                typeof(P).GetMethod(nameof(OnProcessExit), BindingFlags.Static | BindingFlags.NonPublic),
                typeof(P).GetMethod(nameof(FinishAndTerminate), BindingFlags.Static | BindingFlags.NonPublic),
                typeof(P).GetMethod(nameof(WipeHeadersSafe), BindingFlags.Static | BindingFlags.NonPublic),
                typeof(P).GetMethod(nameof(WipeSelfHeaders), BindingFlags.Static | BindingFlags.NonPublic),
            })
                if (mi != null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(mi.MethodHandle);
            Action a1 = static () => { };
            Action a2 = static () => { };
            (a1 + a2)();
        }
        catch { }
        try
        {
            IntPtr h = GetModuleHandle(null);
            GC.KeepAlive(GetCurrentProcess());
            if (h != IntPtr.Zero && VirtualProtect(h, (UIntPtr)0x1000, 0x04u, out uint uOld))
            {
                if (_iSelfLfanew <= 0) _iSelfLfanew = Marshal.ReadInt32(h, 0x3C);
                Marshal.WriteByte(h, 0, Marshal.ReadByte(h, 0));//同一字节写回只为了走通路径
                VirtualProtect(h, (UIntPtr)0x1000, uOld, out _);
            }
        }
        catch { }
        DiagBoot.LogStage(14);
    }

    private static uint RuntimeSeed() => unchecked((uint)Environment.TickCount ^ (uint)Process.GetCurrentProcess().Id);

    //清掉宿主镜像自身的内存头部 元数据在节内还能用 分两级 安全级在载荷运行中做 签名级只能在退出路径做
    private static int _iSelfLfanew;
    private static int _iSelfWiped;
    private static int _iPayloadExit;//载荷Main的返回值 退出时的清头与自终止拿它当进程退出码
    private static int _iSelfSafeWiped;
    private static int _iMonTicks;
    private static int _iMonUp;
    private static bool _bTraceDump;
    private static int _iJitSeen;
    private static int _iJitQuiet;
#if LAMARR_TRIAGE
    private static readonly string _sWipeQuiet = Environment.GetEnvironmentVariable("LAMARR_WIPE_QUIET") ?? "";
#else
    private static readonly string _sWipeQuiet = "";
#endif
    private static string TierMark()
    {
#if LAMARR_TRIAGE
        return Environment.GetEnvironmentVariable("LAMARR_TIER_MARK") ?? "<null>";
#else
        return "<null>";
#endif
    }
    private static string TraceEnv()
    {
#if LAMARR_TRIAGE
        return Environment.GetEnvironmentVariable("LAMARR_TRACE") ?? "<null>";
#else
        return "<null>";
#endif
    }
    private static bool PayloadWindowUp()
    {
        try
        {
            using (Process p = Process.GetCurrentProcess())
            {
                p.Refresh();
                return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0;
            }
        }
        catch { return false; }
    }
    private static void WipeHeadersSafe()
    {
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_NO_WIPE") == "1") return;
#endif
        if (_iSelfSafeWiped >= 1) return;
        try
        {
            IntPtr h = GetModuleHandle(null);
            if (h == IntPtr.Zero) return;
            if (_iSelfLfanew <= 0) _iSelfLfanew = Marshal.ReadInt32(h, 0x3C);
            int iPe = _iSelfLfanew;
            if (iPe <= 0x40 || iPe > 0x1000) return;
            if (!VirtualProtect(h, (UIntPtr)0x1000, 0x04u, out uint uOld)) return;//PAGE_READWRITE
            DiagBoot.LogStage(15);//在写之前记录其后任何JIT编译都不安全
            for (int i = 0x40; i < iPe; i++) Marshal.WriteByte(h, i, 0);//DOS stub含Rich头
            for (int i = 0; i < 4; i++) Marshal.WriteByte(h, iPe + 8 + i, 0);//TimeDateStamp
            for (int i = 0; i < 4; i++) Marshal.WriteByte(h, iPe + 0x58 + i, 0);//CheckSum
            VirtualProtect(h, (UIntPtr)0x1000, uOld, out _);
            _iSelfSafeWiped = 1;
        }
        catch { }
    }

    private static void WipeSelfHeaders(bool bFull)
    {
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_NO_WIPE") == "1") return;
#endif
        try
        {
            if (_iSelfWiped >= (bFull ? 2 : 1)) return;
            IntPtr h = GetModuleHandle(null);
            if (h == IntPtr.Zero) return;
            if (_iSelfLfanew <= 0) _iSelfLfanew = Marshal.ReadInt32(h, 0x3C);
            if (!VirtualProtect(h, (UIntPtr)0x1000, 0x04u, out uint uOld)) return;//PAGE_READWRITE
            DiagBoot.LogStage(16, " (level=" + (bFull ? 2 : 1) + ")");//在写之前之后的编译会读到清零的签名
            if (bFull)
            {
                for (int i = 0; i < 0x40; i++) Marshal.WriteByte(h, i, 0);
                int iPe = _iSelfLfanew;
                if (iPe > 0x40 && iPe + 4 <= 0x1000) for (int i = 0; i < 4; i++) Marshal.WriteByte(h, iPe + i, 0);
                _iSelfWiped = 2;
            }
            else
            {
                for (int i = 0; i < 0x40; i++) Marshal.WriteByte(h, i, 0);//DOS头含MZ
                int iPe = _iSelfLfanew;
                if (iPe > 0x40 && iPe + 4 <= 0x1000) for (int i = 0; i < 4; i++) Marshal.WriteByte(h, iPe + i, 0);//PE\0\0签名
                _iSelfWiped = 1;
            }
            VirtualProtect(h, (UIntPtr)0x1000, uOld, out _);
            if (Marshal.ReadByte(h, 0) != 0 || Marshal.ReadByte(h, 1) != 0) Environment.FailFast(null);
        }
        catch { }
    }

    //编译活动计数由钩子每次编译自增 记录计数有上限 不能用于此处
    private static int JitEvents() => _pTrace == IntPtr.Zero ? 0 : Marshal.ReadInt32(_pTrace, 12);
    private const int JitQuietTicks = 60;   //60 * 120ms = ~7秒内一个JIT事件都没有才认为后台编译停了

    //运行期静默窗口 抹签名级头部后120毫秒内必然出现一次编译 随即的检查会决定是否快速失败
    private static bool WipeQuietForced() => _sWipeQuiet == "1";

    private static void WipeWhenJitQuiet()
    {
        ++_iMonTicks;
        ++_iMonUp;
        if (_iSelfSafeWiped == 0 && _iMonTicks > 25 && (_iMonUp > 100 || PayloadWindowUp())) WipeHeadersSafe();
        if (_iSelfWiped >= 2) return;
        if (!WipeQuietForced()) return;
        if (_iMonTicks <= 25) return;
        int iNow = JitEvents();
        //低频诊断只显示编译频率 安静窗口取决于负载 繁忙的应用到不了那里 签名层保持未抹除
        if ((_iMonTicks & 0x7F) == 0) DiagBoot.Log("stat: jit events=" + iNow + " quiet=" + _iJitQuiet + "/" + JitQuietTicks + " wiped=" + _iSelfWiped);
        if (iNow != _iJitSeen) { _iJitSeen = iNow; _iJitQuiet = 0; return; }
        if (_iMonUp <= 100 && !PayloadWindowUp()) return;
        if (++_iJitQuiet < JitQuietTicks) return;
        WipeSelfHeaders(true);
    }

    private static void CheckJitAfterWipe()
    {
        if (_iSelfWiped < 2) return;
        int iNow = JitEvents();
        if (iNow == _iJitSeen) return;
        DiagBoot.LogGuard(5, " (" + _iJitSeen + "->" + iNow + ")");
        Environment.FailFast(null);
    }

    //完整性失败不都立刻快速失败 也可以只毁状态并置延迟标志 毁掉状态后密钥与内核顺序都已破坏
    private static int _iIntegFail;
    private static void WreckState()
    {
        try { if (_fnJitSetKey != null) _fnJitSetKey(0UL, 0UL); } catch { }
        try { for (int i = 0; i < _pRollKern.Length; i++) _pRollKern[i] = IntPtr.Zero; } catch { }
        try { _uSiteKeyA = _uSiteKeyB = _uSiteKeyC = _uSiteKeyD = 0u; } catch { }
        try { if (_pTrace != IntPtr.Zero) for (int i = 0; i < 16; i++) Marshal.WriteByte(_pTrace, i, 0); } catch { }
        _bJitReady = false;
    }
    private static void WreckAndFail() { WreckState(); Environment.FailFast(null); }
    private static void WreckAndFailLater() { WreckState(); _iIntegFail = 1; }

    private static void AD()
    {
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_NO_AD") == "1") return;
#endif
        if (uLQCA == 0xFEEDFACEu) Environment.FailFast(null);
        uint uAdTick = ++_uAdTick;
        //检测照常按时调用但不看结果 便于定位调用期间的崩溃
#if LAMARR_TRIAGE
        bool bAdIdle = Environment.GetEnvironmentVariable("LAMARR_AD_IDLE") == "1";
#else
        bool bAdIdle = false;
#endif
        bool bDebugger = IsDebuggerPresent();
        if (!bDebugger && HasHardwareBreakpoint())
            bDebugger = true;
        if (!bDebugger && (uAdTick & 0x0Fu) == 0 && HasDebuggerDllLoaded())
            bDebugger = true;
        if ((uAdTick & 0x03u) == 0 && DumpModuleLoaded()) SampleRespond("sample-dump");
        if ((uAdTick & 0x3Fu) == 0 && LocalDumpsConfigured()) SampleRespond("sample-localdumps");
        //诱饵只返回噪音值 不参与判定 当成依据会误伤普通桌面 所以保留调用丢掉结果
        if (!bDebugger && (uAdTick & 0x0Fu) == 0) GC.KeepAlive(DecoyScanWindows());
        if (!bDebugger)
        {
            IntPtr pDebugPort = IntPtr.Zero;
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 7, out pDebugPort, IntPtr.Size, out _) == 0 && pDebugPort != IntPtr.Zero)
                bDebugger = true;
        }
        if (!bDebugger && IsPebBeingDebugged())
            bDebugger = true;
        if (!bDebugger && DetectProfilerEnv())
            bDebugger = true;
        if (!bDebugger && (uAdTick & 0x07u) == 0 && (Environment.TickCount & 1) == 0)
            GC.KeepAlive((uint)Environment.TickCount ^ uAdTick);
        if (!bDebugger)
        {
            bool bRemoteDbg = false;
            if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref bRemoteDbg) && bRemoteDbg)
                bDebugger = true;
        }
        if (!bDebugger && (uAdTick & 0x3Fu) == 0 && _uX1Hash != 0 && MethodHash(_dgX1) != _uX1Hash)
        { DiagBoot.LogGuard(6, " got=" + MethodHash(_dgX1).ToString("X8") + " want=" + _uX1Hash.ToString("X8")); bDebugger = true; }
        if (bDebugger && !bAdIdle)
        { DiagBoot.LogGuard(7); Environment.FailFast(null); }
    }

    private static bool HasHardwareBreakpoint()
    {
        try
        {
            IntPtr hThread = OpenThread(0x0048u, false, GetCurrentThreadId());
            if (hThread == IntPtr.Zero) return false;
            try
            {
                var ctx = new CONTEXT();
                ctx.ContextFlags = 0x00100010u;
                if (!GetThreadContext(hThread, ref ctx)) return false;
                if ((ctx.Dr7 & 0xFFFFFF00) != 0) GC.KeepAlive(ctx.Dr6);
                if (ctx.Dr0 != 0 || ctx.Dr1 != 0 || ctx.Dr2 != 0 || ctx.Dr3 != 0 || (ctx.Dr7 & 0xFF) != 0)
                    return true;
            }
            finally { CloseHandle(hThread); }
        }
        catch { }
        return false;
    }

    private static bool IsPebBeingDebugged()
    {
        try
        {
            byte[] rgBasicInfo = new byte[16];
            if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, rgBasicInfo, rgBasicInfo.Length, out _) != 0) return false;
            long lPeb = BitConverter.ToInt64(rgBasicInfo, 8);
            if (lPeb == 0) return false;
            GC.KeepAlive(lPeb ^ BitConverter.ToInt64(rgBasicInfo, 0));
            return Marshal.ReadByte(new IntPtr(lPeb), 2) != 0;
        }
        catch { }
        return false;
    }

    private static bool DetectProfilerEnv()
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

    //纯噪音枚举窗口 永不快速失败
    private static bool DecoyScanWindows()
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
    private static bool HasDebuggerDllLoaded()
    {
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                string s = m.ModuleName;
                if (s.Length == 0) GC.KeepAlive(s);
                for (int i = 0; i < rgDebugDllNames.Length; i++)
                {
                    if (string.Equals(s, rgDebugDllNames[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static int _iSampleHits;
    private static void SampleRespond(string sTag)
    {
        if (++_iSampleHits >= 2) { DiagBoot.Log("guard: " + sTag + "-2"); Environment.FailFast(null); }
        DiagBoot.Log("guard: " + sTag);
        WreckState();
    }

    //模块面取样库一旦映射进本进程即命中
    private static bool DumpModuleLoaded()
    {
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                string s = m.ModuleName;
                for (int i = 0; i < rgDumpDllNames.Length; i++)
                    if (string.Equals(s, rgDumpDllNames[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }
        return false;
    }

    //注册表项指向本程序时 现场崩溃会被写成全量转储 开发机上很常见 只记一行不当依据
    private static bool LocalDumpsConfigured()
    {
        IntPtr hRoot = new IntPtr(unchecked((int)0x80000002));
        IntPtr hKey = IntPtr.Zero;
        IntPtr hSub = IntPtr.Zero;
        try
        {
            if (RegOpenKeyExW(hRoot, sLocalDumpsKey, 0u, 0x20019u, out hKey) != 0) return false;
            string sExe = Process.GetCurrentProcess().ProcessName + ".exe";
            if (RegOpenKeyExW(hKey, sExe, 0u, 0x20019u, out hSub) == 0) return true;
            DiagBoot.Log("note: HKLM LocalDumps present (global)");
        }
        catch { }
        finally
        {
            if (hSub != IntPtr.Zero) RegCloseKey(hSub);
            if (hKey != IntPtr.Zero) RegCloseKey(hKey);
        }
        return false;
    }

    //抗冻结 进程被挂起表现为拍间隔变长 依据取采样亏空 单次回收或换页不判 超长间隔按休眠处理只记一行
    private static ulong _ullWallPrev;
    private static ulong _ullWallWinStart;
    private static int _iGapBig;
    private static long _llQpcPrev;
    private static void AntiFreezeTick()
    {
        ulong ullWall = GetTickCount64();
        long llQpc = PerfTick();
        if (_ullWallPrev == 0) { _ullWallPrev = ullWall; _ullWallWinStart = ullWall; _llQpcPrev = llQpc; return; }
        ulong ullGap = ullWall - _ullWallPrev;
        _ullWallPrev = ullWall;
        long llGapQpc = ElapsedMs(_llQpcPrev);
        _llQpcPrev = llQpc;
        //两源同量级却差得远说明时钟被调过 记一行不判
        if ((long)ullGap > llGapQpc + 500L || llGapQpc > (long)ullGap + 500L)
            DiagBoot.Log("note: clock skew wall=" + ullGap + " qpc=" + llGapQpc);
        if (ullGap > 30000UL)
        {
            DiagBoot.Log("note: long gap " + ullGap + "ms (treated as machine sleep)");
            _iGapBig = 0; _ullWallWinStart = ullWall;
            return;
        }
        if (ullGap > 4000UL) _iGapBig += 2;
        else if (ullGap > 1200UL) _iGapBig++;
        if (ullWall - _ullWallWinStart < 8000UL) return;
        _ullWallWinStart = ullWall;
        int iBig = _iGapBig;
        _iGapBig = 0;
        if (iBig >= 2) SampleRespond("sample-freeze");
    }

    private static long PerfTick() { QueryPerformanceCounter(out long llTick); return llTick; }
    private static bool IsTieredOff()
    {
        string sE = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "";
        if (sE == "0" || sE.Equals("false", StringComparison.OrdinalIgnoreCase)) return true;
        if (sE == "1" || sE.Equals("true", StringComparison.OrdinalIgnoreCase)) return false;
        if (AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out bool b)) return !b;
        return false;
    }
    private static long ElapsedMs(long llStart)
    {
        QueryPerformanceCounter(out long llEnd);
        QueryPerformanceFrequency(out long llFreq);
        return llFreq > 0 ? (llEnd - llStart) * 1000 / llFreq : 0;
    }
    private static int _iSlowTick;
    //活性依据单次卡顿不算 软阈值内连续三次超限才判 硬阈值以上直接判 记录写进启动期日志
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr lpAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, IntPtr dwNumberOfBytesToMap);
    //用文件映射承载追踪轨迹环 使记录能在编译线程异常中存活
    private static IntPtr MapTraceRing(string sPath, int cb)
    {
        try
        {
            using (var fs = new System.IO.FileStream(sPath, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite))
            {
                IntPtr hMap = CreateFileMappingW(fs.SafeFileHandle.DangerousGetHandle(), IntPtr.Zero, 4u, 0u, (uint)cb, null!);
                if (hMap == IntPtr.Zero) return IntPtr.Zero;
                IntPtr p = MapViewOfFile(hMap, 0xF001Fu, 0u, 0u, new IntPtr(cb));
                if (p != IntPtr.Zero) for (int i = 0; i < cb; i++) Marshal.WriteByte(p, i, 0);
                return p;
            }
        }
        catch { return IntPtr.Zero; }
    }
    private static void StartTraceDumper()
    {
        Thread th = new Thread(delegate () { for (; ; ) { try { LogJitTrace(); } catch { } Thread.Sleep(150); } });
        th.IsBackground = true;
        th.Start();
    }
    private static void LogJitTrace()
    {
        if (!_bTraceDump || _pTrace == IntPtr.Zero) return;
        int iN = Marshal.ReadInt32(_pTrace, 8);
        if (iN <= _iTraceLogged) return;
        for (int i = _iTraceLogged; i < iN && i < 8192; i++)
        {
            int o = 16 + i * 16;
            DiagBoot.Log(string.Format("jit[{0}] ev={1} crc={2:X8} size={3} il=0x{4:X8}",
                i, Marshal.ReadInt32(_pTrace, o), (uint)Marshal.ReadInt32(_pTrace, o + 4),
                Marshal.ReadInt32(_pTrace, o + 8), (uint)Marshal.ReadInt32(_pTrace, o + 12)));
        }
        _iTraceLogged = iN;
    }
    private static void TickGuard(long llStart, long llMsSoft, long llMsHard)
    {
        long llMs = ElapsedMs(llStart);
        long llSoft = llMsSoft * BudgetX();
        long llHard = llMsHard * BudgetX();
        if (llMs <= llSoft) { _iSlowTick = 0; return; }
        if (llMs > llHard || ++_iSlowTick >= 3) { DiagBoot.LogGuard(8, $" ms={llMs} hard={llHard} tick={_iSlowTick}"); Environment.FailFast(null); }
    }
    //分层编译下大方法首次执行会慢数倍 时间预算按系数放宽 否则合法的慢会被误判
    private static int _iBudgetX;
    private static int BudgetX()
    {
        if (_iBudgetX == 0)
        {
            //固定尺度使预算假设可独立于分层开关检验
#if LAMARR_TRIAGE
            string sX = Environment.GetEnvironmentVariable("LAMARR_BUDGET_X") ?? "";
#else
            string sX = "";
#endif
            int iX;
            if (int.TryParse(sX, out iX) && iX > 0) _iBudgetX = iX;
            else _iBudgetX = IsTieredOff() ? 1 : 6;
        }
        return _iBudgetX;
    }
    private static void TCheck(long llStart, long llMsLimit)
    {
        if (uKS0A == 0xDEADBEEFu) Environment.FailFast(null);

        QueryPerformanceCounter(out long llEnd);
        QueryPerformanceFrequency(out long llFreq);
        if (llFreq > 0 && (llEnd - llStart) * 1000 / llFreq > llMsLimit * BudgetX())
        { DiagBoot.LogGuard(9); Environment.FailFast(null); }
    }
    private static void CheckElapsed(long llStart, long llMsLimit)
    {
        if (uQ1A == 0xBADF00Du) Environment.FailFast(null);

        QueryPerformanceCounter(out long llEnd);
        QueryPerformanceFrequency(out long llFreq);
        if (llFreq > 0 && (llEnd - llStart) * 1000 / llFreq > llMsLimit * BudgetX())
        { DiagBoot.LogGuard(10); Environment.FailFast(null); }
    }
    private static void SelfCheck()
    {
        if (uLK0A == 0xCAFEBABEu) Environment.FailFast(null);

        if ((uKS0A ^ uKS0B ^ uV1A ^ uV1B) != 0u) { DiagBoot.Log("guard: sc-k0"); Environment.FailFast(null); }
        if ((uKS1A ^ uKS1B ^ uV2A ^ uV2B) != 0u) { DiagBoot.Log("guard: sc-k1"); Environment.FailFast(null); }
        if ((uKS2A ^ uKS2B ^ uV3A ^ uV3B) != 0u) { DiagBoot.Log("guard: sc-k2"); Environment.FailFast(null); }
        if ((uKS3A ^ uKS3B ^ uV4A ^ uV4B) != 0u) { DiagBoot.Log("guard: sc-k3"); Environment.FailFast(null); }
        if ((uLK0A ^ uLK0B ^ uV5A ^ uV5B) != 0u) { DiagBoot.Log("guard: sc-l0"); Environment.FailFast(null); }
        if ((uLK1A ^ uLK1B ^ uV6A ^ uV6B) != 0u) { DiagBoot.Log("guard: sc-l1"); Environment.FailFast(null); }
        if (MethodHash(_dgAd) != (uHs ^ Exp(ExpSlot.Ad))) { DiagBoot.Log($"guard: sc-ad got={MethodHash(_dgAd):X8} want={(uHs ^ Exp(ExpSlot.Ad)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX1) != (uHs ^ Exp(ExpSlot.X1))) { DiagBoot.Log($"guard: sc-x1 got={MethodHash(_dgX1):X8} want={(uHs ^ Exp(ExpSlot.X1)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX3) != (uHs ^ Exp(ExpSlot.X3))) { DiagBoot.Log($"guard: sc-x3 got={MethodHash(_dgX3):X8} want={(uHs ^ Exp(ExpSlot.X3)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX6) != (uHs ^ Exp(ExpSlot.X6))) { DiagBoot.Log($"guard: sc-x6 got={MethodHash(_dgX6):X8} want={(uHs ^ Exp(ExpSlot.X6)):X8}"); WreckAndFail(); }
        _uX1Hash = MethodHash(_dgX1);
        if (!KatCheck()) { DiagBoot.Log("guard: sc-kat"); WreckAndFail(); }
        if (!FanCheck()) { DiagBoot.Log("guard: sc-fan"); WreckAndFailLater(); }
    }

    private static void VerifyBodies()
    {
        if (MethodHash(_dgAd) != (uHs ^ Exp(ExpSlot.Ad))) { DiagBoot.Log($"guard: vb-ad got={MethodHash(_dgAd):X8} want={(uHs ^ Exp(ExpSlot.Ad)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX1) != (uHs ^ Exp(ExpSlot.X1))) { DiagBoot.Log($"guard: vb-x1 got={MethodHash(_dgX1):X8} want={(uHs ^ Exp(ExpSlot.X1)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX3) != (uHs ^ Exp(ExpSlot.X3))) { DiagBoot.Log($"guard: vb-x3 got={MethodHash(_dgX3):X8} want={(uHs ^ Exp(ExpSlot.X3)):X8}"); WreckAndFail(); }
        if (MethodHash(_dgX6) != (uHs ^ Exp(ExpSlot.X6))) { DiagBoot.Log($"guard: vb-x6 got={MethodHash(_dgX6):X8} want={(uHs ^ Exp(ExpSlot.X6)):X8}"); WreckAndFail(); }
        if (!FanCheck()) { DiagBoot.Log("guard: vb-fan"); WreckAndFailLater(); }
    }

    private static uint MethodHash(Delegate dgMethod)
    {
        byte[] rgIl = dgMethod.Method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
        uint uHash = uLK0A ^ uLK0B;
        uint uCrc = (uint)rgIl.Length ^ 0x9E3779B9u;
        foreach (byte byByte in rgIl) { uHash ^= byByte; uHash *= uLK1A ^ uLK1B; uCrc = (uCrc * 0x01000193u) ^ byByte; }
        GC.KeepAlive(uCrc);
        return uHash;
    }

    private static uint FoldBuf(IntPtr pBuf, int n)
    {
        uint uHash = 0x811C9DC5u;
        for (int i = 0; i < n; i++) { uHash ^= Marshal.ReadByte(pBuf, i); uHash *= 0x01000193u; }
        return uHash;
    }

    private static uint JitTextHash()
    {
        if (_pJitBase == IntPtr.Zero || _jitTextSize <= 0) return 0;
        try
        {
            byte[] rgBuf = new byte[_jitTextSize];
            Marshal.Copy(_pJitBase, rgBuf, 0, _jitTextSize);
            uint uHash = 0x811C9DC5u;
            foreach (byte b in rgBuf) { uHash ^= b; uHash *= 0x01000193u; }
            return uHash;
        }
        catch { return 0; }
    }

    private static uint DecTextHash()
    {
        if (_pDecPage == IntPtr.Zero || _decoderTextSize <= 0) return 0;
        try
        {
            byte[] rgBuf = new byte[_decoderTextSize];
            Marshal.Copy(_pDecPage, rgBuf, 0, _decoderTextSize);
            uint uHash = 0x811C9DC5u;
            foreach (byte b in rgBuf) { uHash ^= b; uHash *= 0x01000193u; }
            return uHash;
        }
        catch { return 0; }
    }

    private static bool KatCheck()
    {
        uint uA, uB, uC;
        if (!KatRaw(out uA, out uB, out uC)) return false;
        return uA == (uHs ^ Exp(ExpSlot.KatA)) && uB == (uHs ^ Exp(ExpSlot.KatB)) && uC == (uHs ^ Exp(ExpSlot.KatC));
    }

    private static bool KatRaw(out uint uA, out uint uB, out uint uC)
    {
        uA = uB = uC = 0;
        byte[] rgVec = new byte[64];
        for (int i = 0; i < 64; i++) rgVec[i] = (byte)(i * 7 + 3);
        IntPtr pBuf = Marshal.AllocHGlobal(64);
        bool bOk;
        try
        {
            DeriveSeedN(rgVec, pBuf);
            uA = FoldBuf(pBuf, 32);
            SeedKeyN(pBuf, IntPtr.Add(pBuf, 32));
            uB = FoldBuf(IntPtr.Add(pBuf, 32), 16);
            GenDisturbN(rgVec, IntPtr.Add(pBuf, 48));
            uC = FoldBuf(IntPtr.Add(pBuf, 48), 16);
            bOk = true;
        }
        catch { bOk = false; }
        finally { WipeFree(pBuf, 64); }
        Array.Clear(rgVec, 0, rgVec.Length);
        return bOk;
    }

    //宽扇入 一次比较覆盖全部已核对事实 镜像还没载入时不判 以两级摘要就位为准
    private static bool FanCheck()
    {
        uint uJ = JitTextHash(), uD = DecTextHash();
        if (uJ == 0 || uD == 0) return true;
        uint uA, uB, uC;
        if (!KatRaw(out uA, out uB, out uC)) { DiagBoot.Log("guard: fan-kat"); return false; }
        uint uFan = uJ ^ uD ^ uA ^ uB ^ uC
                  ^ MethodHash(_dgAd) ^ MethodHash(_dgX1) ^ MethodHash(_dgX3) ^ MethodHash(_dgX6);
        if (uFan != (uHs ^ Exp(ExpSlot.Fan))) { DiagBoot.Log($"guard: fan got={uFan:X8} want={(uHs ^ Exp(ExpSlot.Fan)):X8}"); return false; }
        return true;
    }

    private static void DecoyArgs(string[] rgArgs)
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

    //计算g之前的前256字节哈希也是诱饵
    private static void DecoyHashA(byte[] rgBundle)
    {
        uint uHash = uLK0A ^ uLK0B;
        int iHashLen = Math.Min(rgBundle.Length, 0x100);
        for (int i = 0; i < iHashLen; i++)
            uHash = (uHash ^ rgBundle[i]) * (uLK1A ^ uLK1B);
        GC.KeepAlive(uHash);
    }

    private static uint DecoyHashB(byte[] rgBundle)
    {
        uint uHash = 0x811C9DC5u;
        int iHashLen = Math.Min(rgBundle.Length, 0x40);
        for (int i = 0; i < iHashLen; i++)
            uHash = (uHash ^ rgBundle[i]) * 0x01000193u;
        GC.KeepAlive(uHash);
        return uHash;
    }

    private static void W1(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uV1A == 0xFEEDC0DEu) Environment.FailFast(null);

        if (uKS2A == 0x0BADF00Du) return;
        byte[] rgMeta = Encoding.Unicode.GetBytes(DecodeName(0xCF7DA6AEu, new uint[] { 0xCF12A6CDu, 0x6DD02015u, 0x0B809A43u, 0xAA0513ABu, 0x48358DC7u, 0xE6FD0720u, 0x84BD816Bu, 0x234CFAD3u, 0xC15D7419u, 0x5F1CEE5Au, 0xFD86678Du, 0x9BB3E1C5u, 0x3A175B36u }));
        byte[] rgVer = Encoding.ASCII.GetBytes(DecodeName(0x3EBB0388u, new uint[] { 0x3E9503B1u, 0xDCDC7D71u, 0x7B04F6CAu, 0x191370C3u, 0xB7EEEA09u, 0x55B5644Cu, 0xF429DDA9u, 0x921157A6u, 0x3042D162u, 0xCE964B39u, 0x6CCBC4F2u, 0x0B1D3E42u }));
        int iEnd = 0;
        foreach (var entry in rgEntries)
            iEnd = Math.Max(iEnd, (int)(entry.Item4 + entry.Item3));
        if (iEnd + 160 <= rgBundle.Length)
        {
            Array.Copy(rgMeta, 0, rgBundle, iEnd, Math.Min(rgMeta.Length, rgBundle.Length - iEnd));
            Array.Copy(rgVer, 0, rgBundle, iEnd + 128, Math.Min(rgVer.Length, rgBundle.Length - iEnd - 128));
        }

        var rgDeps = new Dictionary<string, (string, uint, uint, uint)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < rgEntries.Count; i++)
            rgDeps[X2(rgEntries[i].Item1)] = rgEntries[i];
        AssemblyLoadContext.Default.Resolving += (ctx, name) => X5(ctx, name, rgDeps, rgBundle);
    }

    //伪造方法描述给运行时调用 顺带清掉根材料在运行期的最后残留 只留可断言的填充图案
    private static void PatchRegionHeader(byte[] rgBundle)
    {
        if (uR1A == 0x0BADF00Du) Environment.FailFast(null);

        try
        {
            if (rgBundle.Length < 64) return;
            GC.KeepAlive((uint)rgBundle.Length);
            BitConverter.GetBytes(0x1000u).CopyTo(rgBundle, 0);
            BitConverter.GetBytes((ushort)0x0002).CopyTo(rgBundle, 8);
            rgBundle[0x10] = 1; rgBundle[0x11] = 0; rgBundle[0x12] = 2; rgBundle[0x13] = 0x80;
            BitConverter.GetBytes(0u).CopyTo(rgBundle, 0x18);
            BitConverter.GetBytes(0x28u).CopyTo(rgBundle, 0x20);
            byte[] rgCode = { 0x90, 0x90, 0x90, 0x90, 0xEB, 0x00, 0xC3 };
            Array.Copy(rgCode, 0, rgBundle, 0x28, rgCode.Length);
        
        }
        catch { }
    }

    private static void HeadWipeCheck(byte[] rgBundle)
    {
        if (!_bHeadWiped) return;
        try
        {
            if (rgBundle.Length < 64) return;
            for (int i = 0x2F; i < 64; i++)
            {
                if (rgBundle[i] != (byte)(0xA5 ^ i))
                {
                    DiagBoot.LogGuard(11, $" i={i:X} v={rgBundle[i]:X2}");
                    Environment.FailFast(null);
                }
            }
        }
        catch { }
    }

    private static int DecoyStackFill()
    {
        if (uLLCA == 0xDEADBEEFu) return 7;
        Span<long> rgStack = stackalloc long[16];
        for (int i = 0; i < rgStack.Length; i++)
            rgStack[i] = unchecked((long)0xC300EB9090909090);
        GC.KeepAlive(rgStack.Length);
        return (int)(RuntimeSeed() & 0xF) + 1;
    }

    private static void RestoreData(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (rgEntries.Count == 0) return;
        byte[] rgHeader = new byte[64];
        Array.Copy(rgBundle, 0, rgHeader, 0, 64);
        IntPtr pSeed = Marshal.AllocHGlobal(32);
        IntPtr pSiteKey = Marshal.AllocHGlobal(16);
        IntPtr pDisturbKey = Marshal.AllocHGlobal(16);
        DeriveSeedN(rgHeader, pSeed);
        SeedKeyN(pSeed, pSiteKey);
        uint uKeyMix = MxN(pSiteKey);
        _uRollKey = uKeyMix;//roll的at-rest密钥由主钥派生
        GenDisturbN(rgHeader, pDisturbKey);
        for (int i = 0; i < rgEntries.Count; i++)
        {
            var e = rgEntries[i];
            int iOff = (int)e.Item4;
            uint uLen = e.Item3;
            if (i == 0)
            {
                for (int j = 0; j < uLen; j++)
                    rgBundle[iOff + j] ^= (byte)(Marshal.ReadByte(pSiteKey, j & 15) ^ (byte)(uKeyMix >> (8 * (j & 3))));
            }
            else if (e.Item2 == 0x7FFFFFFFu)
            {
                int n = (int)Math.Min(1024, uLen);
                for (int j = 0; j < n; j++)
                    rgBundle[iOff + j] ^= Marshal.ReadByte(pDisturbKey, j & 15);
            }
        }
        Array.Clear(rgHeader, 0, rgHeader.Length);
        WipeFree(pSeed, 32);
        WipeFree(pSiteKey, 16);
        WipeFree(pDisturbKey, 16);
    }

    //解析容器条目表用种子派生的密钥解密
    private static bool X1(byte[] rgBundle, out List<(string, uint, uint, uint)> rgEntries)
    {
        rgEntries = new List<(string, uint, uint, uint)>();
        if (uKS0A == 0xCAFEBABEu) return false;
        uint uXorKey = 0;
        int iCount = 0, iDecoys = 0, iTotal = 0, i = 0, o = 0;
        int iHead = 0, iDataBase = 0, iTbl = 0, iNameOff = 0;
        long llNamePos = 0, llDataOff = 0;
        string sName = "";
        int[] rgPerm = Array.Empty<int>();
        uint[] rgRow = Array.Empty<uint>();
        uint[] rgFields = new uint[4];
        int iState = 0;
        while (true)
        {
            switch (iState)
            {
                case 0:
                    if (rgBundle.Length < 128) return false;
                    iState = 1;
                    continue;
                case 1:
                    IntPtr pSeed = Marshal.AllocHGlobal(32);
                    IntPtr pSiteKey = Marshal.AllocHGlobal(16);
                    DeriveSeedN(rgBundle, pSeed);
                    uXorKey = K1N(pSeed, 32);
                    SeedKeyN(pSeed, pSiteKey);
                    _uSiteKeyA = (uint)Marshal.ReadInt32(pSiteKey, 0);
                    _uSiteKeyB = (uint)Marshal.ReadInt32(pSiteKey, 4);
                    _uSiteKeyC = (uint)Marshal.ReadInt32(pSiteKey, 8);
                    _uSiteKeyD = (uint)Marshal.ReadInt32(pSiteKey, 12);
                    WipeFree(pSeed, 32);
                    WipeFree(pSiteKey, 16);
                    iState = 2;
                    continue;
                case 2:
                    if (rgBundle.Length == 0x7FFFFFFF) return false;
                    iHead = 68 + ((int)((uXorKey >> 0) & 0x1Fu) << 2);
                    iDataBase = (iHead + 16 + ((int)((uXorKey >> 8) & 0x3Fu) << 2) + 7) & ~7;
                    if (iDataBase + 16 > rgBundle.Length) return false;
                    iCount = (int)(ReadU32(rgBundle, iHead) ^ uXorKey);
                    uint uCapBytes = ReadU32(rgBundle, iHead + 4) ^ uXorKey;
                    iTbl = (int)(ReadU32(rgBundle, iHead + 8) ^ uXorKey);
                    iNameOff = (int)(ReadU32(rgBundle, iHead + 12) ^ uXorKey);
                    GC.KeepAlive(uCapBytes);
                    if (iCount <= 0 || iCount > 0x1000 || iTbl < iDataBase || iTbl + 8 > rgBundle.Length || iNameOff < iDataBase || iNameOff > rgBundle.Length) return false;
                    iTotal = (int)(ReadU32(rgBundle, iTbl) ^ uXorKey);
                    iDecoys = iTotal - iCount;
                    if (iTotal <= 0 || iTotal > 0x1100 || iDecoys < 0 || iDecoys > 0x100) return false;
                    if (iTbl + 4 + (long)iTotal * 16 > rgBundle.Length) return false;
                    rgPerm = new int[4];//A+ 每行现算复用同一缓冲区
                    rgRow = new uint[iTotal * 5];
                    i = 0;
                    iState = 3;
                    continue;
                case 3:
                    if (i >= iTotal) { iState = 4; continue; }
                    int iRowOffW = (i << 2) + i;
                    o = iTbl + 4 + i * 16;
                    //字段顺序与填充都按条目序号派生 与全局置换表无关
                    DerivePermAt(uXorKey, i, rgPerm);
                    for (int s = 0; s < 4; s++)
                        rgFields[rgPerm[s]] = ReadU32(rgBundle, o + (s << 2)) ^ FieldKeyAt(uXorKey, i, s);
                    rgRow[iRowOffW + 0] = rgFields[0];//rawLen
                    rgRow[iRowOffW + 1] = rgFields[1];//compLen
                    rgRow[iRowOffW + 2] = rgFields[2];//compOff
                    rgRow[iRowOffW + 3] = rgFields[3];//flag
                    i++;
                    iState = 3;
                    continue;
                case 4:
                    llNamePos = iNameOff;
                    i = 0;
                    iState = 5;
                    continue;
                case 5:
                    if (i >= iCount) { iState = 6; continue; }
                    int iRowOffR = (i << 2) + i;
                    if (rgRow[iRowOffR + 0] == 0) return false;
                    if (llNamePos < iNameOff || llNamePos + 1 > rgBundle.Length) return false;
                    int iLongNameLen = rgBundle[(int)llNamePos];
                    if (llNamePos + 1 + iLongNameLen > rgBundle.Length) return false;
                    uint uNameMask = uXorKey ^ 0x2B7E1516u;
                    int iNamePos = iNameOff;
                    for (int t = 0; t <= i; t++)
                    {
                        if (iNamePos >= rgBundle.Length) return false;
                        int iNameLen = rgBundle[iNamePos];
                        uNameMask = (uNameMask * 0x01000193u) ^ 0x9E3779B9u;
                        if (t == i)
                        {
                            byte[] rgName = new byte[iNameLen];
                            for (int k = 0; k < iNameLen; k++)
                            {
                                uNameMask = (uNameMask * 0x01000193u) + 0x1234567u;
                                rgName[k] = (byte)(rgBundle[iNamePos + 1 + k] ^ (byte)(uNameMask >> 24));
                            }
                            sName = Encoding.UTF8.GetString(rgName);
                        }
                        else
                        {
                            for (int k = 0; k < iNameLen; k++)
                                uNameMask = (uNameMask * 0x01000193u) + 0x1234567u;
                        }
                        iNamePos += 1 + iNameLen;
                    }
                    llDataOff = (long)iDataBase + rgRow[iRowOffR + 2];
                    if (rgRow[iRowOffR + 1] > 0 && llDataOff + rgRow[iRowOffR + 1] > rgBundle.Length) return false;
                    rgEntries.Add((sName, rgRow[iRowOffR + 0], rgRow[iRowOffR + 1], (uint)llDataOff));
                    llNamePos += 1 + iLongNameLen;
                    i++;
                    iState = 5;
                    continue;
                case 6:
                    _iJitSlot = -1;
                    _iSigSlot = -1;
                    _iDecSlot = -1;
                    RestoreData(rgBundle, rgEntries);
                    return true;
            }
        }
    }

    private static string X2(string sName)
        => sName.EndsWith(DecodeName(0x28CF58B4u, new uint[] { 0x28AB589Au, 0xC76AD201u }), StringComparison.OrdinalIgnoreCase)
            ? sName.Substring(0, sName.Length - 4) : sName;

    //区域表逐项掩码打包 打包端用同一张表 表已在容器里被整体异或 这是第二层 掩码材料只用站点键与条目号与字段号
    private static uint RegionFieldMask(int r, int iField)
    {
        //乘加常量沿用文件里既有的异或对 不新增字面量
        uint u = _uSiteKeyA ^ (_uSiteKeyB + (uint)r) ^ (_uSiteKeyC * (uint)(iField + 1)) ^ _uSiteKeyD;
        u = (u ^ (u >> 15)) * (uR1A ^ uR1B);
        u = (u ^ (u >> 13)) + (uR2A ^ uR2B);
        return u ^ (u >> 16);
    }

    private static byte[] ExtractRegion(byte[] rgBundle, (string, uint, uint, uint) entry, int iType)
    {
        uint uCompLen = entry.Item3, uOff = entry.Item4;
        if (uCompLen == 0 || uCompLen > 0x800000) return null!;
        byte[] rgBytes = new byte[uCompLen];
        Array.Copy(rgBundle, (int)uOff, rgBytes, 0, (int)uCompLen);
        int nRegion = BitConverter.ToInt32(rgBytes, 0);
        int iSeg = BitConverter.ToInt32(rgBytes, 4);
        if (nRegion <= 0 || nRegion > 32 || iSeg <= 0 || iSeg > 0x1000) { Array.Clear(rgBytes, 0, rgBytes.Length); return null!; }
        int iHdr = 8 + nRegion * 12 + 4;
        if (iHdr + 4 > rgBytes.Length) { Array.Clear(rgBytes, 0, rgBytes.Length); return null!; }
        int compLen = BitConverter.ToInt32(rgBytes, 8 + nRegion * 12);
        int iAtt = iHdr + compLen;
        if (compLen <= 0 || iAtt >= rgBytes.Length) { Array.Clear(rgBytes, 0, rgBytes.Length); return null!; }
        for (int r = 0; r < nRegion; r++)
        {
            int type = BitConverter.ToInt32(rgBytes, 8 + r * 12) ^ (int)RegionFieldMask(r, 0);
            int iOff = BitConverter.ToInt32(rgBytes, 12 + r * 12) ^ (int)RegionFieldMask(r, 1);
            int len = BitConverter.ToInt32(rgBytes, 16 + r * 12) ^ (int)RegionFieldMask(r, 2);
            if (type == iType)
            {
                if (iOff < 0 || len <= 0 || iAtt + iOff + len > rgBytes.Length) { Array.Clear(rgBytes, 0, rgBytes.Length); return null!; }
                byte[] rgOut = new byte[len];
                Array.Copy(rgBytes, iAtt + iOff, rgOut, 0, len);
                for (int b = 0; b < len / 2; b++) (rgOut[b], rgOut[len - 1 - b]) = (rgOut[len - 1 - b], rgOut[b]);
                Array.Clear(rgBytes, 0, rgBytes.Length);
                return rgOut;
            }
        }
        Array.Clear(rgBytes, 0, rgBytes.Length);
        return null!;
    }

    private static void EnsureDecoder(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uKS3A == 0x8BADF00Du) return;
        if (_pDecPage != IntPtr.Zero) return;
        if (rgEntries.Count == 0) return;
        byte[] rgDecoder = ExtractRegion(rgBundle, rgEntries[0], 1);
        if (rgDecoder == null || rgDecoder.Length == 0) return;
        _pDecPage = LoadBare(rgDecoder, true);
        //记下解码器代码节的哈希 用于运行期核对
        _decoderTextSize = TextSz(rgDecoder);
        if (_pDecPage != IntPtr.Zero && _decoderTextSize > 0)
        {
            byte[] rgText = new byte[_decoderTextSize];
            Marshal.Copy(_pDecPage, rgText, 0, _decoderTextSize);
            uint uCrc = 0x811C9DC5u;
            foreach (byte b in rgText) { uCrc ^= b; uCrc *= 0x01000193u; }
            _decoderTextCrc = uCrc;
            Array.Clear(rgText, 0, rgText.Length);
        }
        Array.Clear(rgDecoder, 0, rgDecoder.Length);
    }

    //手工映射裸镜像并返回基址 代码节与只读数据节按相对虚拟地址排布
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
        string sTxt = DecodeNameRot(0x15083CC2u, new uint[] { 0x14783F62u, 0xB017B5BBu, 0x52D73034u }, uR2A, uR2B);
        string sRda = DecodeNameRot(0x44F79C99u, new uint[] { 0x45879F09u, 0xE00F155Au, 0x82C69303u, 0x1F9E09C4u }, uR2A, uR2B);
        string sDat = DecodeNameRot(0x432BF782u, new uint[] { 0x425BF4A2u, 0xE26B729Bu, 0x7C92EAF4u }, uR2A, uR2B);
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            string sName = Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0');
            uint uSecVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uRsz = BitConverter.ToUInt32(rgDll, o + 16);
            if (sName.Length >= 4 && sName[0] == '.' && uRsz == 0)
                GC.KeepAlive(uSecVa);
            if (sName == sTxt) { uTextVa = uSecVa; iTextRaw = (int)uRaw; iTextSz = (int)uRsz; }
            else if (sName == sRda) { uDataVa = uSecVa; iDataRaw = (int)uRaw; iDataSz = (int)uRsz; }
            else if (sName == sDat) { uData2Va = uSecVa; iData2Raw = (int)uRaw; iData2Sz = (int)uRsz; }
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
        int iFuncOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x1C));
        if (iFuncOff <= 0) return rgExports;
        int iNameOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x20));
        int iOrdOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x24));
        if (iNameCount > 0 && iNameOff > 0 && iOrdOff > 0)
        {
            for (int i = 0; i < iNameCount; i++)
            {
                int iNameRva = BitConverter.ToInt32(rgDll, iNameOff + (i << 2));
                int iNameOff2 = RvaToOff(rgDll, iPe, usOpt, (uint)iNameRva);
                if (iNameOff2 <= 0) continue;
                uint uDecoyName = (uint)(iNameRva ^ i ^ 0x9E3779B9u);
                int iEnd = iNameOff2;
                while (iEnd < rgDll.Length && rgDll[iEnd] != 0) iEnd++;
                string sName = Encoding.ASCII.GetString(rgDll, iNameOff2, iEnd - iNameOff2);
                int usOrdinal = BitConverter.ToUInt16(rgDll, iOrdOff + (i << 1));
                if (usOrdinal >= iFuncCount) { GC.KeepAlive(uDecoyName); continue; }
                uint uFnRva = BitConverter.ToUInt32(rgDll, iFuncOff + usOrdinal * 4);
                rgExports[sName] = uFnRva;
                GC.KeepAlive(uDecoyName ^ uFnRva);
            }
        }
        //按序号导出且没有名称表时取首个函数相对虚拟地址 不暴露具名导出
        if (rgExports.Count == 0 && iFuncCount >= 1)
        {
            uint uFunc0Rva = BitConverter.ToUInt32(rgDll, iFuncOff);
            if (uFunc0Rva != 0) rgExports[string.Empty] = uFunc0Rva;
        }
        return rgExports;
    }

    //按序号的导出映射 具名导出被去掉后只剩序号格式 数据目录里依次是基址与函数数与函数地址表
    private static Dictionary<uint, uint> ParseExportOrdinals(byte[] rgDll)
    {
        var rgOrd = new Dictionary<uint, uint>();
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        ushort usOpt = BitConverter.ToUInt16(rgDll, iPe + 20);
        ushort usMagic = BitConverter.ToUInt16(rgDll, iPe + 24);
        int iDataDir = iPe + 24 + (usMagic == 0x20B ? 112 : 96);
        uint uExpRva = BitConverter.ToUInt32(rgDll, iDataDir);
        if (uExpRva == 0) return rgOrd;
        int iExpOff = RvaToOff(rgDll, iPe, usOpt, uExpRva);
        if (iExpOff <= 0) return rgOrd;
        uint uBase = BitConverter.ToUInt32(rgDll, iExpOff + 0x10);
        int iFuncCount = BitConverter.ToInt32(rgDll, iExpOff + 0x14);
        int iFuncOff = RvaToOff(rgDll, iPe, usOpt, BitConverter.ToUInt32(rgDll, iExpOff + 0x1C));
        if (iFuncOff <= 0) return rgOrd;
        for (int i = 0; i < iFuncCount; i++)
        {
            if (iFuncOff + (i << 2) + 4 > rgDll.Length) break;
            uint uRva = BitConverter.ToUInt32(rgDll, iFuncOff + (i << 2));
            if (uRva != 0) rgOrd[uBase + (uint)i] = uRva;
        }
        return rgOrd;
    }

    private static int RvaToOff(byte[] rgDll, int iPe, ushort usOpt, uint uRva)
    {
        ushort usCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSec = iPe + 24 + usOpt;
        for (int i = 0; i < usCnt; i++)
        {
            int o = iSec + (i << 5) + (i << 3);
            uint uVirtSize = BitConverter.ToUInt32(rgDll, o + 8);
            uint uSecVa = BitConverter.ToUInt32(rgDll, o + 12);
            uint uRawSize = BitConverter.ToUInt32(rgDll, o + 16);
            uint uRaw = BitConverter.ToUInt32(rgDll, o + 20);
            uint uEnd = Math.Max(uVirtSize, uRawSize);
            if (uRva >= uSecVa && uRva < uSecVa + uEnd) return (int)(uRaw + (uRva - uSecVa));
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
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') == DecodeNameRot(0x15083CC2u, new uint[] { 0x14783F62u, 0xB017B5BBu, 0x52D73034u }, uR2A, uR2B))
                return BitConverter.ToUInt32(rgDll, o + 12);
        }
        return 0x1000;
    }

    private static T Mk<T>(uint uRva) where T : Delegate
    {
        var pAddr = new IntPtr(_pJitBase.ToInt64() + (uRva - _uJitTextRva));
        return Marshal.GetDelegateForFunctionPointer<T>(pAddr);
    }

    private static void EnsureJitHook(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uLK1A == 0xDEADBEEFu) return;
        if (rgEntries.Count == 0) return;
        byte[] rgDll = ExtractRegion(rgBundle, rgEntries[0], 2);
        if (rgDll == null || rgDll.Length == 0) return;
        {
            uint uDecoySize = (uint)rgDll.Length ^ 0x5A5A5A5Au;
            _pJitBase = LoadBare(rgDll, true);//.text只读 .data可写
            _uJitTextRva = PeTextVa(rgDll);
            _jitTextSize = TextSz(rgDll);
            byte[] rgText = new byte[_jitTextSize];
            Marshal.Copy(_pJitBase, rgText, 0, _jitTextSize);
            uint uCrc = 0x811C9DC5u;
            foreach (byte b in rgText) { uCrc ^= b; uCrc *= 0x01000193u; }
            _jitTextCrc = uCrc;
            var rgJitOrd = ParseExportOrdinals(rgDll);
            GC.KeepAlive(uDecoySize);
            //只剩两个序号导出 第一个返回六个入口的表 第二个是安装入口 必须在武装之前可用 所以表由更早调用的入口交出
            const uint uJitOrd0 = 1u;
            if (rgJitOrd.TryGetValue(uJitOrd0, out uint uTableRva) && rgJitOrd.TryGetValue(uJitOrd0 + 1u, out uint uInstallRva))
            {
                _fnJitTable = Mk<JitTable>(uTableRva);
                _fnJitInstall = Mk<JitInstall>(uInstallRva);
                IntPtr pFnTab = _fnJitTable();
                if (pFnTab != IntPtr.Zero)
                {
                    _fnJitSetKey = Marshal.GetDelegateForFunctionPointer<JitSetKey>(Marshal.ReadIntPtr(pFnTab, 1 * IntPtr.Size));
                    _fnJitSetTable = Marshal.GetDelegateForFunctionPointer<JitSetSigTable>(Marshal.ReadIntPtr(pFnTab, 2 * IntPtr.Size));
                    _fnJitVerifyHook = Marshal.GetDelegateForFunctionPointer<JitVerifyHook>(Marshal.ReadIntPtr(pFnTab, 3 * IntPtr.Size));
                    _fnJitSetAdFlag = Marshal.GetDelegateForFunctionPointer<JitSetAdFlag>(Marshal.ReadIntPtr(pFnTab, 4 * IntPtr.Size));
                    _fnJitSetSlots = Marshal.GetDelegateForFunctionPointer<JitSetSlots>(Marshal.ReadIntPtr(pFnTab, 5 * IntPtr.Size));
                }
            }
            Array.Clear(rgDll, 0, rgDll.Length);
        }
    }

    private static uint EngineTextSum(byte[] rgDll)
    {
        uint uH = uLK0A ^ uLK0B;
        if (rgDll == null || rgDll.Length < 0x40) return uH;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        if (iPe <= 0 || iPe + 0x40 > rgDll.Length) return uH;
        int iSecCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSecOff = iPe + 24 + BitConverter.ToUInt16(rgDll, iPe + 20);
        for (int i = 0; i < iSecCnt; i++)
        {
            int o = iSecOff + i * 40;
            if (o + 40 > rgDll.Length) return uH;
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') != ".text") continue;
            int iRawSz = BitConverter.ToInt32(rgDll, o + 16);
            int iRaw = BitConverter.ToInt32(rgDll, o + 20);
            if (iRawSz <= 0 || iRaw < 0 || iRaw + iRawSz > rgDll.Length) return uH;
            for (int k = 0; k < iRawSz; k++) { uH ^= rgDll[iRaw + k]; uH *= (uR1A ^ uR1B); }
            return uH;
        }
        return uH;
    }

    private static void EnsureEngine(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (_engineOk) return;
        if (rgEntries.Count == 0) return;
        if (uLK0A == 0x8BADF00Du) return;
        byte[] rgEngine = ExtractRegion(rgBundle, rgEntries[0], 6);
        if (rgEngine == null || rgEngine.Length < 0x40) return;
        int iPe = BitConverter.ToInt32(rgEngine, 0x3C);
        if (rgEngine[0] != 0x4D || rgEngine[1] != 0x5A) { Array.Clear(rgEngine, 0, rgEngine.Length); return; }
        if (iPe <= 0 || iPe + 0x18 >= rgEngine.Length || BitConverter.ToUInt32(rgEngine, iPe) != 0x4550)
        { Array.Clear(rgEngine, 0, rgEngine.Length); return; }
        //引擎摘要核对 在用引擎之前核对容器自带的期望值 缺失或不符都直接毁状态并快速失败 不降级
        byte[] rgSum8 = ExtractRegion(rgBundle, rgEntries[0], 8);
        if (rgSum8 == null || rgSum8.Length < 8)
        {
            Array.Clear(rgEngine, 0, rgEngine.Length);
            DiagBoot.LogGuard(12);
            WreckAndFail();
            return;
        }
        if (BitConverter.ToUInt32(rgSum8, 0) != (uint)rgEngine.Length || BitConverter.ToUInt32(rgSum8, 4) != EngineTextSum(rgEngine))
        {
            Array.Clear(rgSum8, 0, rgSum8.Length);
            Array.Clear(rgEngine, 0, rgEngine.Length);
            DiagBoot.LogGuard(13);
            WreckAndFail();
            return;
        }
        Array.Clear(rgSum8, 0, rgSum8.Length);

        //镜像须是单导出 取唯一导出相对虚拟地址 即引擎入口委托
        try
        {
            _engineLen = (uint)rgEngine.Length;
            _engineBase = LoadBare(rgEngine, true);//.text置RX
            _engineTextVa = PeTextVa(rgEngine);//基于文件字节求 .text VA
            _engineTextSz = TextSz(rgEngine);//引擎自度量范围
            var rgExports = ParseExports(rgEngine);
            if (_engineBase != IntPtr.Zero && rgExports.Count == 1)
            {
                foreach (uint uRva in rgExports.Values)
                {
                    long pAddr = _engineBase.ToInt64() + (long)(uRva - _engineTextVa);
                    _fnEngine = Marshal.GetDelegateForFunctionPointer<EngineRun>(new IntPtr(pAddr));
                    break;
                }
            }
            if (_fnEngine != null) _engineOk = true;
        }
        catch { }
        Array.Clear(rgEngine, 0, rgEngine.Length);
        GC.KeepAlive(_engineLen);
    }

    //载入容器字节码并校验 没有指定类型就跳过容器 长度前缀是明文 用来算各内核入口 每个内核各自加密且流从头开始
    private static bool EnsureRoll(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (_pRoll != IntPtr.Zero) return true;
        if (rgEntries.Count == 0) return true;
        if (uLK0A == 0x8BADF00Du) return true;
        byte[] rgKey = ExtractRegion(rgBundle, rgEntries[0], 7);
        DiagBoot.Log("roll: region len=" + (rgKey == null ? -1 : rgKey.Length) + " cap=0x1000");
        if (rgKey == null || rgKey.Length < 8 || rgKey.Length > 0x1000) return true;
        IntPtr p = Marshal.AllocHGlobal(rgKey.Length);
        Marshal.Copy(rgKey, 0, p, rgKey.Length);
        _pRoll = p;
        _cbRoll = rgKey.Length;
        for (int i = 0; i < _pRollKern.Length; i++) _pRollKern[i] = IntPtr.Zero;
        bool bOk = _cbRoll >= 12;
        uint uCount = 0;
        //内核顺序固定 依次为密钥与签名表掩码与种子密钥与派生常量与扰动表与混合常量与流解密 数目不符就放弃
        if (bOk)
        {
            uCount = (uint)rgKey[0] | ((uint)rgKey[1] << 8) | ((uint)rgKey[2] << 16) | ((uint)rgKey[3] << 24);
            int iOff = 4;
            //数目必须与引导侧内核顺序一致 不能写死
            if (uCount != (uint)_pRollKern.Length) bOk = false;
            for (uint i = 0; bOk && i < uCount; i++)
            {
                if (iOff + 4 > rgKey.Length) { bOk = false; break; }
                uint uLen = (uint)rgKey[iOff] | ((uint)rgKey[iOff + 1] << 8) | ((uint)rgKey[iOff + 2] << 16) | ((uint)rgKey[iOff + 3] << 24);
                iOff += 4;
                if (uLen < 8 || (uLen & 3) != 0 || uLen > (uint)rgKey.Length - (uint)iOff) { bOk = false; break; }
                _pRollKern[i] = new IntPtr(p.ToInt64() + iOff);
                iOff += (int)uLen;
            }
        }
        DiagBoot.Log("roll: cb=" + _cbRoll + " count=" + uCount + " want=" + _pRollKern.Length + " ok=" + bOk);
        Array.Clear(rgKey, 0, rgKey.Length);
        if (!bOk) return false;
        return RollSelfCheck();
    }

    //固定输入上各内核的派生值必须与本机一致 前两个没有明文实现用构建期常量比对 其余四个与明文实现对照
    private static bool RollSelfCheck()
    {
        if (_fnEngine == null || _pRoll == IntPtr.Zero) return true;
        if (!KatCheck()) { DiagBoot.Log("guard: roll-kat"); WreckAndFail(); }
        byte[] rgVec = new byte[64], rg16 = new byte[16], rg32 = new byte[32], rgM = new byte[256], rgRef = new byte[16];
        for (int i = 0; i < 64; i++) rgVec[i] = (byte)(i * 7 + 1);
        Array.Copy(rgVec, rg16, 16);
        Array.Copy(rgVec, rg32, 32);
        IntPtr pSeed = Marshal.AllocHGlobal(32), pRef = Marshal.AllocHGlobal(16);
        bool bOk;
        try
        {
            for (int i = 0; i < 32; i++) Marshal.WriteByte(pSeed, i, rgVec[i]);
            bOk = JitKeyVm(rg16) == (uExpJitKeyA ^ uExpJitKeyB);
            if (bOk) bOk = MaskVm(rg16) == (uExpMaskKeyA ^ uExpMaskKeyB);
            if (bOk)//SeedKey材料@Seed32结果写在Key
            {
                SeedKeyN(pSeed, pRef);
                Marshal.Copy(pRef, rgRef, 0, 16);
                KernCall(_pRollKern[KI_SEED], rg32, 0, rgM);
                for (int i = 0; bOk && i < 16; i++) bOk = rgM[0x20 + i] == rgRef[i];
            }
            if (bOk) bOk = KernCall(_pRollKern[KI_K1], rg32, 0, null!) == K1N(pSeed, 32);
            if (bOk)//GenDisturb材料@H64结果写在Key
            {
                GenDisturbN(rgVec, pRef);
                Marshal.Copy(pRef, rgRef, 0, 16);
                KernCall(_pRollKern[KI_DIST], rgVec, 0x40, rgM);
                for (int i = 0; bOk && i < 16; i++) bOk = rgM[0x20 + i] == rgRef[i];
            }
            if (bOk)//Mx材料@Key16
            {
                for (int i = 0; i < 16; i++) Marshal.WriteByte(pRef, i, rg16[i]);
                bOk = KernCall(_pRollKern[KI_MX], rg16, 0x20, null!) == MxN(pRef);
            }
        }
        catch { bOk = false; }
        finally
        {
            WipeFree(pSeed, 32);
            WipeFree(pRef, 16);
        }
        Array.Clear(rgVec, 0, rgVec.Length);
        Array.Clear(rg16, 0, rg16.Length);
        Array.Clear(rg32, 0, rg32.Length);
        Array.Clear(rgM, 0, rgM.Length);
        Array.Clear(rgRef, 0, rgRef.Length);
        return bOk;
    }

    //JIT信息接口的虚表槽位偏移按运行时主版本查表 表外主版本在安装处失败关闭 免得槽位用错静默损坏 表按上游头文件顺序推导
    private static bool JitSlotsForMajor(int iRtMajor, out uint uGmi, out uint uCi)
    {
        switch (iRtMajor)
        {
            case 5: uGmi = 0x18; uCi = 0x20; return true;
            case 6:
            case 7: uGmi = 0x20; uCi = 0x28; return true;
            case 8: uGmi = 0x20; uCi = 0x30; return true;
            case 9:
            case 10: uGmi = 0x28; uCi = 0x40; return true;
            case 11: uGmi = 0x30; uCi = 0x48; return true;
            default: uGmi = 0; uCi = 0; return false;
        }
    }

    //未知布局会从错误的槽进入JIT造成静默损坏 发行产物拿不到就不注入 调试构建回退到最新的已知布局
    private static bool JitSlotsFallback(int iRtMajor, out uint uGmi, out uint uCi)
    {
        return JitSlotsForMajor(iRtMajor > 11 ? 11 : 5, out uGmi, out uCi);
    }

    private static void ApplyJitSlots()
    {
        if (_fnJitSetSlots == null)
        {
#if DEBUG
            Console.Error.WriteLine("[FcJit] jithook export @6 (SetJitSlots) missing, canInline protection off");
            return;
#else
            Environment.FailFast(null);
#endif
        }
        int iRtMajor = Environment.Version.Major;
        if (!JitSlotsForMajor(iRtMajor, out uint uGmi, out uint uCi))
        {
#if DEBUG
            JitSlotsFallback(iRtMajor, out uGmi, out uCi);
            Console.Error.WriteLine("[FcJit] unmapped runtime " + Environment.Version + ": using slot layout 0x" +
                uGmi.ToString("X") + "/0x" + uCi.ToString("X") + " (release builds fail closed here)");
#else
            Environment.FailFast(null);
#endif
        }
        //内联按表内偏移安装 槽位被拒时不能当它已生效
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_INPLACE") == "1")
        {
            uCi |= 0x4000u;
            DiagBoot.LogStage(17);
        }
        if (Environment.GetEnvironmentVariable("LAMARR_NO_CANINLINE") == "1") uCi |= 0x8000u;
        //解密后的方法体可能被内联器读到临时缓冲 故默认禁止内联 可由开关恢复
        if (Environment.GetEnvironmentVariable("LAMARR_CI_DENY") == "1") uCi |= 0x2000u;
        if (Environment.GetEnvironmentVariable("LAMARR_NO_CISCAN") == "1") uCi |= 0x2000u;
        string sCiOv = Environment.GetEnvironmentVariable("LAMARR_CI_OFF") ?? "";
        if (sCiOv.Length > 0) uCi = (uCi & 0xE000u) | Convert.ToUInt32(sCiOv, 16);
#endif
        uint uCiMode = 0;
#if LAMARR_TRIAGE
        string sCiMode = Environment.GetEnvironmentVariable("LAMARR_CI_MODE") ?? "";
        if (sCiMode.Length > 0) uCiMode = Convert.ToUInt32(sCiMode, 16);
#endif
        DiagBoot.LogStage(18);
        if (_fnJitSetSlots(uGmi, uCi, uCiMode) == 0)
        {
            //汇编层拒收这对偏移
#if DEBUG
            Console.Error.WriteLine("[FcJit] SetJitSlots rejected 0x" + uGmi.ToString("X") + "/0x" + uCi.ToString("X"));
            return;
#else
            Environment.FailFast(null);
#endif
        }
    }

    private static void JitVerify()
    {
        if (_pJitBase == IntPtr.Zero || _jitTextSize <= 0 || _jitTextCrc == 0) return;
        try
        {
            byte[] rgBuf = new byte[_jitTextSize];
            Marshal.Copy(_pJitBase, rgBuf, 0, _jitTextSize);
            uint uHash = 0x811C9DC5u;
            foreach (byte b in rgBuf) { uHash ^= b; uHash *= 0x01000193u; }
            if (uHash != _jitTextCrc) { DiagBoot.LogGuard(14, $" got={uHash:X8} want={_jitTextCrc:X8} size={_jitTextSize}"); Environment.FailFast(null); }
            if (Exp(ExpSlot.Jit) != 0 && uHash != Exp(ExpSlot.Jit)) { DiagBoot.LogGuard(15, $" got={uHash:X8} want={Exp(ExpSlot.Jit):X8}"); Environment.FailFast(null); }
        }
        catch { }
    }
    private static void DecVerify()
    {
        if (_pDecPage == IntPtr.Zero || _decoderTextSize <= 0 || _decoderTextCrc == 0) return;
        byte[] rgBuf = new byte[_decoderTextSize];
        Marshal.Copy(_pDecPage, rgBuf, 0, _decoderTextSize);
        uint uHash = 0x811C9DC5u;
        foreach (byte b in rgBuf) { uHash ^= b; uHash *= 0x01000193u; }
        if (uHash != _decoderTextCrc) { DiagBoot.LogGuard(16, $" got={uHash:X8} want={_decoderTextCrc:X8} size={_decoderTextSize}"); Environment.FailFast(null); }
        if (Exp(ExpSlot.Dec) != 0 && uHash != Exp(ExpSlot.Dec)) { DiagBoot.LogGuard(17, $" got={uHash:X8} want={Exp(ExpSlot.Dec):X8}"); Environment.FailFast(null); }
    }

private static void InjectSigs(byte[] rgBundle, List<(string, uint, uint, uint)> rgEntries)
    {
        if (uLK1B == 0xCAFEBABEu) return;
        if (rgEntries.Count == 0) return;
        byte[] rgSig = ExtractRegion(rgBundle, rgEntries[0], 3);
        if (rgSig == null || rgSig.Length < 4) return;
        if (_fnJitSetTable == null) { Array.Clear(rgSig, 0, rgSig.Length); return; }
        //第三区布局 含标记与计数与逐个指纹与逐个变体字节 变体由打包期给出 钩子直接取用 取不到时自行计算
        uint uEntryCount;
        int iFpOff = 0, iVarOff = -1;
        if (rgSig.Length >= 8 && BitConverter.ToUInt32(rgSig, 0) == 0x32474953u)
        {
            uEntryCount = BitConverter.ToUInt32(rgSig, 4);
            iFpOff = 8;
            iVarOff = 8 + (int)uEntryCount * 4;
            if (uEntryCount == 0 || (long)iVarOff + (long)uEntryCount > rgSig.Length) { Array.Clear(rgSig, 0, rgSig.Length); return; }
        }
        else
        {
            uEntryCount = (uint)(rgSig.Length / 4);//表项4字节 条目数 = 长度/4
        }
        //掩码取自独立域与解密密钥无关 表项由校验值与掩码合成 派生体在第二内核里 内核缺失就不注入签名表
        if (_pRollKern[KI_MASK] == IntPtr.Zero) { Array.Clear(rgSig, 0, rgSig.Length); return; }
        byte[] rgMat = SiteKey();
        uint uMaskLo = MaskVm(rgMat);
        Array.Clear(rgMat, 0, rgMat.Length);
        IntPtr pSig = Marshal.AllocHGlobal(rgSig.Length);//进程期常驻jithook直接引用不释放
        Marshal.Copy(rgSig, 0, pSig, rgSig.Length);
        IntPtr pVars = iVarOff >= 0 ? IntPtr.Add(pSig, iVarOff) : IntPtr.Zero;
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_NO_SIG_VARS") == "1") pVars = IntPtr.Zero;
#endif
        _fnJitSetTable(IntPtr.Add(pSig, iFpOff), uEntryCount, uMaskLo, pVars);
#if LAMARR_TRIAGE
        if (Environment.GetEnvironmentVariable("LAMARR_SIG_DUMP") == "1")
        {
            var sb = new System.Text.StringBuilder("sig: count=" + uEntryCount + " mask=" + uMaskLo.ToString("X8")
                + " vars=" + (pVars == IntPtr.Zero ? "none" : "on"));
            for (int i = 0; i < uEntryCount; i++) sb.Append(' ').Append(i).Append(":0x").Append(BitConverter.ToUInt32(rgSig, iFpOff + i * 4).ToString("X8"));
            DiagBoot.Log(sb.ToString());
        }
#endif
        Array.Clear(rgSig, 0, rgSig.Length);
    }

    //把缓冲区间内的密钥流还原交给卷内核 宿主只搬字节 内核约定入参顺序与流起始下标与字节数 返回失败表示内核缺失或调用失败
    private static bool DecStreamVm(byte[] rgBuf, int iOff, uint uLen, uint uKeyMix, byte[] rgKey)
    {
        if (_pRollKern[KI_DEC] == IntPtr.Zero || _fnEngine == null) return false;
        IntPtr pRegs = Marshal.AllocHGlobal(VmRegBytes);
        IntPtr pMem = Marshal.AllocHGlobal(VmMemBytes);
        try
        {
            for (int i = 0; i < VmRegBytes; i++) Marshal.WriteByte(pRegs, i, 0);
            Marshal.WriteInt64(pRegs, 1 * 8, uKeyMix);
            Marshal.WriteInt64(pRegs, 4 * 8, unchecked((long)BitConverter.ToUInt64(rgKey, 0)));
            Marshal.WriteInt64(pRegs, 5 * 8, unchecked((long)BitConverter.ToUInt64(rgKey, 8)));
            uint uDone = 0;
            while (uDone < uLen)
            {
                int n = (int)Math.Min((uint)VmDataBytes, uLen - uDone);
                Marshal.WriteInt64(pRegs, 2 * 8, uDone);//v2=流下标
                Marshal.WriteInt64(pRegs, 3 * 8, (long)n);//v3=本块字节数
                Marshal.WriteInt64(pRegs, 6 * 8, 0L);//v6=M偏移
                for (int i = 0; i < n; i++) Marshal.WriteByte(pMem, i, rgBuf[iOff + (int)uDone + i]);
                _fnEngine(_pRollKern[KI_DEC], pRegs, pMem, VmMemBytes);
                for (int i = 0; i < n; i++) rgBuf[iOff + (int)uDone + i] = Marshal.ReadByte(pMem, i);
                uDone += (uint)n;
            }
        }
        catch { return false; }
        finally { WipeFree(pRegs, VmRegBytes); WipeFree(pMem, VmMemBytes); }
        return true;
    }

    //解压条目时 压缩的按4千字节页调解码器 非压缩的直接异或
    private static byte[] X3(byte[] rgBundle, (string, uint, uint, uint) entry, bool bMain)
    {
        if (uKS1A == 0xF00DFACEu) return rgBundle;
        byte[] rgRaw = Array.Empty<byte>(), rgComp = Array.Empty<byte>(), rgOut = Array.Empty<byte>(), rgHist = Array.Empty<byte>(), rgPage = Array.Empty<byte>(), rgState = Array.Empty<byte>();
        DecodePageFunc fnDec = null!;
        uint uRawLen = 0, uPage = 0, uPageStart = 0, uPageEnd = 0;
        int iDataOff = 0, iCompLen = 0, iDecRc = 0, iChunkLen = 0;
        int iState = 0;
        while (true)
        {
            switch (iState)
            {
                case 0:
                    iDataOff = (int)entry.Item4;
                    uRawLen = entry.Item2;
                    iCompLen = (int)entry.Item3;
                    if (uRawLen == 0x7FFFFFFFu) { iState = 4; continue; }
                    iState = (iCompLen == (int)uRawLen) ? 1 : 2;
                    continue;
                case 4:
                    byte[] rgPlain = new byte[iCompLen];
                    Array.Copy(rgBundle, iDataOff, rgPlain, 0, iCompLen);
                    return rgPlain;
                case 1:
                    if (uRawLen == 0x7FFFFFFF) return rgBundle;
                    rgRaw = new byte[uRawLen];
                    Array.Copy(rgBundle, iDataOff, rgRaw, 0, (int)uRawLen);
                    byte[] rgKey1 = SiteKey();
                    uint uKeyMix1 = MixKey(rgKey1);
                    //流异或的变换本身交给卷内核 解密在副本上做 不动原始容器
                    bool bVm1 = DecStreamVm(rgRaw, 0, uRawLen, uKeyMix1, rgKey1);
#if LAMARR_TRIAGE
                    if (!bVm1) DiagBoot.Log("vm: stream decrypt failed len=" + uRawLen.ToString("X8"));
#endif
                    if (!bVm1) throw new InvalidDataException();//卷内核必须在位不做明文回退
                    uint uDecoyRoll = uKeyMix1 ^ 0x5A5A5A5Au;
                    int iDn1 = (int)Math.Min(uRawLen, 64u);
                    for (int i = 0; i < iDn1; i++) uDecoyRoll = (uDecoyRoll * 0x9E3779B9u) ^ rgRaw[i];
                    GC.KeepAlive(uDecoyRoll);
                    Array.Clear(rgKey1, 0, rgKey1.Length);
                    return rgRaw;
                case 2:
                    rgComp = new byte[iCompLen];
                    Array.Copy(rgBundle, iDataOff, rgComp, 0, iCompLen);
                    return X3Comp(rgComp, uRawLen, bMain);
            }
        }
    }

    private static byte[] X3Comp(byte[] rgComp, uint uRawLen, bool bMain)
    {
        if (_pDecPage == IntPtr.Zero)
            throw new InvalidDataException();
        DecodePageFunc fnDec = Marshal.GetDelegateForFunctionPointer<DecodePageFunc>(_pDecPage);
        int iCompLen = rgComp.Length;
        if (bMain)
        {
            int nRegion = BitConverter.ToInt32(rgComp, 0);
            int iSeg2 = BitConverter.ToInt32(rgComp, 4);
            if (nRegion > 0 && nRegion <= 32 && iSeg2 > 0 && iSeg2 <= 0x1000)
            {
                int iHdr = 8 + nRegion * 12 + 4;
                int compLen = BitConverter.ToInt32(rgComp, 8 + nRegion * 12);
                if (compLen > 0 && iHdr + compLen <= rgComp.Length)
                {
                    byte[] rgComp0 = new byte[compLen];
                    Array.Copy(rgComp, iHdr, rgComp0, 0, compLen);
                    rgComp = rgComp0;
                    iCompLen = compLen;
                }
            }
        }
        //用消息认证码验证压缩流头的16字节标签 防篡改
        if (iCompLen < 16) throw new InvalidDataException();
        byte[] rgTagHdr = new byte[16];
        Array.Copy(rgComp, 0, rgTagHdr, 0, 16);
        byte[] rgCompBody = new byte[iCompLen - 16];
        Array.Copy(rgComp, 16, rgCompBody, 0, rgCompBody.Length);
        if (!PolyVerify(PolyTagKey(), rgCompBody, rgTagHdr))
            throw new InvalidDataException();
        rgComp = rgCompBody;
        iCompLen = rgComp.Length;

        byte[] rgOut = new byte[uRawLen];
        byte[] rgHist = new byte[0x80000];
        byte[] rgPage = new byte[0x1000];
        byte[] rgState = new byte[0x38];
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x00);
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x04);
        BitConverter.GetBytes(1u).CopyTo(rgState, 0x08);
        BitConverter.GetBytes((uint)iCompLen).CopyTo(rgState, 0x28);
        BitConverter.GetBytes(uRawLen).CopyTo(rgState, 0x34);
        rgHist[0] = rgComp[0];
        uint uPage = 0;
        try
        {
            while (true)
            {
                if (uPage << 12 >= uRawLen) break;
                uint uPageStart = uPage << 12;
                uint uPageEnd = Math.Min(uPageStart + 0x1000, uRawLen);
                if (uPage == 0) rgPage[0] = rgComp[0];
                else Array.Clear(rgPage, 0, 0x1000);
                BitConverter.GetBytes(uPageStart).CopyTo(rgState, 0x2C);
                BitConverter.GetBytes(uPageEnd).CopyTo(rgState, 0x30);
                uint uDecoyPage = (uPage ^ 0x0F0F0F0Fu) * 0x9E3779B9u;
                int iDecRc = fnDec(rgState, rgHist, rgPage, rgComp);
                GC.KeepAlive(uDecoyPage);
                if (iDecRc != 0)
                    throw new InvalidDataException();
                int iChunkLen = (int)(uPageEnd - uPageStart);
                Array.Copy(rgPage, 0, rgOut, (int)uPageStart, iChunkLen);
                uPage++;
            }
        }
        finally
        {
            //解码完成或失败 中间状态明文不留 只有输出缓冲是必要的明文输出
            Array.Clear(rgHist, 0, rgHist.Length);
            Array.Clear(rgState, 0, rgState.Length);
            Array.Clear(rgPage, 0, rgPage.Length);
        }
        return rgOut;
    }

    private static Assembly X4(AssemblyLoadContext alc, byte[] rgBytes)
    {
        using var stream = new MemoryStream(rgBytes, writable: true);
        var asm = alc.LoadFromStream(stream);
        if (stream.TryGetBuffer(out var buf))
            Array.Clear(buf.Array!, buf.Offset, buf.Count);
        return asm;
    }

    private static Assembly? X5(AssemblyLoadContext alc, AssemblyName asmName,
                                Dictionary<string, (string, uint, uint, uint)> rgDeps, byte[] rgBundle)
    {
        if (asmName.Name == null || !rgDeps.TryGetValue(asmName.Name, out var entry))
            return null;
        byte[] rgBytes;
        try { rgBytes = X3(rgBundle, entry, false); }
        catch { return null; }
        try { return X4(alc, rgBytes); }
        finally { Array.Clear(rgBytes, 0, rgBytes.Length); }
    }

    private static byte[]? ReadSectionFromMemory(ProcessModule modSelf, string sSection)
    {
        IntPtr pBase = modSelf.BaseAddress;
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
            string sSecName = (Marshal.PtrToStringAnsi(new IntPtr(pBase.ToInt64() + o), 8) ?? "").TrimEnd('\0');
            if (sSecName != sSection) continue;
            int iVs = Marshal.ReadInt32(pBase, o + 8);
            int iVa = Marshal.ReadInt32(pBase, o + 12);
            if (iVs <= 0 || iVa <= 0) return null;
            byte[] rgBuf = new byte[iVs];
            Marshal.Copy(new IntPtr(pBase.ToInt64() + iVa), rgBuf, 0, iVs);
            return rgBuf;
        }
        return null;
    }

    private static byte[] X6()
    {
        if (uLLCB == 0xCAFEBABEu) return Array.Empty<byte>();
        string sSection = DecodeNameRot(0x15083CC2u, new uint[] { 0x14783F52u, 0xB01FB573u, 0x52D7333Cu, 0xEFAEA9EDu }, uR2A, uR2B);
        try
        {
            var modSelf = Process.GetCurrentProcess().MainModule;
            if (modSelf != null)
            {
                byte[]? rgMem = ReadSectionFromMemory(modSelf, sSection);
                if (rgMem != null && rgMem.Length >= 128)
                    return rgMem;
            }
        }
        catch { }
        string sSelfPath = Process.GetCurrentProcess().MainModule?.FileName
    #if NET6_0_OR_GREATER
            ?? Environment.ProcessPath
    #endif
            ?? throw new InvalidOperationException();
        using var fsSelf = new FileStream(sSelfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        byte[] rgHdr = new byte[0x400];
        int iRead = fsSelf.Read(rgHdr, 0, rgHdr.Length);
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
            string sSecName = Encoding.ASCII.GetString(rgHdr, o, 8).TrimEnd('\0');
            uint uRaw = BitConverter.ToUInt32(rgHdr, o + 20);
            uint uRawSz = BitConverter.ToUInt32(rgHdr, o + 16);
            if (sSecName.Length == 4 && sSecName[0] == '.' && uRawSz == 0)
                GC.KeepAlive(uRaw);
            if (sSecName == sSection)
            {
                if (uRawSz == 0 || uRaw + uRawSz > (ulong)fsSelf.Length)
                    throw new InvalidOperationException();
                byte[] rgMarker = new byte[uRawSz];
                fsSelf.Position = uRaw;
                int iGot = 0;
                while (iGot < uRawSz)
                {
                    int iRead2 = fsSelf.Read(rgMarker, iGot, (int)uRawSz - iGot);
                    if (iRead2 <= 0)
                        throw new InvalidOperationException();
                    iGot += iRead2;
                }
                return rgMarker;
            }
        }
        throw new InvalidOperationException();
    }
}

//环境变量记录启动期各步与未处理异常 模块装载时即运行 入口类型静态初始化之前的失败也能带栈记录 发行构建里也保留 能指名哪道守卫被触发

internal static class DiagBoot
{
    private static string sPath = "";

    //发行态只写编号 可读文本只在调试构建里 文本字面量不进发行产物
    internal static void LogStage(int iStage)
    {
#if DEBUG
        Log("stage: " + rgStageName[iStage]);
#else
        Log("stage: " + iStage);
#endif
    }

    internal static void LogStage(int iStage, string sTail)
    {
#if DEBUG
        Log("stage: " + rgStageName[iStage] + sTail);
#else
        Log("stage: " + iStage + sTail);
#endif
    }

#if DEBUG
    private static readonly string[] rgStageName =
    {
        "", "first chance exception logging on", "selfcheck ok", "x1 ok", "decoder ok",
        "engine ok", "roll ok", "QF (roll selfcheck failed, engine not live)", "roll selfcheck ok",
        "trace", "jit hook installed", "payload decrypted", "invoking payload entry",
        "QF (decoy path)", "exit path warmed", "wiping header safe parts",
        "wiping header signatures", "jit in-place decrypt requested", "canInline protection ON",
    };
#endif

    //发行态只写编号 编号名只在调试构建里
    internal static void LogGuard(int iGuard)
    {
#if DEBUG
        Log("guard: " + rgGuardName[iGuard]);
#else
        Log("guard: g" + iGuard);
#endif
    }

    internal static void LogGuard(int iGuard, string sTail)
    {
#if DEBUG
        Log("guard: " + rgGuardName[iGuard] + sTail);
#else
        Log("guard: g" + iGuard + sTail);
#endif
    }

#if DEBUG
    private static readonly string[] rgGuardName =
    {
        "", "integrity late tick", "fan-late", "jithook verify",
        "roll selfcheck failed with the engine live", "jit event after wipe", "ad-x1", "ad",
        "tickguard", "tcheck", "elapsed", "headwipe", "engine-sum-missing", "engine-sum",
        "jitcrc", "jitcrc-exp", "deccrc", "deccrc-exp",
    };
#endif

    internal static void Log(string s)
    {
        if (sPath.Length == 0) return;
#if !LAMARR_TRIAGE
        if (s.StartsWith("guard: ", StringComparison.Ordinal))
        {
            //守卫行示例含实际值与期望值 尾部标注属于哪段守卫
            int iSp = s.IndexOf(' ', 7);
            if (iSp > 0) s = s.Substring(0, iSp);
        }
        else if (!s.StartsWith("UNHANDLED:", StringComparison.Ordinal) && !s.StartsWith("exc: ", StringComparison.Ordinal)) return;
#endif
        //启动期日志会从多个线程同时写 追加写入在冲突时会抛异常并丢行 恰好掩盖要看的守卫失败
        for (int i = 0; i < 20; i++)
        {
            try
            {
                using (var fs = new System.IO.FileStream(sPath, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var sw = new System.IO.StreamWriter(fs))
                {
                    sw.Write(s + "\n");
                }
                return;
            }
            catch { System.Threading.Thread.Sleep(5); }
        }
    }

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        try
        {
            sPath = System.Environment.GetEnvironmentVariable("LAMARR_BOOT_LOG") ?? "";
            if (sPath.Length == 0) return;
            System.IO.File.WriteAllText(sPath, "module init ok\n");
            System.AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UNHANDLED: " + e.ExceptionObject);
        }
        catch { }
    }
}
