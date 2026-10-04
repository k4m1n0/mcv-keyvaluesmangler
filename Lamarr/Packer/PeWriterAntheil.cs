using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.Packer;

internal partial class PeWriterAntheil
{
    #region 常量字段

    private byte[] rgStubCode = null!;
    private uint uStubEntryOff;

    private byte[] rgPayload = null!;
    private int iPeOff, iOptOff, iSecOff;
    private ushort usSecCount;
    private uint uSectAlign, uFileAlign, uSizeOfHdrs;
    private byte[]? rgResPayload;//产物的.rsrc与载荷整节继承 载荷没有资源节就不写该节
    private uint uResRva, uResLen, uResDirOff, uResDirSize;
    private long lBundleHeaderOffset;
    private int iBundleDataStart;

    private long lNewBundleHeaderOffset;
    private uint uStubRaw, uStubRawSize;
    private long lMarkerRaw;
    private long lBundleStart;

    private byte[] rgBundleData = null!;
    private byte[] rgNewHeader = null!;
    private long lBundleDataLen;
    private long[] rgBundleOffsets = null!;
    private long[] rgBundleCsz = null!;
    private long[] rgBundleSz = null!;
    private int iNewRtcIdx = -1;
    private int iIdxRtc = -1;
    private int iEntryCount;
    private readonly HashSet<string> rgStripDeps = new(StringComparer.OrdinalIgnoreCase);
    private string? sRtcVersion = null;//null=不改版本
    public void SetRtcVersion(string sV) { sRtcVersion = string.IsNullOrEmpty(sV) ? null : sV; }
    private string sTiered = "";
    public void SetTiered(string sM) { sTiered = sM; }
    private int iPayloadMajor = 0;//载荷TFM主版本=stub的PREFMAJ
    public void SetPayloadMajor(int iV) { iPayloadMajor = iV; }
    private string? sRollForward = null;//null=取载荷值 未发布则用打包端默认策略 取该主版本最新运行时 缺失取更高一级
    public void SetRollForward(string? sV) { sRollForward = string.IsNullOrEmpty(sV) ? null : sV; }
    internal const string sRollForwardDefault = "Major";

    private byte[] rgBoot = null!;
    private byte[]? rgShim = null;//入口层镜像 空则用boot本体

    public void LoadShim(string sPath)
    {
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Entry layer is not a PE: {sPath}");
        rgShim = rg;
        if (uVmSeed == 0) throw new InvalidOperationException("SetVmSeed must run before LoadShim (entry layer domain seed)");
    }
    private List<uint> rgBootCrcs = new();
    private List<byte> rgBootVars = new();//boot各加密体的实际变体顺序 与rgBootCrcs同序
    private static bool IsTripwire(uint uV) => uV == 0xDEADBEEFu || uV == 0xCAFEBABEu || uV == 0x12345678u;//boot侧按此判篡改 命中即静默跳过校验与签名注入
    private byte[] rgLamApp = null!;
    private uint uLamAppRaw, uLamAppRawSize;
    private int iMainEntry = -1;
    private string sMainName = "";
    private string sPayloadPath = "";

    private static readonly byte[] rgSignatureDotnet = new byte[]//.NET单文件bundle的公开签名 只用于解析输入载荷与替换入口层镜像里那份
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

    private static byte[] rgSigOut = null!;//锚点键由GenKBsjb派生stub里的gKBsjb是同一份 标记记录与入口层校验与shim搜索都用它

    private byte[] rgKBsjb = null!;

    private byte[] rgHead = null!;//rdata段首密钥区 派生全部密钥
    private byte[]? rgReplayHead;
    private string? sReplaySeed;
    private byte[] rkBuildKey = Array.Empty<byte>();//根密钥K是32B 只在打包进程内 永不进产物
    private static readonly uint uPermMul = 0x01000193u;
    private static readonly uint uPermAdd = 0x9E3779B9u;
    //这些旋钮只在triage构建里读环境变量 发行打包器里是固定常量 调弱防御这条路在发行二进制里不存在
#if LAMARR_TRIAGE
    private static readonly bool bMoveInject = Environment.GetEnvironmentVariable("LAMARR_MOVE_INJECT") != "0";//启用移动注入 置0退回就地扰动 LAMARR_MOVE_ONLY限定目标方法
    private static readonly bool bNoMutate = Environment.GetEnvironmentVariable("LAMARR_NO_MUTATE") == "1";//LAMARR_NO_MUTATE跳过boot IL扰动LAMARR_NO_RENAME跳过方法改名
    private static readonly bool bNoRename = Environment.GetEnvironmentVariable("LAMARR_NO_RENAME") == "1";
    private static readonly bool bNoEngineSum = Environment.GetEnvironmentVariable("LAMARR_NO_ENGINE_SUM") == "1";//缺type 8摘要或摘要不符时不注入
    private static readonly bool bBadEngineSum = Environment.GetEnvironmentVariable("LAMARR_ENGINE_SUM_BAD") == "1";
    private static readonly bool bNoIdxPerm = Environment.GetEnvironmentVariable("LAMARR_NO_IDX_PERM") == "1";//编号置换由vmSeed派生 表项按槽位索引 钩子按指纹内容寻址 LAMARR_NO_IDX_PERM=1回退恒等
    private static readonly bool bLayout = Environment.GetEnvironmentVariable("LAMARR_LAYOUT") == "1";//构建侧布局/条目清单 只走stdout 不进产物
#else
    private static readonly bool bMoveInject = true;
    private static readonly bool bNoMutate = false;
    private static readonly bool bNoRename = false;
    private static readonly bool bNoEngineSum = false;
    private static readonly bool bBadEngineSum = false;
    private static readonly bool bNoIdxPerm = false;
    private static readonly bool bLayout = false;
#endif
    private const int iIdxSpace = 4096;//槽位空间上限 超过即构建期报错
    private int[] rgIdxPerm = null!;
    private uint[] rgIdxCrc = null!;//按槽位索引存放的真实指纹
    private byte[] rgIdxVar = null!;//按槽位索引存放的变体标记
    private bool[] rgIdxUsed = null!;

    private void EnsureIdxMap()
    {
        if (rgIdxPerm != null) return;
        rgIdxPerm = new int[iIdxSpace];
        for (int i = 0; i < iIdxSpace; i++) rgIdxPerm[i] = i;
        if (!bNoIdxPerm)
        {
            ulong uS = uVmSeed ^ 0x9E3779B97F4A7C15UL;//xorshift64流做Fisher-Yates
            if (uS == 0) uS = 0xA5A5A5A5A5A5A5A5UL;
            for (int i = iIdxSpace - 1; i > 0; i--)
            {
                uS ^= uS << 13; uS ^= uS >> 7; uS ^= uS << 17;
                int j = (int)(uS % (ulong)(i + 1));
                (rgIdxPerm[i], rgIdxPerm[j]) = (rgIdxPerm[j], rgIdxPerm[i]);
            }
        }
        rgIdxCrc = new uint[iIdxSpace];
        rgIdxVar = new byte[iIdxSpace];
        rgIdxUsed = new bool[iIdxSpace];
    }

    private void PutIdxEntry(int iSlot, uint uCrc, byte uVar)//散列一条真实表项到它的槽位
    {
        rgIdxCrc[iSlot] = uCrc; rgIdxVar[iSlot] = uVar; rgIdxUsed[iSlot] = true;
    }
    private string sSeed = "";
    private const int iBsjbLen = 64;

    private static readonly uint uLK0A = 0x12345678, uLK0B = 0x9328CBBD;//0x811C9DC5常量对 A^B参与运算
    private static readonly uint uLK1A = 0x11111111, uLK1B = 0x111110A2;//0x000001B3
    private static readonly uint uLGAA = 0x0F0F0F0F, uLGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint uLLCA = 0x0A0A0A0A, uLLCB = 0x0A136C07;//0x0019660D
    private static readonly uint uLQCA = 0x5A5A5A5A, uLQCB = 0x6634A905;//0x3C6EF35F

    private byte[] rgDecoder = null!;
    private byte[] rgJitHook = null!;
    private string sJitHookPath = "";
    private byte[] rgPheropod = null!;
    private byte[] rgEngine = null!;//Pianola原生镜像
    private uint uEngineSum;//引擎.text摘要 载入时固化 escrow与roll键都用它
    private uint uJitSum;//jithook与decoder的.text摘要 载入时固化 期望值用它
    private uint uDecSum;
    private byte[] rgRoll = null!;//region type 7=roll字节码 打包端按种子现生成
    private byte[] rgPreKern = null!;//入口层前置内核容器(单元1=钥匙掩码 单元9=ChaCha12流) 在roll之前执行 只由镜像头派生钥匙
    private byte[] rgRollEnt = null!;//入口层用的一份 反转是就地做的 必须先留克隆
    private byte[] rgEngineEnt = null!;//入口层用的一份 同样要留克隆
    private uint uRollKeyEntry;//入口层自带roll的at-rest钥匙 打包期只写进入口层尾部的掩码字段
    private uint uCctorBake;//boot cctor的写入锚点LoadBoot里记下 供二次写入派生值
    private static readonly string[] rgBakeNames = { "uBA0A","uBA0B","uBA1A","uBA1B","uBA2A","uBA2B","uBA3A","uBA3B",//写入槽的名字要在改名之前解析token改名前后不变 所以早解析晚写值
                                                     "uBB0A","uBB0B","uBB1A","uBB1B","uBB2A","uBB2B","uBB3A","uBB3B",
                                                     "uBCA","uBCB","uBDA","uBDB",
                                                     "uBT0A","uBT0B","uBT1A","uBT1B","uBT2A","uBT2B","uBT3A","uBT3B" };//必经路径票据互校的4个种子对
    private uint[] rgBakeToks = null!;
    private ulong uVmSeed;//roll种子由CLI传--seed决定载体ISA与at-rest密钥

    #endregion
    #region 依赖配置

    public void LoadJitHook(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"JitHook not found: {sPath}");
        rgJitHook = File.ReadAllBytes(sPath);
        int nSlot = Z0SlotFill(rgJitHook, "jithook");//等长插槽重写 与诱饵共用同一套池与目录解析
        if (nSlot >= 0) Console.WriteLine($"[jithook] slots rewritten bytes={nSlot}");
        PeScrub.Scrub(rgJitHook);
        uJitSum = EngineTextSum(rgJitHook);
        sJitHookPath = sPath;
    }

    public void LoadPheropod(string sPath)
    {
        rgPheropod = File.Exists(sPath)//路径优先 缺失用程序集内嵌那份
            ? File.ReadAllBytes(sPath)
            : ReadEmbeddedZ0() ?? throw new InvalidOperationException(sPath.Length != 0 ? $"Pheropod not found: {sPath}" : "Pheropod not found: this build carries no embedded copy");
        int iDbg = Z0DebugOfs(rgPheropod, out int nDbg);//调试目录项随后会被PeScrub清掉 位置先记下来
        PeScrub.Scrub(rgPheropod);
        byte[] rgPre = (byte[])rgPheropod.Clone();//变异前留一份 自检比压缩输出
        Z0Mutate(rgPheropod, iDbg, nDbg);//按释放种子改一份 诱饵与压缩端共用
        Z0Verify(rgPre, rgPheropod);
        OpenZ0(LayOutZ0(rgPheropod));
    }

    public void LoadEngine(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"engine image not found: {sPath}");
        rgEngine = File.ReadAllBytes(sPath);
        PeScrub.Scrub(rgEngine);
        rgEngineEnt = (byte[])rgEngine.Clone();//区域写入会就地把引擎反转 入口层这份先留克隆
        uEngineSum = EngineTextSum(rgEngine);//.text摘要须载入时固化 加密后再算只得FNV初值
    }

    public void SetBuildKey(byte[] rgK)//--key与--key-file给出的根密钥 必须在LoadBoot之前调用
    {
        if (rgK.Length != 32) throw new InvalidOperationException("build key must be 32 bytes (64 hex chars)");
        rkBuildKey = (byte[])rgK.Clone();
        uVmSeed = Kdf64(rgK, "chain:vmseed");
        A0.Q.uDomainSeed = DomainSeedOf(uVmSeed);
    }

    public void SetReplayMaterial(ulong uSeed, byte[] rgHead, string sSeedHex)//重放 材料全部来自escrow 不需要K 逐字节复述那一次发布
    {
        uVmSeed = uSeed;
        A0.Q.uDomainSeed = DomainSeedOf(uSeed);
        rgReplayHead = rgHead;
        sReplaySeed = sSeedHex;
    }

    internal static uint DomainSeedOf(ulong uSeed)//域种子由发布种子派生 换产物即换域 入口层镜像里的那份由PatchShimDomainSeed写入
    {
        ulong u = 0xCBF29CE484222325uL;
        for (int i = 0; i < 8; i++) { u ^= (byte)(uSeed >> (i * 8)); u *= 0x100000001B3uL; }
        uint uRet = (uint)(u ^ (u >> 32));
        return uRet | 1u;//派生值恒非0 把派生结果0这种病态输入与没派生区分开
                          //域种子的初始值不是0 忘了设置就是静默错派生 靠调用顺序保证 三处都在设置它
    }

    public void SetReplayHead(byte[] rgH) { rgReplayHead = rgH; }//用清单头材料与种子重打包 逐字节复现那次发布

    private Random NewRng(uint uSalt)//不依赖时钟 由运行种子与产物头材料派生
    {
        return new Random(RngSeed(uSalt));
    }

    private int RngSeed(uint uSalt)//盐值派生的int种子 随机源按它构造
    {
        unchecked
        {
            ulong u = ShapeMix(uSalt);
            u ^= u >> 30; u *= 0xBF58476D1CE4E5B9UL; u ^= u >> 27; u *= 0x94D049BB133111EBUL; u ^= u >> 31;
            return (int)u ^ (int)(u >> 32);
        }
    }

    private static bool MutationSane(byte[] rgBefore, byte[] rgAfter, string[] rgIntended, bool bEquiv, out string sChanged)//扰动增长方法体并移位元数据 解析不过或哈希锁定却变动的体让产物死在启动期
    {
        sChanged = "";
        //只有目标方法可不同 别的差异会增长并切到相邻方法 但IL校验仍会通过
        try
        {
            var htBefore = AllMethodBodies(rgBefore);
            var htAfter = AllMethodBodies(rgAfter);
            foreach (var kv in htBefore)
            {
                if (Array.IndexOf(rgIntended, kv.Key) >= 0) continue;
                if (!htAfter.TryGetValue(kv.Key, out byte[]? rgC) || rgC.Length != kv.Value.Length)
                {
                    sChanged = $"{kv.Key} (length)";
                    return false;
                }
                for (int i = 0; i < rgC.Length; i++) if (rgC[i] != kv.Value[i]) { sChanged = $"{kv.Key} (body)"; return false; }
            }
        }
        catch (Exception ex) { sChanged = $"enumerate: {ex.GetType().Name}"; return false; }
        try
        {
            using var ms = new MemoryStream(rgAfter, writable: false);
            using var per = new PEReader(ms);
            var mr = per.GetMetadataReader();
            foreach (var mdh in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(mdh);
                if (md.RelativeVirtualAddress == 0) continue;
                byte[]? rgIl = per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
                if (rgIl == null) { sChanged = $"{mr.GetString(md.Name)} (unparsable)"; return false; }
                if (!IlSanity(rgIl)) { sChanged = $"{mr.GetString(md.Name)} (il sanity)"; return false; }
            }
        }
        catch (Exception ex) { sChanged = $"parse: {ex.GetType().Name}"; return false; }
        int iEquivOk = 0;//扰动后必须与原体行为相同 短变长补空操作之外的差异都拒
        if (bEquiv)
        {
            foreach (string sName in rgIntended)
            {
                byte[]? rgA = MethodIlByName(rgBefore, sName);
                byte[]? rgC = MethodIlByName(rgAfter, sName);
                if (rgA == null || rgC == null) continue;
                bool bSame = rgA.Length == rgC.Length;
                if (bSame)
                    for (int i = 0; i < rgA.Length; i++) if (rgA[i] != rgC[i]) { bSame = false; break; }
                if (bSame) continue;
                if (!IlEquivalent(rgA, rgC, out string sEq)) { sChanged = $"{sName} (not equivalent: {sEq})"; return false; }
                iEquivOk++;
            }
        }
        if (bEquiv) Console.WriteLine($"  il_mutate: equivalence ok ({iEquivOk} method(s) re-encoded)");
        return true;
    }

    private static byte[]? MethodBodyBytes(byte[] rgD, int iRva)//比整个方法体而非只比IL 增长会移位元数据 头破坏时只比IL看不出来
    {
        try
        {
            int iPe = BitConverter.ToInt32(rgD, 0x3C);
            ushort usOpt = BitConverter.ToUInt16(rgD, iPe + 20);
            ushort usNum = BitConverter.ToUInt16(rgD, iPe + 6);
            int iOff = RvaToOffset(rgD, iPe, usOpt, usNum, (uint)iRva);
            if (iOff <= 0 || iOff + 12 > rgD.Length) return null;
            byte b0 = rgD[iOff];
            int iHdr, iCs;
            if ((b0 & 3) == 2) { iHdr = 1; iCs = b0 >> 2; }
            else if ((b0 & 3) == 3) { iHdr = 12; iCs = BitConverter.ToInt32(rgD, iOff + 4); }
            else return null;
            if (iCs <= 0 || iOff + iHdr + iCs > rgD.Length) return null;
            byte[] rgB = new byte[iHdr + iCs];
            Array.Copy(rgD, iOff, rgB, 0, rgB.Length);
            return rgB;
        }
        catch { return null; }
    }

    private static Dictionary<string, byte[]> AllMethodBodies(byte[] rgD)
    {
        var rgOut = new Dictionary<string, byte[]>();
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var mdh in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(mdh);
            int iRva = md.RelativeVirtualAddress;
            if (iRva == 0) continue;
            string sName = mr.GetString(md.Name);
            byte[]? rgB = MethodBodyBytes(rgD, iRva);
            if (rgB != null) rgOut[sName] = rgB;
        }
        return rgOut;
    }

    private static byte[]? MethodIlByName(byte[] rgD, string sName)
    {
        try
        {
            using var ms = new MemoryStream(rgD, writable: false);
            using var per = new PEReader(ms);
            var mr = per.GetMetadataReader();
            foreach (var mdh in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(mdh);
                if (mr.GetString(md.Name) != sName) continue;
                return per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
            }
        }
        catch { }
        return null;
    }

    public void SetCompressDeps(bool b) => bCompressDeps = b;

    private string sDecoderPath = "";
    private string sMethodDictPath = "";
    private bool bCompressDeps = true;

    private readonly HashSet<string> rgEncryptDeps = new(StringComparer.OrdinalIgnoreCase);
    public void SetMethodDict(string sPath) => sMethodDictPath = sPath;//私有依赖显式指定加密走jithook 其余依赖只压缩

    public void SetEncryptDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            rgEncryptDeps.Add(sName);
            rgEncryptDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : $"{sName}.dll");
        }
    }

    private readonly HashSet<string> rgNoCompressDeps = new(StringComparer.OrdinalIgnoreCase);
    public void SetNoCompressDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            rgNoCompressDeps.Add(sName);
            rgNoCompressDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : $"{sName}.dll");
        }
    }

    #endregion
    #region 打包入口

    public void LoadStub(string sPath)
    {
        byte[] rgStub = File.ReadAllBytes(sPath);
        int nSlot = Z0SlotFill(rgStub, "stub");//等长插槽重写 与诱饵共用同一套池与目录解析
        if (nSlot >= 0) Console.WriteLine($"[stub] slots rewritten bytes={nSlot}");
        int iPe = BitConverter.ToInt32(rgStub, 0x3C);
        ushort usCnt = BitConverter.ToUInt16(rgStub, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgStub, iPe + 20);
        int iS = iPe + 24 + usOpt;
        uint uVa = 0, uRaw = 0, uVs = 0;
        for (int i = 0; i < usCnt; i++)
        {
            int iOff = iS + i * 40;
            if (BitConverter.ToUInt32(rgStub, iOff + 12) != 0)
            {
                uVa = BitConverter.ToUInt32(rgStub, iOff + 12);
                uRaw = BitConverter.ToUInt32(rgStub, iOff + 20);
                uVs = BitConverter.ToUInt32(rgStub, iOff + 8);
                break;
            }
        }
        rgStubCode = new byte[uVs];
        Array.Copy(rgStub, (int)uRaw, rgStubCode, 0, (int)Math.Min(uVs, rgStub.Length - uRaw));

        int iFound = -1;
        for (int i = rgStubCode.Length - 4; i >= 0; i--)
        {
            if (rgStubCode[i] == 0x55 && rgStubCode[i + 1] == 0x48 &&
                rgStubCode[i + 2] == 0x8B && rgStubCode[i + 3] == 0xEC)
            {
                iFound = i;
                break;
            }
        }
        if (iFound < 0)
            throw new InvalidOperationException("StubEntry not found in stub DLL");
        uStubEntryOff = (uint)iFound;
    }

    public void LoadPayload(string sPath)
    {
        sPayloadPath = sPath;
        rgPayload = File.ReadAllBytes(sPath);
        iPeOff = BitConverter.ToInt32(rgPayload, 0x3C);
        if (iPeOff + 0x18 >= rgPayload.Length || BitConverter.ToUInt32(rgPayload, iPeOff) != 0x4550)
            throw new InvalidOperationException("Input is not a PE");
        iOptOff = iPeOff + 24;
        usSecCount = BitConverter.ToUInt16(rgPayload, iPeOff + 6);
        iSecOff = iPeOff + 24 + BitConverter.ToUInt16(rgPayload, iPeOff + 20);
        uSectAlign = BitConverter.ToUInt32(rgPayload, iOptOff + 32);
        uFileAlign = BitConverter.ToUInt32(rgPayload, iOptOff + 36);//文件对齐取自载荷头 不假定512
        if (uFileAlign == 0 || (uFileAlign & (uFileAlign - 1)) != 0)
            throw new InvalidOperationException($"payload FileAlignment is not a power of two: 0x{uFileAlign:X}");
        uSizeOfHdrs = BitConverter.ToUInt32(rgPayload, iOptOff + 60);

        lBundleHeaderOffset = FindMarkerOffset(rgPayload);
        if (lBundleHeaderOffset <= 0)
            throw new InvalidOperationException("Input is not a .NET single-file bundle (marker not found)");
        iBundleDataStart = (int)ParseBundleFirstEntryOffset(rgPayload, lBundleHeaderOffset);
        if (iBundleDataStart <= 0 || iBundleDataStart >= rgPayload.Length)
            throw new InvalidOperationException("Invalid bundle layout");
        sMainName = $"{Path.GetFileNameWithoutExtension(sPath)}.dll";
        if (PeWriter.TryExtractRes(rgPayload, out byte[]? rgResP, out uint uRvaP, out uint uLenP, out uint uDirOffP, out uint uDirSizeP))//产物的.rsrc与载荷整节继承 载荷没有资源节就保持2节且DD2为0
        {
            rgResPayload = rgResP; uResRva = uRvaP; uResLen = uLenP; uResDirOff = uDirOffP; uResDirSize = uDirSizeP;
            Console.WriteLine($"  payload .rsrc: rva=0x{uRvaP:X} len={uLenP} dirOff=0x{uDirOffP:X} dirSize={uDirSizeP}");
        }
        else
        {
            rgResPayload = null; uResRva = 0; uResLen = 0; uResDirOff = 0; uResDirSize = 0;
        }
    }

    public void LoadBoot(string sPath)
    {
        if (rgReplayHead != null) { rgHead = rgReplayHead; rgReplayHead = null; }//重打包即全链换钥 头材料取自清单
        else
        {
            if (rkBuildKey.Length != 32) throw new InvalidOperationException("build key not set: call SetBuildKey (or SetReplayMaterial) before LoadBoot");
            rgHead = GenHead(rkBuildKey);
        }
        sSeed = sReplaySeed ?? KdfSeedHex(rkBuildKey);//种子由构建侧给出 不从产物头部反推
        sReplaySeed = null;
        rgSigOut = GenAnchorSig(sSeed);//锚点键随产物派生LoadShim可能早于RebuildBundle
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Bootstrapper is not a PE: {sPath}");
        PeScrub.Scrub(rg);//内层boot dll同样清构建特征
        byte[] rgPreMutate = rg;
        rg = bNoMutate
            ? rg
            : (bMoveInject ? MoveInjectIl(InjectIlNoise(rg, NewRng(0x12u)), NewRng(0x11u)) : InjectIlNoise(rg, NewRng(0x12u)));
        if (!ReferenceEquals(rg, rgPreMutate))
        {
            string[] rgIntended = bNoMutate ? Array.Empty<string>() : (bMoveInject ? BootNames.MoveInjected.Concat(BootNames.NoiseInjected).ToArray() : BootNames.NoiseInjected);
            if (!MutationSane(rgPreMutate, rg, rgIntended, !bMoveInject, out string sVictim))
            {
                Console.WriteLine($"  il_mutate: mutated image rejected ({sVictim}) -> mutation dropped");//IL不可解析或未扰动体发生移动的boot 一律不发布
                rg = rgPreMutate;
            }
        }
#if LAMARR_TRIAGE
        string sDumpBoot = Environment.GetEnvironmentVariable("LAMARR_DUMP_BOOT") ?? "";//导出扰动后未加密的boot镜像 交运行时JIT
        if (sDumpBoot.Length > 0) File.WriteAllBytes(sDumpBoot, rg);
#endif
        var rgPairs = BootNames.PairFields;//发行构建不导出boot镜像
        var fldMap = FieldTokens(rg, rgPairs);
        {
            var rgB = FieldTokens(rg, rgBakeNames);//改名之前解析写入槽token
            foreach (var s in rgBakeNames) if (!rgB.ContainsKey(s)) throw new InvalidOperationException($"bake slot missing in boot (pre-rename): {s}");
            rgBakeToks = Array.ConvertAll(rgBakeNames, s => rgB[s]);
        }
        var rgVal = new Dictionary<uint, uint>();
        foreach (var kv in fldMap) rgVal[kv.Value] = 0;
        uint uCctor;
        ReadCctorValues(rg, rgVal, out uCctor);
        uCctorBake = uCctor;//派生值稍后写入同一张cctor表
        CaptureExpInputs(rg);
        byte[] rgMatB = SeedKey(Encoding.ASCII.GetBytes(sSeed));//boot方法占签名表前段base=0 序号是uKey2派生输入 掩码域与jit密钥域分开
        CheckEncryptOrder(rg);//加密前排序校验 发现就绪前调用即打包失败
        EnsureIdxMap();//boot批也走同一张置换表 表项散列到各自槽位
        var rgBootSlots = new List<int>();
        rgBootCrcs = MethodEncryptor.EncryptAll(rg, Fnv1aK1(rgMatB), MaskKeyOf(rgMatB), BootTokens(rg), 0, rgBootVars, rgIdxPerm, rgBootSlots);
        for (int i = 0; i < rgBootCrcs.Count; i++) PutIdxEntry(rgBootSlots[i], rgBootCrcs[i], rgBootVars[i]);
        Array.Clear(rgMatB, 0, rgMatB.Length);
        BootRenamer.SetSeed(uVmSeed);//改名后的boot方法名同样要可复现
        rgBoot = bNoRename ? rg : BootRenamer.Rename(rg, sMethodDictPath);
        var rgNew = new Dictionary<uint, uint>();
        var rnd5 = NewRng(0x13u);
        for (int i = 0; i < rgPairs.Length; i += 2)
        {
            uint uTokA = fldMap[rgPairs[i]], uTokB = fldMap[rgPairs[i + 1]];
            uint uReal = rgVal[uTokA] ^ rgVal[uTokB];
            uint uA2;//魔数boot侧判篡改 命中即静默跳过校验与签名注入 抽到就重抽
            int iTry = 0;
            do { uA2 = (uint)rnd5.Next() | ((uint)rnd5.Next() << 16); }
            while (++iTry < 64 && (IsTripwire(uA2) || IsTripwire(uA2 ^ uReal)));
            if (IsTripwire(uA2) || IsTripwire(uA2 ^ uReal))
                throw new InvalidOperationException($"constant randomization hit a tripwire magic: A=0x{uA2:X8} B=0x{uA2 ^ uReal:X8}");
            rgNew[uTokA] = uA2; rgNew[uTokB] = uA2 ^ uReal;
        }
        WriteCctorValues(rgBoot, uCctor, rgNew);
#if LAMARR_TRIAGE
        string sDumpFinal = Environment.GetEnvironmentVariable("LAMARR_DUMP_BOOT_FINAL") ?? "";//导出bundle携带的最终boot镜像 已改名已加密
        if (sDumpFinal.Length > 0) File.WriteAllBytes(sDumpFinal, rgBoot);
#endif
        //发行构建不导出最终boot镜像
    }

    public void LoadDecoder(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"Decoder not found: {sPath}");
        rgDecoder = File.ReadAllBytes(sPath);
        int nSlot = Z0SlotFill(rgDecoder, "iamdec");//等长插槽重写 与诱饵共用同一套池与目录解析
        if (nSlot >= 0) Console.WriteLine($"[decoder] slots rewritten bytes={nSlot}");
        PeScrub.Scrub(rgDecoder);
        uDecSum = EngineTextSum(rgDecoder);
        sDecoderPath = sPath;
    }

    //escrow字段与读写全在Escrow分部 写只需公钥 读需离线私钥

    internal static List<string> MaterialLeak(byte[] rgArt, byte[] rgK, string sSeed)//产物里不得出现根密钥K或任何KdfLabel输出 命中即构建失败 这是防密钥写进产物最值钱的一道闸
    {
        var rgHit = new List<string>();
        void Scan(byte[] rgNeedle, string sWhat)
        {
            if (rgNeedle.Length == 0) return;//空needle在IndexOf下恒命中 必须跳过 否则报假泄漏
            if (rgArt.AsSpan().IndexOf(rgNeedle) >= 0) rgHit.Add(sWhat);
        }
        if (rgK.Length == 32)
        {
            Scan(rgK, "K(raw)");
            Scan(Encoding.ASCII.GetBytes(Convert.ToHexString(rgK)), "K(hex)");
            foreach (string sLabel in new[] { "chain:vmseed", "chain:seed", "chain:head" })
            {
                byte[] rg = KdfLabel(rgK, sLabel);
                Scan(rg, "Kdf(" + sLabel + ")");
                Scan(Encoding.ASCII.GetBytes(Convert.ToHexString(rg)), "Kdf(" + sLabel + ")(hex)");
            }
            byte[] rgPriv = KdfLabel(rgK, "seal:priv");//seal:priv的私钥材料绝不允许落盘pub_B是公开数据本来就在尾部seal区所以不扫
            Scan(rgPriv, "Kdf(seal:priv)");
            Scan(rgPriv[..32], "seal:priv(seed32)");
            Scan(Encoding.ASCII.GetBytes(Convert.ToHexString(rgPriv)), "Kdf(seal:priv)(hex)");
        }
        if (sSeed.Length > 0)
        {
            Scan(Encoding.ASCII.GetBytes(sSeed), "seed(ascii)");
            Scan(SeedKey(Encoding.ASCII.GetBytes(sSeed)), "SeedKey(seed)");
        }
        return rgHit;
    }

    void CheckMaterialLeakOrThrow(byte[] rgArt, string sSeed)
    {
        var rgHit = MaterialLeak(rgArt, rkBuildKey, sSeed);
        if (rgHit.Count > 0)
        {
            //把命中名同时写文件才可靠 因为构建链会折行stderr并吞掉stdout
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), $"lamarr_leak_{Environment.ProcessId}.txt"), string.Join("\n", rgHit)); } catch { }//按进程命名 并发与多用户构建不互踩
            Console.WriteLine("[leak] hit: " + string.Join(", ", rgHit));
            throw new InvalidOperationException("material leak: the artifact carries key material (" + string.Join(", ", rgHit) + ")");
        }
    }

    internal void Pack(string sOutPath)
    {
        if (rgBoot == null)
            throw new InvalidOperationException("Bootstrapper not loaded (--boot required)");
        if (rgDecoder == null || rgDecoder.Length == 0)
            throw new InvalidOperationException("Decoder not loaded (--decoder required)");
        if (rkBuildKey.Length != 0 && (rkBuildKey.Length != 32 || Array.TrueForAll(rkBuildKey, b => b == 0)))//K必须是空或32字节非全零 清零后再Pack要响亮报错 绝不静默出坏产物
            throw new InvalidOperationException("build key invalid (cleared, all-zero, or not 32B): pass --key/--key-file");
        bool bHadKey = rkBuildKey.Length == 32;
        WriteBootExpectations(rgBoot);//期望值量的是扰动后的boot 且必须写在入口层加密之前
        BakeRunTables(rgBoot, "boot");//dispatch模板 哨兵是字节模式 改名后再写无妨

        uint uRollKey = 0;
        if (uVmSeed != 0)
        {
            uRollKey = RollKey(sSeed);
            uRollKeyEntry = uRollKey;//入口层那份roll与引擎用同一把at-rest钥匙
            rgRoll = NotenrolleComp.BuildRollForSeed(uVmSeed, uRollKey, RunnerFor(rgEngine), out uint uChainTail);
            //链终态写在roll容器尾部并与roll钥匙异或boot用_cbRoll-4读它做自检锚点
            //掩码键必须用boot实际喂给引擎的那把ctx键 即roll钥匙异或引擎映射后.text的FNV
            {
                byte[] rgTail = new byte[rgRoll.Length + 4];
                Array.Copy(rgRoll, rgTail, rgRoll.Length);
                BitConverter.GetBytes(uChainTail ^ uRollKey ^ EngineFnMapped(rgEngine)).CopyTo(rgTail, rgRoll.Length);
                rgRoll = rgTail;
            }

            WriteExpShard2(BitConverter.ToUInt32(rgRoll, rgRoll.Length - 4));//第二份期望块的掩码就是运行期链尾 必须写在boot嵌进入口层之前 链尾只由引擎链产出

            rgRollEnt = (byte[])rgRoll.Clone();//区域写入会就地把roll反转 入口层这份先留克隆
            if (rgShim != null) { PatchShimSecrets(); PrepareShim(); }//域种子与roll钥匙都定了再改镜像 早于RebuildBundle
            if (rgShim != null)//前置内核 钥匙只由最终镜像头派生 必须在shim改完之后生成
            {
                byte[] rgHeadPre = new byte[Math.Min(A0.Q.iHeadSample, rgShim.Length)];
                Array.Copy(rgShim, 0, rgHeadPre, 0, rgHeadPre.Length);
                rgPreKern = NotenrolleComp.BuildPreKern(uVmSeed, A0.Q.PreKernKey(rgHeadPre), new[] { NotenrolleComp.KernMask, NotenrolleComp.KernChaCha12 }, RunnerFor(rgEngine));
                Array.Clear(rgHeadPre, 0, rgHeadPre.Length);
            }
        }
        if (rgShim != null && rgShim.Length > 0) BakeRunTables(rgShim, "shim");//shim侧同款写入 放在shim变异之后嵌进入口层之前
        RebuildBundle(sSeed);
        if (sEscrowPath.Length > 0)
        {
            string sOutDir = Path.GetDirectoryName(Path.GetFullPath(sOutPath))!;//拒绝写到产物同目录 否则随产物一起分发
            if (Path.GetDirectoryName(Path.GetFullPath(sEscrowPath)) == sOutDir)
                throw new InvalidOperationException("escrow must not be written into the artifact directory");
            if (!bSealOn) WriteEscrow(sEscrowPath, uRollKey, Path.GetFileName(sOutPath));//盖章时推迟到seal之后再写 否则会先落一份pubB与sig为空的弱escrow
        }

        ComputeLayout();
        PatchStubVars(sOutPath);

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
        byte[] rgFinal = File.ReadAllBytes(sOutPath);
        CheckMaterialLeakOrThrow(rgFinal, sSeed);//密钥材料不得出现在产物里 命中即构建失败
        if (bSealOn)//把签名锚pub_B与sig追加在文件尾部固定长度区 公开数据且不改PE结构
        {
            byte[] rgPubB = SealPubB(), rgSig;
            if (rkSealSig.Length == 64) rgSig = rkSealSig;//重放时直接复制escrow里的成品签名 不重签
            else
            {
                if (rkBuildKey.Length != 32) throw new InvalidOperationException("sealing needs the build key (or the escrow's finished seal)");
                rgSig = SealSig(rgFinal);
            }
            byte[] rgSeal = SealRegionOf(rgPubB, rgSig);
            if (!Ed25519.Verify(rgPubB, Sha512.Hash(rgFinal), rgSig))//交叉绑定 这把pub_B与σ必须真的覆盖即将写出的body 重放时成品签名来自escrow 覆盖不上就响失败
                throw new InvalidOperationException("seal materials do not match the produced body: the escrow's pubB/sig (or the build key) are inconsistent with this artifact"
                    + (rkSealSig.Length == 64 ? " -- replay note: a copied seal is only valid if *every* material input matched the escrow: the payload/mode/native fingerprints AND the switch set (--method-dict / --no-mutate / --no-rename / --compress-deps / --encrypt-deps / --no-compress-deps) -- different inputs => different body => a different line was produced." : ""));
            byte[] rgOut = new byte[rgFinal.Length + rgSeal.Length];
            rgFinal.CopyTo(rgOut, 0);
            rgSeal.CopyTo(rgOut, rgFinal.Length);
            File.WriteAllBytes(sOutPath, rgOut);
            rgFinal = rgOut;
            Console.WriteLine($"  seal: pubB={Convert.ToHexString(rgPubB)[..16]} ({(rkSealSig.Length == 64 ? "copied from escrow" : "signed")}; see --verify-seal)");
            if (sEscrowPath.Length > 0)//escrow里要放成品pubB与sig 重放靠复制 所以seal之后再写一次
                WriteEscrow(sEscrowPath, uRollKey, Path.GetFileName(sOutPath), rgPubB, rgSig);
        }
        if (!AuditEntryLayer(rgFinal, out string sPlain))
            throw new InvalidOperationException("entry layer audit failed: " + sPlain);//产物入口层里不许出现可读的roll或引擎
        if (bHadKey) Array.Clear(rkBuildKey, 0, rkBuildKey.Length);//K用完即清 重放路径本来就没K 之后还要Pack会在入口响亮抛错
    }

    internal void BakeBootValues(byte[] rgSiteKey, byte[] rgDisturbKey, uint uRollKey, uint uK1)//把引导期派生值以PairFields形式写进boot 对每个对子随机拆分并规避IsTripwire哨兵 产物里无固定常量
    {
        if (rgBakeToks == null) throw new InvalidOperationException("bake tokens not resolved: LoadBoot must run before the bake");
        var rgNew = new Dictionary<uint, uint>();
        var rnd = NewRng(0x2Au);
        void Bake(int i, uint uWant)
        {
            uint uA2; int iTry = 0;
            do { uA2 = (uint)rnd.Next() | ((uint)rnd.Next() << 16); }
            while (++iTry < 64 && (IsTripwire(uA2) || IsTripwire(uA2 ^ uWant)));
            if (IsTripwire(uA2) || IsTripwire(uA2 ^ uWant))
                throw new InvalidOperationException($"bake randomization hit a tripwire at slot {i}");
            rgNew[rgBakeToks[i * 2]] = uA2; rgNew[rgBakeToks[i * 2 + 1]] = uA2 ^ uWant;
        }
        for (int i = 0; i < 4; i++) Bake(i, BitConverter.ToUInt32(rgSiteKey, i * 4));
        for (int i = 0; i < 4; i++) Bake(4 + i, BitConverter.ToUInt32(rgDisturbKey, i * 4));
        Bake(8, uRollKey);
        Bake(9, uK1);
        for (int i = 0; i < 4; i++) Bake(10 + i, (uint)rnd.Next() | ((uint)rnd.Next() << 16));//4个票据种子 任意值只需哨兵规避 两半异或即为运行期用的种子
        WriteCctorValues(rgBoot, uCctorBake, rgNew);
    }

    private uint RollKey(string sSeed)//roll的at-rest密钥=种子材料派生 再混入引擎.text摘要
    {
        byte[] rgSk = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        uint uKey = MixAdj(rgSk) ^ uEngineSum;
        Array.Clear(rgSk, 0, rgSk.Length);
        return uKey;
    }

    private void ComputeBundleStart()
    {
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        lMarkerRaw = uStubRaw + uStubRawSize;
        uLamAppRaw = AlignUp((uint)(lMarkerRaw + 40), uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);//16字节余量 使容器数据从对齐点起 无格式含义
        lBundleStart = uBundleRaw;
    }

    private void ComputeLayout()
    {
        ComputeBundleStart();
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }
    #endregion
}
