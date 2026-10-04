using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace A0;

internal static class H
{
    private static readonly byte[] rgSig = new byte[]//bundle标记的32B签名
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

    [STAThread]
    private static int Main(string[] rgArgs)
    {
        try
        {
            S5LayoutJitter();//每进程重派 先垫一段运行期随机的布局抖动
            string sSelf = SelfPath();
            byte[] rg = File.ReadAllBytes(sSelf);//条目偏移在头部里按文件偏移存放 所以整文件读进来
            int iMarker = FindMarker(rg);
            if (iMarker < 0) return Fail(0);
            long lHeader = BitConverter.ToInt64(rg, iMarker - 8);
            if (lHeader <= 0 || lHeader >= rg.Length) return Fail(1);

            int iPos = (int)lHeader;//解析宿主bundle头部只要条目表
            uint uMajor = ReadU32(rg, ref iPos);
            ReadU32(rg, ref iPos);
            int iCount = ReadI32(rg, ref iPos);
            ReadStr(rg, ref iPos);
            if (uMajor >= 2) for (int i = 0; i < 5; i++) ReadI64(rg, ref iPos);
            if (iCount <= 0 || iCount > 0x1000) return Fail(2);
            var rgSz = new long[iCount];
            long lOff0 = 0, lSz0 = 0;
            for (int i = 0; i < iCount; i++)
            {
                long lOff = ReadI64(rg, ref iPos);
                long lSz = ReadI64(rg, ref iPos);
                if (uMajor >= 6) ReadI64(rg, ref iPos);
                ReadU8(rg, ref iPos);
                ReadStr(rg, ref iPos);
                rgSz[i] = lSz;
                if (i == 0) { lOff0 = lOff; lSz0 = lSz; }
            }
            if (lSz0 <= Q.iTailLen || lOff0 <= 0 || lOff0 + lSz0 > rg.Length) return Fail(3);

            byte[] rgEntry = new byte[lSz0];
            Array.Copy(rg, lOff0, rgEntry, 0, lSz0);
            Array.Clear(rg, 0, rg.Length);

            ulong uLen = BitConverter.ToUInt64(rgEntry, (int)lSz0 - 16) ^ Q.uLenMask;//尾部40字节roll长度 引擎长度roll钥匙掩码 布局策略掩码 前置内核长度掩码 保留 密文长度掩码 密文校验掩码
            ulong uChk = BitConverter.ToUInt64(rgEntry, (int)lSz0 - 8) ^ Q.uChkMask;
            int iCipher = (int)uLen;
            int iLayer = (int)lSz0 - Q.iTailLen - iCipher;
            if (iCipher <= 0 || iLayer <= 0) return Fail(4);
            int iRollLen = BitConverter.ToInt32(rgEntry, (int)lSz0 - Q.iTailLen + 0);
            int iEngLen = BitConverter.ToInt32(rgEntry, (int)lSz0 - Q.iTailLen + 4);
            int iPrekLen = BitConverter.ToInt32(rgEntry, (int)lSz0 - Q.iTailLen + 16) ^ (int)Q.uPrekMask;//前置内核段长度 静态掩码 摆放需要 不涉密钥
            if (iRollLen < 0 || iEngLen < 0 || iPrekLen < 12 || iRollLen + iEngLen + iPrekLen >= iLayer) return Fail(4);

            byte[] rgHead = new byte[Math.Min(Q.iHeadSample, iLayer)];
            Array.Copy(rgEntry, 0, rgHead, 0, rgHead.Length);
            uint uPolicy = BitConverter.ToUInt32(rgEntry, (int)lSz0 - Q.iTailLen + 12) ^ Q.uLayoutMask;//尾部+12的策略码必须等于镜像头与roll钥匙派生的值 顺序与反转都按它来
            if ((uPolicy & ~7u) != 0) return Fail(14);//策略码只允许低3位 高位有值即判改过
            int iSegLayer = iLayer - iPrekLen;//前置内核段紧邻密文之前 所以roll与引擎两段摆在它之前
            Q.EntrySegmentOffsets(iSegLayer, iRollLen, iEngLen, uPolicy, out int iRollOff, out int iEngOff);
            if (iRollOff <= 0 || iEngOff <= 0 || iRollOff + iRollLen > iSegLayer || iEngOff + iEngLen > iSegLayer) return Fail(4);
            byte[] rgFold = new byte[iRollLen + iEngLen];//产物自指 复制一条盘上形态的roll与引擎流供VM计算 再剥容器掩码
            Array.Copy(rgEntry, iRollOff, rgFold, 0, iRollLen);
            Array.Copy(rgEntry, iEngOff, rgFold, iRollLen, iEngLen);
            if (iEngLen > 0) Q.Xor(rgEntry, iEngOff, Lamarr.Packer.ChMask.Mask(rgHead, lSz0, 0u, 2, iEngLen, 0, 0));//引擎段不含roll钥匙 先就地还原它并把打包期反转过的那段转回规范形态 掩码必须先于VM解开
            if ((uPolicy & 4u) != 0 && iEngLen > 0) Array.Reverse(rgEntry, iEngOff, iEngLen);
            int iPre = PreKernEntry(rgEntry, iLayer - iPrekLen, iPrekLen, iEngOff, iEngLen, rgHead, out uint uPre, out int iPrekUnit1);//跑前置内核才拿得到尾部roll钥匙字段的掩码 之后roll段才能解开
            if (iPre != 0) return Fail(iPre);
            uRollKey = BitConverter.ToUInt32(rgEntry, (int)lSz0 - Q.iTailLen + 8) ^ Q.TailKeyMaskOf(uPre);
            if (uPolicy != (BitConverter.ToUInt32(rgEntry, (int)lSz0 - Q.iTailLen + 20) ^ Q.TailKeyMaskOf(uPre))) return Fail(14);//布局策略期望值写在尾部+20 掩码来自roll钥匙字段 不跑前置内核解不出来 改策略码必然对不上
            if ((uPolicy & 0xFFu) == ((uPolicy ^ 0x5Au) & 0xFFu)) return Fail(14);//诱饵 与真判定同形且恒假x等于x异或M对非零M恒不成立
            if ((iCipher & 0x0Fu) == ((iCipher ^ 0x31u) & 0x0Fu)) return Fail(14);
            if (iRollLen > 0)
            {
                if (iPrekUnit1 <= 0) return Fail(7);//roll段掩码必须由前置容器单元在VM里生成 容器里没有单元1即判改过 不留IL回退以免可静态复算
                int iMk = PreKernRollMask(rgEntry, iLayer - iPrekLen, iPrekLen, iPrekUnit1, iEngOff, iEngLen, iRollOff, iRollLen, lSz0, uRollKey, rgHead);
                if (iMk != 0) return Fail(iMk);
            }
            if ((uPolicy & 2u) != 0 && iRollLen > 0) Array.Reverse(rgEntry, iRollOff, iRollLen);//打包端是先反转明文再掩码
            int iVm = VmMaskMix(rgEntry, iRollOff, iRollLen, iEngOff, iEngLen, rgHead, rgFold, lSz0, iLayer, iCipher, uChk, Q.TableDigest(rgSz), out ulong uMix, out ulong uSelfMix);
            Array.Clear(rgFold, 0, rgFold.Length);
            if (iVm != 0) return Fail(iVm);
            Array.Clear(rgHead, 0, rgHead.Length);

            byte[] rgImage = new byte[iCipher];
            Array.Copy(rgEntry, iLayer, rgImage, 0, iCipher);
            Array.Clear(rgEntry, 0, rgEntry.Length);

            Assembly asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgImage, 0, rgImage.Length));
            Array.Clear(rgImage, 0, rgImage.Length);//镜像交给运行时后本体可以丢掉 与载荷装载同一条路径
            MethodInfo? miEntry = asm.EntryPoint;
            if (miEntry == null) return Fail(6);
            object[] rgParams = miEntry.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { rgArgs };
            object? oRet = miEntry.Invoke(null, rgParams);
            return oRet is int iRet ? iRet : 0;
        }
        catch (Exception ex)
        {
#if DEBUG
            Log("host: " + ex.GetType().Name);
#else
            GC.KeepAlive(ex);
#endif
            Environment.FailFast(null);
            return 1;

        }
    }
    private delegate ulong vRun(IntPtr pBc, IntPtr pRegs, IntPtr pMem, ulong cbM);//引擎唯一导出 必须真委托类型Func<>会被marshaler拒绝

    private static uint uRollKey = 0x5EED0001u;//运行时从尾部取出 尾部存的是掩码后的值

    private static uint uRollSum = 0x5EED0003u;//入口层roll明文摘要 打包期写入 剥掉掩码后核对
    private static uint uEngSum = 0x5EED0004u;//入口层引擎明文摘要 打包期写入 剥掉掩码后核对

    private static readonly byte[] _tmplPerm = {//dispatch模板由打包期写入shim 含256B的opcode到槽表与h_perm的RVA
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,
        0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7,0xA7 };
    private static readonly byte[] _tmplRva = { 0x69,0x69,0x69,0x69,0x69,0x69,0x69,0x69 };
    [ThreadStatic] private static IntPtr _pRunTbl;
    [ThreadStatic] private static byte[] _rgPermShuf = null!;
    private static ulong _uRunSeed;
    private static int _iTblGen;

    private static ulong Mix64S(ulong u)
    {
        u ^= u >> 30; u *= 0xBF58476D1CE4E5B9UL;
        u ^= u >> 27; u *= 0x94D049BB133111EBUL;
        u ^= u >> 31;
        return u;
    }

    private static ulong RunSeedS(IntPtr pBase, IntPtr pRegs)
    {
        if (_uRunSeed != 0) return _uRunSeed;
        ulong u = (ulong)pBase.ToInt64() ^ ((ulong)pRegs.ToInt64() << 5);
        try { u ^= (ulong)(uint)Marshal.ReadInt32(new IntPtr(0x7FFE0000)) << 7; } catch { }   //KUSER_SHARED_DATA.TickCountLow
        try { u ^= (ulong)Marshal.ReadInt64(new IntPtr(0x7FFE0008)); } catch { }              //InterruptTime
        u ^= (ulong)Environment.TickCount64 * 0x9E3779B97F4A7C15UL;
        _uRunSeed = Mix64S(u) | 1;
        return _uRunSeed;
    }

    private static void InstallRunTablesShim(IntPtr pRegs, IntPtr pBase, int iTextVa, uint uChainIn)//逐单元重建dispatch表 前置内核也逐运行 自校验不过就回退内置表 绝不跳野地址
    {
        if (pBase == IntPtr.Zero || iTextVa == 0) return;
        if (_tmplPerm[0] == 0xA7 && _tmplPerm[255] == 0xA7) return;              //模板未写入就回退
        if (BitConverter.ToInt32(_tmplRva, 0) == 0x69696969) return;
        int iRva = BitConverter.ToInt32(_tmplRva, 0);
        long pHperm = pBase.ToInt64() + (iRva - iTextVa);
        long pHtbl = pHperm + 256;
        int iMis = 0;
        for (int i = 0; i < 256; i++) if (Marshal.ReadByte(new IntPtr(pHperm + i)) != _tmplPerm[i]) iMis++;
        long lMin = pBase.ToInt64(), lMax = lMin + 0x4000;
        int iBad = 0;
        for (int s0 = 0; s0 < 256; s0++)
        {
            long p = pHtbl + Marshal.ReadInt32(new IntPtr(pHtbl + s0 * 4));
            if (p < lMin || p >= lMax) iBad++;
        }
        if (iMis != 0 || iBad != 0) { return; }
        if (_pRunTbl == IntPtr.Zero) _pRunTbl = Marshal.AllocHGlobal(256 + 256 * 8);
        if (_rgPermShuf == null) _rgPermShuf = new byte[256];
        ulong uS = Mix64S(RunSeedS(pBase, pRegs) ^ ((ulong)uChainIn * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)System.Threading.Interlocked.Increment(ref _iTblGen) * 0xD6E8FEB86659FD93UL));
        for (int i = 0; i < 256; i++) _rgPermShuf[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            uS = Mix64S(uS + 0x9E3779B97F4A7C15UL);
            int j = (int)(uS % (ulong)(i + 1));
            byte b = _rgPermShuf[i]; _rgPermShuf[i] = _rgPermShuf[j]; _rgPermShuf[j] = b;
        }
        for (int op = 0; op < 256; op++)
        {
            int s0 = _tmplPerm[op];
            int s = _rgPermShuf[s0];
            Marshal.WriteByte(_pRunTbl, op, (byte)s);
            int iRel = Marshal.ReadInt32(new IntPtr(pHtbl + s0 * 4));
            Marshal.WriteInt64(_pRunTbl, 256 + s * 8, pHtbl + iRel);
        }
        Marshal.WriteInt64(pRegs, Q.CtxRunTbl, _pRunTbl.ToInt64());
        Marshal.WriteInt32(pRegs, Q.CtxRunTag, unchecked((int)Q.CtxRunTagVal));
    }

    [DllImport("kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
    private static extern IntPtr vAlloc(IntPtr p, UIntPtr cb, uint fl, uint pr);

    [DllImport("kernel32.dll", EntryPoint = "VirtualProtect", SetLastError = true)]
    private static extern bool vProt(IntPtr p, UIntPtr cb, uint np, out uint old);

    [DllImport("kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
    private static extern bool vFree(IntPtr p, UIntPtr cb, uint fl);//引擎映射用完即卸

    private static int RvaOff(byte[] rg, int iPe, ushort usOpt, uint uRva)
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
    private static IntPtr MapEngine(byte[] rg, out int iTextVa, out int iTextSz)//引擎裸映射 只搬.text与.rdata与.data 无导入表无重定位 与boot侧同一套做法
    {
        iTextVa = 0;
        iTextSz = 0;
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
        int cbMap = iTextSz + 0x1000 + (iDataSz > 0 ? iDataVa - iTextVa + iDataSz : 0) + (iData2Sz > 0 ? iData2Va - iTextVa + iData2Sz : 0) + 0x1000;
        IntPtr p = vAlloc(IntPtr.Zero, (UIntPtr)cbMap, 0x3000, 0x40);
        if (p == IntPtr.Zero) return IntPtr.Zero;
        Marshal.Copy(rg, iTextRaw, p, iTextSz);
        if (iDataSz > 0) Marshal.Copy(rg, iDataRaw, new IntPtr(p.ToInt64() + (iDataVa - iTextVa)), iDataSz);
        if (iData2Sz > 0) Marshal.Copy(rg, iData2Raw, new IntPtr(p.ToInt64() + (iData2Va - iTextVa)), iData2Sz);
        vProt(p, (UIntPtr)iTextSz, 0x20, out _);//.text置RX
        int iDataAt = (iTextSz + 0xFFF) & ~0xFFF;//数据区从整页边界起降为读写 不从.text尾部所在页起降 免得把代码页变成不可执行
        if (iDataAt < cbMap) vProt(new IntPtr(p.ToInt64() + iDataAt), (UIntPtr)(cbMap - iDataAt), 0x04, out _);
        return p;

    }
    private static uint EngineEntryRva(byte[] rg)//单导出引擎取唯一导出相对虚拟地址 具名表被去掉时退回函数表首项
    {
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe <= 0 || iPe + 24 > rg.Length) return 0;
        int iDataDir = iPe + 24 + (BitConverter.ToUInt16(rg, iPe + 24) == 0x20B ? 112 : 96);
        uint uExpRva = BitConverter.ToUInt32(rg, iDataDir);
        if (uExpRva == 0) return 0;
        ushort usOpt = BitConverter.ToUInt16(rg, iPe + 20);
        int iExp = RvaOff(rg, iPe, usOpt, uExpRva);
        if (iExp <= 0) return 0;
        int iNameCnt = BitConverter.ToInt32(rg, iExp + 0x18);
        int iFuncCnt = BitConverter.ToInt32(rg, iExp + 0x1C);
        int iFuncOff = RvaOff(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x1C));
        if (iFuncOff <= 0 || iFuncCnt <= 0) return 0;
        if (iNameCnt > 0)
        {
            int iNameOff = RvaOff(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x20));
            int iOrdOff = RvaOff(rg, iPe, usOpt, BitConverter.ToUInt32(rg, iExp + 0x24));
            if (iNameOff > 0 && iOrdOff > 0) return BitConverter.ToUInt32(rg, iFuncOff + BitConverter.ToUInt16(rg, iOrdOff) * 4);
        }
        return BitConverter.ToUInt32(rg, iFuncOff);

    }
    //入口层折叠量 掩码内核对固定材料的输出 明文回声对出同一值
    private static readonly byte[] rgZeroWipe = new byte[0x2000];
    private static void MemWipe(IntPtr p, int cb) { while (cb > 0) { int n = Math.Min(cb, rgZeroWipe.Length); Marshal.Copy(rgZeroWipe, 0, p, n); p = new IntPtr(p.ToInt64() + n); cb -= n; } }//块写清零 启动期调用百万级次数 逐字节写会慢到秒级
    private static uint TextFnv(IntPtr pBase, int iTextSz)//把引擎.text整段拷出在托管数组上算FNV-1a32 省掉每字节一次跨边界调用
    {
        if (iTextSz <= 0) return 0x811C9DC5u;
        byte[] rg = new byte[iTextSz];
        Marshal.Copy(pBase, rg, 0, iTextSz);
        uint u = 0x811C9DC5u;
        for (int i = 0; i < rg.Length; i++) { u ^= rg[i]; u *= 0x01000193u; }
        Array.Clear(rg, 0, rg.Length);
        return u;
    }
    private static int VmMaskMix(byte[] rgEntry, int iRollOff, int iRollLen, int iEngOff, int iEngLen, byte[] rgHead, byte[] rgFold, long lSz0, int iLayer, int iCipher, ulong uChk, ulong uTableDigest, out ulong uMix, out ulong uSelfMix)//返回0即成功 其余是失败阶段号
    {
        uMix = 0;
        uSelfMix = 0;
        if (iRollOff <= 0 || iRollLen < 12 || iEngLen < 0x40) return 7;
        if (iRollOff + iRollLen > rgEntry.Length || iEngOff + iEngLen > rgEntry.Length) return 7;//区间上界检查 长度与缓冲区大小不能直接比
        if ((Q.Hash32(rgEntry, iRollOff, iRollLen) ^ Q.uRollSumMask) != uRollSum) return 13;//掩码已剥掉 这里核对明文摘要 入口层引擎或roll被改过都必须在这里失败
        if ((Q.Hash32(rgEntry, iEngOff, iEngLen) ^ Q.uEngSumMask) != uEngSum) return 13;
        if (BitConverter.ToInt32(rgEntry, iRollOff) != Q.iRollUnits) return 7;//内核数目必须等于Q.iRollUnits
        int iRollSz = iRollLen;
        int iKernA = -1, iKernB = -1, iKernD = -1, iKernS = -1, iKernX = -1;
        int[] rgUnitOff = new int[16];//按序走链要覆盖全部前序单元 只记用得着的几枚会漏
        int iOff = 4;
        for (int i = 0; i < Q.iRollUnits; i++)
        {
            if (iOff + 4 > iRollSz) return 7;
            int iLen = BitConverter.ToInt32(rgEntry, iRollOff + iOff);
            iOff += 4;
            if (iLen < 8 || (iLen & 3) != 0 || (uint)iLen > (uint)iRollSz - (uint)iOff) return 7;//用减法iLen来自产物 加法会回绕
            if (i < 16) rgUnitOff[i] = iOff;
            if (i == 1) iKernA = iOff;
            if (i == 2) iKernB = iOff;
            if (i == 7) iKernD = iOff;
            if (i == 8) iKernS = iOff;
            if (i == 9) iKernX = iOff;
            iOff += iLen;
        }
        if (iKernA < 0 || iKernB < 0 || iKernD < 0 || iKernS < 0 || iKernX < 0) return 7;

        byte[] rgEngine = new byte[iEngLen];
        Array.Copy(rgEntry, iEngOff, rgEngine, 0, iEngLen);
        IntPtr pRoll = IntPtr.Zero, pBase = IntPtr.Zero;
        int iTextVa = 0, iTextSz = 0;
        try
        {
            pRoll = Marshal.AllocHGlobal(iRollSz);
            Marshal.Copy(rgEntry, iRollOff, pRoll, iRollSz);//整个roll照搬 内核指针指向它内部
            pBase = MapEngine(rgEngine, out iTextVa, out iTextSz);
            if (pBase == IntPtr.Zero) return 8;
            uint uRva = EngineEntryRva(rgEngine);
            if (uRva == 0 || uRva < (uint)iTextVa) return 10;
            uint uFn = TextFnv(pBase, iTextSz);//整段拷贝后算FNV 逐字节读会让每字节一次边界调用
            uint uCtx = uRollKey ^ uFn;
            //链式roll只能从单元0按序解 先走0到8拿各单元的链入状态 单元9自身用表项9
            uint[] rgCh = new uint[24];
            int iWalk = ChainWalk(pRoll, rgUnitOff, 8, uCtx, pBase, iTextSz, iTextVa, uRva, rgCh);
            if (iWalk != 0) return iWalk;
            //掩码内核取返回值 种子内核取M[0x20] 两枚都要与明文回声逐位相同
            int iRet = VmKernel(pRoll, iKernA, VmMat(rgHead, Q.uDomainSeed), uCtx, pBase, iTextSz, iTextVa, uRva, out uint uA, out _, rgCh[1]);
            if (iRet != 0) return iRet;
            iRet = VmKernel(pRoll, iKernB, VmMat(rgHead, uRollKey), uCtx, pBase, iTextSz, iTextVa, uRva, out _, out uint uB, rgCh[2]);
            if (iRet != 0) return iRet;
            if (uA == 0 || uB == 0) return 11;
            uMix = uA ^ uB;
            //roll与引擎的明文摘要由摘要内核执行 值不可静态复算 次序放在主内核之后 同一映射上的调用尽量少
            uint uD1, uD2;
            int iDg = VmDigest(pRoll, iKernD, uCtx, pBase, iTextSz, iTextVa, uRva, rgEntry, iRollOff, iRollLen, out uD1, rgCh[7]);
            if (iDg != 0) return iDg;
            iDg = VmDigest(pRoll, iKernD, uCtx, pBase, iTextSz, iTextVa, uRva, rgEntry, iEngOff, iEngLen, out uD2, rgCh[7]);
            if (iDg != 0) return iDg;
            if ((uD1 ^ Q.uRollSumMask) != uRollSum) return 13;
            if ((uD2 ^ Q.uEngSumMask) != uEngSum) return 13;
            //自指量由64位摘要单元在VM里算 这条派生不存在于IL中
            int iSf = VmDigest64(pRoll, iKernS, uCtx, pBase, iTextSz, iTextVa, uRva, rgFold, 0, rgFold.Length, out uSelfMix, rgCh[8]);
            if (iSf != 0) return iSf;
            //boot解密密钥流由ChaCha12流单元在VM里生成并就地XOR 不留IL回退以免密钥流本体可被静态复算
            if (Q.Hash64(rgEntry, iLayer, iCipher) != uChk) return 5;
            int iKs = VmStreamXor(pRoll, iKernX, uCtx, pBase, iTextSz, iTextVa, uRva, rgEntry, iLayer, iCipher,
                rgHead, lSz0, uTableDigest, 3, iCipher, uMix, uSelfMix, 0u, rgCh[9]);
            if (iKs != 0) return iKs;
#if DEBUG
            Log($"vm: a=0x{uA:X8} b=0x{uB:X8} rollKey=0x{uRollKey:X8}");
#endif
            return 0;
        }
        catch (Exception ex)
        {
            GC.KeepAlive(ex);//异常对象要显式引用 否则发布构建下该变量被判未使用
#if DEBUG
            Log($"vm: textVa={iTextVa} textSz={iTextSz} base=0x{(pBase == IntPtr.Zero ? 0 : pBase.ToInt64()):X} ex={ex.GetType().Name} {ex.Message}");
#endif
            return 12;
        }
        finally
        {
            if (pRoll != IntPtr.Zero) { for (int i = 0; i < iRollSz; i++) Marshal.WriteByte(pRoll, i, 0); Marshal.FreeHGlobal(pRoll); }
            Array.Clear(rgEngine, 0, rgEngine.Length);
            if (pBase != IntPtr.Zero) vFree(pBase, UIntPtr.Zero, 0x8000);//两次内核调用之后再没人碰引擎 整个映射归还 地址空间里不留解释器

        }
    }
    //prek链式容器按序解 只有先跑单元0才拿得到单元1的链入状态 单元0在缓冲内偏移8
    private static int PreKernWalk(byte[] rgEntry, int iPrekOff, IntPtr pBlob, int iPrekLen, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, out uint uChainOut)
    {
        uChainOut = 0;
        int iCnt = BitConverter.ToInt32(rgEntry, iPrekOff);
        if (iCnt < 1 || iCnt > 4) return 7;
        int iLen0 = BitConverter.ToInt32(rgEntry, iPrekOff + 4);
        if (iLen0 < 8 || (iLen0 & 3) != 0 || (uint)iLen0 > (uint)iPrekLen - 8u) return 7;//用减法iLen0来自产物 调用方已保证iPrekLen不小于12
        byte[] rgMat = Q.ChainMat();//链材料与打包端逐字节相同prek链也由执行输出推进
        int iRet = VmKernel(pBlob, 8, rgMat, uCtx, pBase, iTextSz, iTextVa, uRva, out uint uRet, out _, 0u, true);//走链 材料不进不出
        Array.Clear(rgMat, 0, rgMat.Length);
        if (iRet != 0) return iRet;
        if (uRet == 0xFFFFFFFFu || _uVmChainOut == 0) return 11;
        uChainOut = _uVmChainOut;
        return 0;

    }
    //前置容器第二单元 roll段掩码由VM生成 roll自身是单元的宿主容器不能自举 at-rest钥匙同前置容器
    private static int PreKernRollMask(byte[] rgEntry, int iPrekOff, int iPrekLen, int iUnit1, int iEngOff, int iEngLen, int iRollOff, int iRollLen, long lSz0, uint uRollKey, byte[] rgHead)//返回0即成功 其余是失败阶段号
    {
        byte[] rgEngine = new byte[iEngLen];
        Array.Copy(rgEntry, iEngOff, rgEngine, 0, iEngLen);
        IntPtr pBlob = IntPtr.Zero, pBase = IntPtr.Zero;
        int iTextVa = 0, iTextSz = 0;
        try
        {
            pBlob = Marshal.AllocHGlobal(iPrekLen);
            Marshal.Copy(rgEntry, iPrekOff, pBlob, iPrekLen);
            pBase = MapEngine(rgEngine, out iTextVa, out iTextSz);
            if (pBase == IntPtr.Zero) return 8;
            uint uRva = EngineEntryRva(rgEngine);
            if (uRva == 0 || uRva < (uint)iTextVa) return 10;
            uint uFn = TextFnv(pBase, iTextSz);//整段拷贝后算FNV 逐字节读会让每字节一次边界调用
            uint uCtx = Q.PreKernKey(rgHead) ^ uFn;
            //prek同样是链式容器 第二单元必须先解出第一单元
            int iWalk = PreKernWalk(rgEntry, iPrekOff, pBlob, iPrekLen, uCtx, pBase, iTextSz, iTextVa, uRva, out uint uCh1);
            if (iWalk != 0) return iWalk;
            //roll掩码流按kind等于1的掩码生成 key与nonce由段级派生
            int iRet = VmStreamXor(new IntPtr(pBlob.ToInt64() + (iUnit1 - iPrekOff)), 0, uCtx, pBase, iTextSz, iTextVa, uRva, rgEntry, iRollOff, iRollLen,
                rgHead, lSz0, uRollKey, 1, iRollLen, 0ul, 0ul, 0u, uCh1);
            return iRet;
        }
        catch (Exception ex)
        {
            GC.KeepAlive(ex);
            return 12;
        }
        finally
        {
            if (pBlob != IntPtr.Zero) { for (int i = 0; i < iPrekLen; i++) Marshal.WriteByte(pBlob, i, 0); Marshal.FreeHGlobal(pBlob); }
            Array.Clear(rgEngine, 0, rgEngine.Length);
            if (pBase != IntPtr.Zero) vFree(pBase, UIntPtr.Zero, 0x8000);

        }
    }
    //前置内核 at-rest钥匙只由镜像头派生 先于roll钥匙字段解开而执行
    private static int PreKernEntry(byte[] rgEntry, int iPrekOff, int iPrekLen, int iEngOff, int iEngLen, byte[] rgHead, out uint uPre, out int iUnit1)//返回0即成功 其余是失败阶段号
    {
        uPre = 0;
        iUnit1 = 0;
        if (iPrekOff <= 0 || iPrekLen < 12 || iPrekOff + iPrekLen > rgEntry.Length) return 7;
        int iCnt = BitConverter.ToInt32(rgEntry, iPrekOff);//单元数 0是解锁roll钥匙的掩码内核 1是splitmix流即roll段掩码
        if (iCnt < 1 || iCnt > 4) return 7;
        int iUnit0 = 0;
        int iWalk = iPrekOff + 4;
        for (int i = 0; i < iCnt; i++)
        {
            if (iWalk + 4 > iPrekOff + iPrekLen) return 7;
            int iLen = BitConverter.ToInt32(rgEntry, iWalk);
            iWalk += 4;
            if (iLen < 8 || (iLen & 3) != 0 || iWalk + iLen > iPrekOff + iPrekLen) return 7;
            if (i == 0) iUnit0 = iWalk;
            if (i == 1) iUnit1 = iWalk;
            iWalk += iLen;
        }
        if (iUnit0 <= 0) return 7;
        byte[] rgEngine = new byte[iEngLen];
        Array.Copy(rgEntry, iEngOff, rgEngine, 0, iEngLen);
        IntPtr pBlob = IntPtr.Zero, pBase = IntPtr.Zero;
        int iTextVa = 0, iTextSz = 0;
        try
        {
            pBlob = Marshal.AllocHGlobal(iPrekLen);
            Marshal.Copy(rgEntry, iPrekOff, pBlob, iPrekLen);
            pBase = MapEngine(rgEngine, out iTextVa, out iTextSz);
            if (pBase == IntPtr.Zero) return 8;
            uint uRva = EngineEntryRva(rgEngine);
            if (uRva == 0 || uRva < (uint)iTextVa) return 10;
            uint uFn = TextFnv(pBase, iTextSz);//整段拷贝后算FNV
            uint uCtx = Q.PreKernKey(rgHead) ^ uFn;
            int iRet = VmKernel(pBlob, iUnit0 - iPrekOff, VmMat(rgHead, Q.uDomainSeed), uCtx, pBase, iTextSz, iTextVa, uRva, out uint uA, out _);
            if (iRet != 0) return iRet;
            if (uA == 0) return 11;
            uPre = uA;
            return 0;
        }
        catch (Exception ex)
        {
            GC.KeepAlive(ex);
            return 12;
        }
        finally
        {
            if (pBlob != IntPtr.Zero) { for (int i = 0; i < iPrekLen; i++) Marshal.WriteByte(pBlob, i, 0); Marshal.FreeHGlobal(pBlob); }
            Array.Clear(rgEngine, 0, rgEngine.Length);
            if (pBase != IntPtr.Zero) vFree(pBase, UIntPtr.Zero, 0x8000);

        }
    }
    //按序走链 从单元0起用空材料逐个调用到iUpTo收集每单元的链入状态 跳号或乱序或替换单元都会让某个前序单元的键不成立
    private static int ChainWalk(IntPtr pRoll, int[] rgUnitOff, int iUpTo, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, uint[] rgState)
    {
        uint uCh = 0;
        byte[] rgMat = Q.ChainMat();//链材料必须与打包端逐字节相同 链由执行输出推进
        for (int k = 0; k <= iUpTo && k < rgUnitOff.Length; k++)
        {
            if (rgUnitOff[k] <= 0) { Array.Clear(rgMat, 0, rgMat.Length); return 7; }
            if (rgState != null) rgState[k] = uCh;
            int iRet = VmKernel(pRoll, rgUnitOff[k], rgMat, uCtx, pBase, iTextSz, iTextVa, uRva, out uint uRet, out _, uCh, true);//走链 材料不进不出
            if (iRet != 0) { Array.Clear(rgMat, 0, rgMat.Length); return iRet; }
            if (uRet == 0xFFFFFFFFu || _uVmChainOut == 0) { Array.Clear(rgMat, 0, rgMat.Length); return 11; }//前序单元的键不成立即h_bad
            uCh = _uVmChainOut;
        }
        Array.Clear(rgMat, 0, rgMat.Length);
        if (rgState != null && iUpTo + 1 < rgState.Length) rgState[iUpTo + 1] = uCh;
        return 0;

    }
    //摘要内核驱动 分段喂入 单段不超过数据页 头4字节是续算状态 状态用返回的v0跨段携带
    private static int VmDigest(IntPtr pRoll, int iKernOff, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, byte[] rg, int iOff, int cb, out uint uDigest, uint uChainIn = 0)
    {
        uDigest = 0x811C9DC5u;
        const int iChunkMax = 0x07F0;//状态头4字节加2032等于2036 留在材料窗口内
        for (int iAt = 0; iAt < cb; iAt += iChunkMax)
        {
            int n = Math.Min(iChunkMax, cb - iAt);
            byte[] rgSlice = new byte[n + 4];
            BitConverter.GetBytes(uDigest).CopyTo(rgSlice, 0);
            Array.Copy(rg, iOff + iAt, rgSlice, 4, n);
            int iRet = VmKernel(pRoll, iKernOff, rgSlice, uCtx, pBase, iTextSz, iTextVa, uRva, out uint uRet, out _, uChainIn);
            Array.Clear(rgSlice, 0, rgSlice.Length);
            if (iRet != 0) return iRet;
            uDigest = uRet;
        }
        return 0;

    }
    //摘要内核驱动64位形态 分段喂入 头8字节是续算状态 结果在M[0x20..0x28)
    private static int VmDigest64(IntPtr pRoll, int iKernOff, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, byte[] rg, int iOff, int cb, out ulong uDigest, uint uChainIn = 0)
    {
        uDigest = 0xCBF29CE484222325UL;
        const int iChunkMax = 0x07F0;//状态头8字节加2032等于2040 不贴VM栈区
        for (int iAt = 0; iAt < cb; iAt += iChunkMax)
        {
            int n = Math.Min(iChunkMax, cb - iAt);
            byte[] rgSlice = new byte[n + 8];
            BitConverter.GetBytes(uDigest).CopyTo(rgSlice, 0);
            Array.Copy(rg, iOff + iAt, rgSlice, 8, n);
            int iRet = VmCall64(pRoll, iKernOff, rgSlice, uCtx, pBase, iTextSz, iTextVa, uRva, out ulong uMem64, uChainIn);
            Array.Clear(rgSlice, 0, rgSlice.Length);
            if (iRet != 0) return iRet;
            uDigest = uMem64;
        }
        return 0;

    }
    //ChaCha12流XOR驱动与打包侧掩码成对 key32与nonce12段级派生 ctr经协议槽首4字节携带 分段喂入
    private static int VmStreamXor(IntPtr pRoll, int iKernOff, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva,//材料布局 key32在0x00 槽在0x20 数据在0x30 QR下标表在材料之外逐调用布
        byte[] rg, int iOff, int cb, byte[] rgHead, long lEntrySize, ulong uRollKey, int iKind, int cbFull,
        ulong uVmMix, ulong uSelfMix, uint uCtr, uint uChainIn = 0)
    {
        const int iChunk = A0.QStream.dm;//分块1984即31乘64 块对齐 尾段由内核按cb逐字节截断
        A0.QStream.KeyNonceOf(rgHead, lEntrySize, uRollKey, iKind, cbFull, uVmMix, uSelfMix, out byte[] rgK, out byte[] rgN);
        byte[] rgTbl = A0.QStream.TblBytes();
        try
        {
            for (int iAt = 0; iAt < cb; iAt += iChunk)
            {
                int n = Math.Min(iChunk, cb - iAt);
                byte[] rgMat = new byte[A0.QStream.Data + n];
                Array.Copy(rgK, 0, rgMat, A0.QStream.Key, 32);
                Array.Copy(rgN, 0, rgMat, A0.QStream.Slot, 12);
                BitConverter.GetBytes(uCtr).CopyTo(rgMat, A0.QStream.Slot + 12);
                Array.Copy(rg, iOff + iAt, rgMat, A0.QStream.Data, n);
                int iRet = VmCall64Io(pRoll, iKernOff, rgMat, uCtx, pBase, iTextSz, iTextVa, uRva, out ulong uMem64, uChainIn, rgTbl);
                if (iRet != 0) { Array.Clear(rgMat, 0, rgMat.Length); return iRet; }
                Array.Copy(rgMat, A0.QStream.Data, rg, iOff + iAt, n);//只回写真实长度
                uCtr = (uint)uMem64;//内核把新计数器写在槽首 4B
                Array.Clear(rgMat, 0, rgMat.Length);
            }
            return 0;
        }
        finally
        {
            Array.Clear(rgK, 0, rgK.Length);
            Array.Clear(rgN, 0, rgN.Length);
            Array.Clear(rgTbl, 0, rgTbl.Length);
        }
    }
    private static int VmCall64(IntPtr pRoll, int iKernOff, byte[] rgMat, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, out ulong uMem64, uint uChainIn = 0)//只读结果型调用 单元不修改材料
    {
        return VmCore64(pRoll, iKernOff, rgMat, uCtx, pBase, iTextSz, iTextVa, uRva, out uMem64, false, uChainIn);

    }
    private static int VmCall64Io(IntPtr pRoll, int iKernOff, byte[] rgMat, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, out ulong uMem64, uint uChainIn = 0, byte[]? rgTbl = null)//材料进出型调用 单元就地改写材料 必须把VM内存里的材料回写到调用方缓冲
    {
        return VmCore64(pRoll, iKernOff, rgMat, uCtx, pBase, iTextSz, iTextVa, uRva, out uMem64, true, uChainIn, rgTbl);

    }
    private static int VmCore64(IntPtr pRoll, int iKernOff, byte[] rgMat, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, out ulong uMem64, bool bIo, uint uChainIn = 0, byte[]? rgTbl = null)//单次调用并取回M[0x20..0x28) 即64位结果 bIo决定材料是否回写
    {
        uMem64 = 0;
        IntPtr pRegs = IntPtr.Zero, pMem = IntPtr.Zero;
        try
        {
            var fn = Marshal.GetDelegateForFunctionPointer<vRun>(new IntPtr(pBase.ToInt64() + (int)uRva - iTextVa));
            pRegs = Q.CtxAlloc();//CtxAlloc内已8字节清零
            pMem = Q.CtxAlloc(0x2000);//每块调一次
            Marshal.WriteInt64(pRegs, 2 * 8, 0L);
            Marshal.WriteInt64(pRegs, 3 * 8, rgMat.Length);
            Marshal.WriteInt32(pRegs, 128, unchecked((int)uCtx));
            Marshal.WriteInt64(pRegs, 128 + 8, pBase.ToInt64());
            Marshal.WriteInt32(pRegs, 128 + 16, iTextSz);
            Marshal.WriteInt32(pRegs, 128 + 20, unchecked((int)uChainIn));//链入 单元i的键只在其前序被解过后才成立
            Marshal.WriteInt32(pRegs, 128 + 32, Q.iWindowBytes);//声明材料窗口 引擎超窗即拒
            Marshal.WriteInt32(pRegs, 128 + 40, bIo ? 1 : 0);

            uint[] rgT = PageTbl(); StageTo(pMem, rgMat, rgT);//本运行的页置换下把材料落页
            if (rgTbl != null) for (int i = 0; i < rgTbl.Length; i++) Marshal.WriteByte(pMem, Q.PagePhys(rgT, A0.QStream.Tbl + i), rgTbl[i]);//ChaCha12的QR下标表 在材料之外
            Q.PageTblToPtr(_pTblMem, rgT);
            Marshal.WriteInt64(pRegs, Q.CtxPageTbl, _pTblMem.ToInt64());//把本运行页表交给引擎
            InstallRunTablesShim(pRegs, pBase, iTextVa, uChainIn);//本单元的dispatch表 前置内核也逐运行
            ulong uRun = fn(new IntPtr(pRoll.ToInt64() + iKernOff), pRegs, pMem, 0x2000);
            if (uRun == unchecked((ulong)-1)) return 11;//解码或执行失败即h_bad 一律响亮失败
            _uVmChainOut = (uint)Marshal.ReadInt32(pRegs, 128 + 24);//链出 由引擎按本单元明文折叠回传
            if (bIo) UnstageFrom(rgMat, pMem, rgT);//材料进出型把VM内存里被改写的材料按表取回

            uMem64 = (ulong)Marshal.ReadInt64(pMem, Q.PagePhys(rgT, 0x20));
            return 0;
        }
        catch { return 12; }
        finally
        {
            if (pRegs != IntPtr.Zero) { MemWipe(pRegs, 192); Marshal.FreeHGlobal(pRegs); }
            if (pMem != IntPtr.Zero) { MemWipe(pMem, 0x2000); Marshal.FreeHGlobal(pMem); }

        }
    }
    private static uint _uVmChainOut;//链式容器的链状态回传 由单元调用核心写入 由ChainWalk与各驱动读取

    //每运行一张页置换 惰性生成 驱动按它落页 引擎按同一张表翻译 表恒等时与不设表逐字节等价 页2与页3是VM栈保持恒等
    private static uint[] _rgPageTbl = null!;
    private static IntPtr _pTblMem = IntPtr.Zero;
    private static uint[] PageTbl()
    {
        if (_rgPageTbl == null)
        {
            uint uSeed = RuntimeSeed();
            if (uSeed == 0) uSeed = 0x5EED5EEDu;
            _rgPageTbl = Q.PagePerm(uSeed);
            _pTblMem = Marshal.AllocHGlobal(16);
            Q.PageTblToPtr(_pTblMem, _rgPageTbl);
#if DEBUG
            Log($"layout: pages={_rgPageTbl[0]},{_rgPageTbl[1]},{_rgPageTbl[2]},{_rgPageTbl[3]}");
#endif
        }
        return _rgPageTbl;
    }
    private static void StageTo(IntPtr pMem, byte[] rgMat, uint[] rgT)//按表落页 虚拟到物理
    {
        for (int i = 0; i < rgMat.Length && i < 0x1000; i++)
        {
            int iSlot = (int)(rgT[i >> Q.iPageShift] & (uint)(Q.iPageSlots - 1));
            Marshal.WriteByte(pMem, (iSlot << Q.iPageShift) | (i & Q.iPageMask), rgMat[i]);
        }
    }
    private static void UnstageFrom(byte[] rgMat, IntPtr pMem, uint[] rgT)//按表取回 物理到虚拟
    {
        for (int i = 0; i < rgMat.Length && i < 0x1000; i++)
        {
            int iSlot = (int)(rgT[i >> Q.iPageShift] & (uint)(Q.iPageSlots - 1));
            rgMat[i] = Marshal.ReadByte(pMem, (iSlot << Q.iPageShift) | (i & Q.iPageMask));

        }
    }
    private static byte[] VmMat(byte[] rgHead, uint uKey)
    {
        byte[] rgMat = new byte[32];
        BitConverter.GetBytes(uKey).CopyTo(rgMat, 0);
        Array.Copy(rgHead, 0, rgMat, 4, Math.Min(28, rgHead.Length));
        return rgMat;

    }
    private static int VmKernel(IntPtr pRoll, int iKernOff, byte[] rgMat, uint uCtx, IntPtr pBase, int iTextSz, int iTextVa, uint uRva, out uint uRet, out uint uMem32, uint uChainIn = 0, bool bMatIo = false)//单次内核调用 regs与mem每次都重新清零 内存输出前4字节回传
    {
        uRet = 0;
        uMem32 = 0;
        IntPtr pRegs = IntPtr.Zero, pMem = IntPtr.Zero;
        try
        {
            var fn = Marshal.GetDelegateForFunctionPointer<vRun>(new IntPtr(pBase.ToInt64() + (int)uRva - iTextVa));
            pRegs = Q.CtxAlloc();//CtxAlloc已清零
            pMem = Q.CtxAlloc(0x2000);
            Marshal.WriteInt64(pRegs, 2 * 8, 0L);
            Marshal.WriteInt64(pRegs, 3 * 8, rgMat.Length);
            Marshal.WriteInt32(pRegs, 128, unchecked((int)uCtx));
            Marshal.WriteInt64(pRegs, 128 + 8, pBase.ToInt64());
            Marshal.WriteInt32(pRegs, 128 + 16, iTextSz);
            Marshal.WriteInt32(pRegs, 128 + 20, unchecked((int)uChainIn));//链入 单元i的键只在其前序被解过后才成立
            Marshal.WriteInt32(pRegs, 128 + 32, Q.iWindowBytes);//声明材料窗口 引擎超窗即拒
            Marshal.WriteInt32(pRegs, 128 + 40, bMatIo ? 1 : 0);

            uint[] rgT = PageTbl(); StageTo(pMem, rgMat, rgT);//本运行的页置换下把材料落页
            Q.PageTblToPtr(_pTblMem, rgT);
            Marshal.WriteInt64(pRegs, Q.CtxPageTbl, _pTblMem.ToInt64());//把本运行页表交给引擎
            InstallRunTablesShim(pRegs, pBase, iTextVa, uChainIn);//本单元的内核调用表随本次页置换重派
            uRet = (uint)fn(new IntPtr(pRoll.ToInt64() + iKernOff), pRegs, pMem, 0x2000);
            _uVmChainOut = (uint)Marshal.ReadInt32(pRegs, 128 + 24);//链状态回传 由单元调用核心写入
            uMem32 = (uint)Marshal.ReadInt32(pMem, Q.PagePhys(rgT, 0x20));//结果窗口经页表取回
            if (bMatIo) UnstageFrom(rgMat, pMem, rgT);//材料进出型把VM内存里被改写的材料按表取回

            return 0;
        }
        catch { return 12; }
        finally
        {
            if (pRegs != IntPtr.Zero) { MemWipe(pRegs, 192); Marshal.FreeHGlobal(pRegs); }
            if (pMem != IntPtr.Zero) { MemWipe(pMem, 0x2000); Marshal.FreeHGlobal(pMem); }

        }
    }
    private static int Fail(int iStage)//失败编号只在Debug构建写日志 发布期静默FailFast
    {
#if DEBUG
        Log("h:" + iStage + " " + rgStage[iStage]);
#endif
        Environment.FailFast(null);
        return 1;

    }
    private static string SelfPath()
    {
#if NET6_0_OR_GREATER
        return Environment.ProcessPath ?? throw new InvalidOperationException();
#else
        return Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException();
#endif

    }
    private static int FindMarker(byte[] rg)//签名扫描 命中处前8字节就是头部偏移
    {
        for (int i = 8; i + 32 <= rg.Length; i++)
        {
            if (rg[i] != rgSig[0]) continue;
            int k = 1;
            while (k < 32 && rg[i + k] == rgSig[k]) k++;
            if (k == 32) return i;
        }
        return -1;

    }
    private static uint ReadU32(byte[] rg, ref int iPos)
    {
        uint u = BitConverter.ToUInt32(rg, iPos);
        iPos += 4;
        return u;

    }
    private static int ReadI32(byte[] rg, ref int iPos)
    {
        int i = BitConverter.ToInt32(rg, iPos);
        iPos += 4;
        return i;

    }
    private static long ReadI64(byte[] rg, ref int iPos)
    {
        long l = BitConverter.ToInt64(rg, iPos);
        iPos += 8;
        return l;

    }
    private static byte ReadU8(byte[] rg, ref int iPos) => rg[iPos++];

    private static string ReadStr(byte[] rg, ref int iPos)
    {
        int iLen = rg[iPos++];
        if ((iLen & 0x80) != 0) iLen = (iLen & 0x7F) << 8 | rg[iPos++];
        string s = Encoding.UTF8.GetString(rg, iPos, iLen);
        iPos += iLen;
        return s;

    }
    //每进程重派 敏感副本的落点逐运行不同 所以固定偏移的dump脚本失效 产物字节不变只垫一段随机抖动
    //必须放在DEBUG之外 Release也要生效 日志本身只在DEBUG下可用
    private static IntPtr _pLayoutPad = IntPtr.Zero;
    private static uint _uLayoutFp = 0x811C9DC5u;
    private static uint RuntimeSeed() => unchecked((uint)Environment.TickCount ^ ((uint)Process.GetCurrentProcess().Id * 2654435761u));
    private static void S5LayoutJitter()
    {
        if (_pLayoutPad != IntPtr.Zero) return;
        uint uS = RuntimeSeed();
        int iPad = (int)(uS & 0x3FF) * 64 + 64;
        _pLayoutPad = Marshal.AllocHGlobal(iPad);
        for (int i = 0; i < iPad; i += 64) Marshal.WriteByte(_pLayoutPad, i, (byte)(uS >> (i & 7)));
        _uLayoutFp = ((0x811C9DC5u ^ (uint)iPad) * 0x01000193u) ^ (uint)_pLayoutPad.ToInt64() ^ uS;
#if DEBUG
        Log($"layout: pad={iPad} fp=0x{_uLayoutFp:X8}");
#endif
    }

#if DEBUG
    private const string sLogVar = "LAMARR_BOOT_LOG";//LAMARR_BOOT_LOG指向文件时落盘 整块只进Debug构建
    private static readonly string[] rgStage =
    {
        "marker not found", "bad header offset", "bad entry count", "bad entry bounds",
        "bad entry tail", "entry check failed", "no entry point",
        "roll shape", "engine map", "-", "engine export", "kernel returned zero", "engine call threw",
        "vm digest", "entry layout"
    };

    private static void Log(string sLine)
    {
        try
        {
            string sPath = Environment.GetEnvironmentVariable(sLogVar) ?? "";
            if (sPath.Length == 0) return;
            File.AppendAllText(sPath, sLine + Environment.NewLine);
        }
        catch { }
    }//Log方法整体属于Debug块 闭括号必须在#endif之前 否则Release下会多出一个类外括号
#endif
}
