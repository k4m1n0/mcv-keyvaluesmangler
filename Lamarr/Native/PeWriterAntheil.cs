using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

internal partial class PeWriterAntheil
{
    #region 常量字段

    private byte[] rgStubCode = null!;
    private uint uStubEntryOff;

    private byte[] rgPayload = null!;
    private int iPeOff, iOptOff, iSecOff;
    private ushort usSecCount;
    private uint uSectAlign, uFileAlign, uSizeOfHdrs;
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
    private string sRtcVersion = "5.0.0";
    public void SetRtcVersion(string sV) { sRtcVersion = sV; }
    private string sTiered = "";
    public void SetTiered(string sM) { sTiered = sM; }

    private byte[] rgBoot = null!;
    private List<(ulong Hi, ulong Lo)> rgBootCrcs = new();
    private byte[] rgLamApp = null!;
    private uint uLamAppRaw, uLamAppRawSize;
    private int iMainEntry = -1;
    private string sMainName = "";
    private string sPayloadPath = "";

    private static readonly byte[] rgSignature = new byte[]
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

    private byte[] rgKBsjb = null!;

    //rdata段首IA64密钥区 派生全部密钥
    private byte[] rgHead = null!;
    private static readonly uint uPermMul = 0x01000193u;
    private static readonly uint uPermAdd = 0x9E3779B9u;
    //移动注入开关 LAMARR_MOVE_INJECT=1启用 默认就地扰动 可用LAMARR_MOVE_ONLY限定目标方法
    private static readonly bool bMoveInject = Environment.GetEnvironmentVariable("LAMARR_MOVE_INJECT") == "1";
    private string sSeed = "";
    private const int iBsjbLen = 64;

    //常量对A^B参与运算
    private static readonly uint uLK0A = 0x12345678, uLK0B = 0x9328CBBD;//0x811C9DC5
    private static readonly uint uLK1A = 0x11111111, uLK1B = 0x111110A2;//0x000001B3
    private static readonly uint uLGAA = 0x0F0F0F0F, uLGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint uLLCA = 0x0A0A0A0A, uLLCB = 0x0A136C07;//0x0019660D
    private static readonly uint uLQCA = 0x5A5A5A5A, uLQCB = 0x6634A905;//0x3C6EF35F

    private byte[] rgDecoder = null!;
    private byte[] rgJitHook = null!;
    private string sJitHookPath = "";
    private byte[] rgPheropod = null!;

    #endregion
    #region 依赖配置

    public void LoadJitHook(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"JitHook not found: {sPath}");
        rgJitHook = File.ReadAllBytes(sPath);
        sJitHookPath = sPath;
    }

    public void LoadPheropod(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"Pheropod not found: {sPath}");
        rgPheropod = File.ReadAllBytes(sPath);
    }

    public void SetCompressDeps(bool b) => bCompressDeps = b;

    private string sDecoderPath = "";
    private string sMethodDictPath = "";
    private bool bCompressDeps = true;

    private readonly HashSet<string> rgEncryptDeps = new(StringComparer.OrdinalIgnoreCase);
    //私有依赖显式指定加密走jithook 其余依赖只压缩
    public void SetMethodDict(string sPath) => sMethodDictPath = sPath;

    public void SetEncryptDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            rgEncryptDeps.Add(sName);
            rgEncryptDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : sName + ".dll");
        }
    }

    private readonly HashSet<string> rgNoCompressDeps = new(StringComparer.OrdinalIgnoreCase);
    //显式指定不压缩的依赖 明文存储
    public void SetNoCompressDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            rgNoCompressDeps.Add(sName);
            rgNoCompressDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : sName + ".dll");
        }
    }

    #endregion
    #region 打包入口

    public void LoadStub(string sPath)
    {
        byte[] rgStub = File.ReadAllBytes(sPath);
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
        uFileAlign = 0x200;//文件对齐0x200
        uSizeOfHdrs = BitConverter.ToUInt32(rgPayload, iOptOff + 60);

        lBundleHeaderOffset = FindMarkerOffset(rgPayload);
        if (lBundleHeaderOffset <= 0)
            throw new InvalidOperationException("Input is not a .NET single-file bundle (marker not found)");
        iBundleDataStart = (int)ParseBundleFirstEntryOffset(rgPayload, lBundleHeaderOffset);
        if (iBundleDataStart <= 0 || iBundleDataStart >= rgPayload.Length)
            throw new InvalidOperationException("Invalid bundle layout");
        sMainName = Path.GetFileNameWithoutExtension(sPath) + ".dll";
    }

    public void LoadBoot(string sPath)
    {
        //重打包即全链换钥
        rgHead = GenIa64Area(64);
        sSeed = DeriveSeed(rgHead);
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Bootstrapper is not a PE: {sPath}");
        rg = bMoveInject
            ? MoveInjectIl(rg, new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks))
            : InjectIlNoise(rg, new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks));
        //jithook安装后的方法才加密 先按原名加密再重命名
        string[] rgPairs = { "uLK0A","uLK0B","uLK1A","uLK1B","uLGAA","uLGAB","uLLCA","uLLCB","uLQCA","uLQCB",
            "uKS0A","uKS0B","uKS1A","uKS1B","uKS2A","uKS2B","uKS3A","uKS3B",
            "uQ1A","uQ1B","uQ2A","uQ2B","uQ3A","uQ3B","uQ4A","uQ4B","uR1A","uR1B","uR2A","uR2B",
            "uV1A","uV1B","uV2A","uV2B","uV3A","uV3B","uV4A","uV4B","uV5A","uV5B","uV6A","uV6B","uV7A","uV7B" };
        var fldMap = FieldTokens(rg, rgPairs);
        var rgVal = new Dictionary<uint, uint>();
        foreach (var kv in fldMap) rgVal[kv.Value] = 0;
        uint uCctor;
        ReadCctorValues(rg, rgVal, out uCctor);
        rgBootCrcs = MethodEncryptor.EncryptAll(rg, Fnv1a(SeedKey(Encoding.ASCII.GetBytes(sSeed))), BootTokens(rg));
        rgBoot = BootRenamer.Rename(rg, sMethodDictPath);
        var rgNew = new Dictionary<uint, uint>();
        var rnd5 = new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
        for (int i = 0; i < rgPairs.Length; i += 2)
        {
            uint uTokA = fldMap[rgPairs[i]], uTokB = fldMap[rgPairs[i + 1]];
            uint uReal = rgVal[uTokA] ^ rgVal[uTokB];
            uint uA2 = (uint)rnd5.Next() | ((uint)rnd5.Next() << 16);
            rgNew[uTokA] = uA2; rgNew[uTokB] = uA2 ^ uReal;
        }
        WriteCctorValues(rgBoot, uCctor, rgNew);
    }

    public void LoadDecoder(string sPath)
    {
        if (!File.Exists(sPath))
            throw new InvalidOperationException($"Decoder not found: {sPath}");
        rgDecoder = File.ReadAllBytes(sPath);
        sDecoderPath = sPath;
    }

    public void Pack(string sOutPath)
    {
        if (rgBoot == null)
            throw new InvalidOperationException("Bootstrapper not loaded (--boot required)");
        if (rgDecoder == null || rgDecoder.Length == 0)
            throw new InvalidOperationException("Decoder not loaded (--decoder required)");

        //seed已提前生成 重建bundle条目
        RebuildBundle(sSeed);

        ComputeLayout();
        PatchStubVars(sOutPath);

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
    }

    private void ComputeBundleStart()
    {
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        lMarkerRaw = uStubRaw + uStubRawSize;
        uLamAppRaw = AlignUp((uint)(lMarkerRaw + 40), uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);
        lBundleStart = uBundleRaw;
    }

    private void ComputeLayout()
    {
        ComputeBundleStart();
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }
    #endregion
}
