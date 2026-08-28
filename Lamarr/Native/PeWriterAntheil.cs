using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

internal class PeWriterAntheil
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
    public void SetRtcVersion(string v) { sRtcVersion = v; }
    private string sTiered = "off";
    public void SetTiered(string m) { sTiered = m; }

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

    //假BSJB头与stub区XOR密钥
    private static readonly byte[] rgKBsjb = new byte[]
    {
        0x00,0x00,0x5A,0xA5,0x4B,0x42,0x0F,0xF0,
        0xA5,0x5A,0x0F,0x0F,0x5A,0xA5,0xF0,0x0F,
        0x0F,0xF0,0xA5,0x5A,0xF0,0x0F,0x5A,0xA5,
        0xA5,0x0F,0xF0,0x5A,0x0F,0x5A,0xA5,0xF0
    };

    //Boot区XOR
    private static readonly byte[] rgKSeed = new byte[]
    {
        0x10,0x00,0x02,0x80,0x28,0x01,0x90,0xEB,
        0xC3,0x00,0x10,0x02,0x01,0x80,0x28,0x90
    };

    //打包seed 提前生成 供方法体加密key派生
    private string sSeed = "";

    //lamapp区 BSJB头+seed payload布局
    private const int iBsjbLen = 64;
    private const int iSeedOff = 64;
    private const int iHashOff = 96;
    private const int iHeadOff = 100;
    private const int iTblOff = 116;

    //密钥派生 常量A^B参与运算
    private static readonly uint uLK0A = 0x12345678, uLK0B = 0x9328CBBD;//0x811C9DC5
    private static readonly uint uLK1A = 0x11111111, uLK1B = 0x111110A2;//0x01000193
    private static readonly uint uLGAA = 0x0F0F0F0F, uLGAB = 0x913876B6;//0x9E3779B9
    private static readonly uint uLLCA = 0x0A0A0A0A, uLLCB = 0x0A136C07;//0x0019660D
    private static readonly uint uLQCA = 0x5A5A5A5A, uLQCB = 0x6634A905;//0x3C6EF35F

    private byte[] rgDecoder = null!;
    private byte[] rgJitHook = null!;
    private string sJitHookPath = "";
    private byte[] rgPheropod = null!;

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

    public void SetCompressDeps(bool b) => _bCompressDeps = b;
    private string sDecoderPath = "";
    private bool _bCompressDeps = true;

    private readonly HashSet<string> _encryptDeps = new(StringComparer.OrdinalIgnoreCase);
    //私有依赖显式指定加密走jithook 其余依赖只压缩
    public void SetEncryptDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            _encryptDeps.Add(sName);
            _encryptDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : sName + ".dll");
        }
    }

    private readonly HashSet<string> _noCompressDeps = new(StringComparer.OrdinalIgnoreCase);
    //显式指定不压缩的依赖 明文存储
    public void SetNoCompressDeps(string s)
    {
        foreach (var sPart in s.Split(',', ';'))
        {
            var sName = sPart.Trim();
            if (sName.Length == 0) continue;
            _noCompressDeps.Add(sName);
            _noCompressDeps.Add(sName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? sName[..^4] : sName + ".dll");
        }
    }

    #endregion
    #region 入口

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
        sSeed = MakeSeed();
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Bootstrapper is not a PE: {sPath}");
        //jithook安装后才运行的方法(W1/X4/X5)才能加密 先加密(按原名找token)再重命名 顺序反了就找不到
        rgBootCrcs = MethodEncryptor.EncryptAll(rg, Fnv1a(SeedKey(Encoding.ASCII.GetBytes(sSeed))), BootTokens(rg));
        rgBoot = BootRenamer.Rename(rg);
    }

    private static List<uint> BootTokens(byte[] rgD)
    {
        var rg = new List<uint>();
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            string sName = mr.GetString(md.Name);
            if (sName == "W1" || sName == "X4" || sName == "X5" || sName == "TCheck" || sName == "X2")
                rg.Add(0x06000000u | (uint)MetadataTokens.GetRowNumber(mr, h));
        }
        return rg;
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

    private long FindMarkerOffset(byte[] rgB)
    {
        //查bundle marker 返回头部偏移
        for (int i = 0; i + 40 <= rgB.Length; i++)
            if (rgB[i + 8] == rgSignature[0] && MatchSig(rgB, i + 8))
                return BitConverter.ToInt64(rgB, i);
        return -1;
    }

    private bool MatchSig(byte[] rgB, int iOff)
    {
        for (int j = 0; j < 32; j++)
            if (rgB[iOff + j] != rgSignature[j]) return false;
        return true;
    }

    private long ParseBundleFirstEntryOffset(byte[] rgB, long lHeaderOff)
    {
        int iPos = (int)lHeaderOff;
        uint uMajor = ReadU32(rgB, ref iPos);
        ReadU32(rgB, ref iPos);
        int iN = ReadI32(rgB, ref iPos);
        ReadStr(rgB, ref iPos);
        if (uMajor >= 2)
        {
            ReadI64(rgB, ref iPos); ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos); ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos);
        }
        long lFirst = 0;
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            long lOff = ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos);
            if (uMajor >= 6)
                ReadI64(rgB, ref iPos);
            ReadU8(rgB, ref iPos);
            ReadStr(rgB, ref iPos);
            if (i == 0) lFirst = lOff;
        }
        return lFirst;
    }

    #endregion
    #region bundle重建

    private void RebuildBundle(string sSeed)
    {
        int iPos = (int)lBundleHeaderOffset;
        uint uMajor = ReadU32(rgPayload, ref iPos);
        ReadU32(rgPayload, ref iPos);
        int iN = ReadI32(rgPayload, ref iPos);
        string sBundleId = ReadStr(rgPayload, ref iPos);

        long lDepsSz = 0, lRtcSz = 0, lRtcHash = 0;
        if (uMajor >= 2)
        {
            ReadI64(rgPayload, ref iPos);
            lDepsSz = ReadI64(rgPayload, ref iPos);
            ReadI64(rgPayload, ref iPos);
            lRtcSz = ReadI64(rgPayload, ref iPos);
            lRtcHash = ReadI64(rgPayload, ref iPos);
        }

        var rgRel = new long[iN];
        var rgSz = new long[iN];
        var rgCsz = new long[iN];
        var rgType = new byte[iN];
        var rgName = new string[iN];
        iMainEntry = -1;
        iIdxRtc = -1;
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            rgRel[i] = ReadI64(rgPayload, ref iPos) - iBundleDataStart;
            rgSz[i] = ReadI64(rgPayload, ref iPos);
            if (uMajor >= 6)
                rgCsz[i] = ReadI64(rgPayload, ref iPos);
            else
                rgCsz[i] = 0;
            rgType[i] = ReadU8(rgPayload, ref iPos);
            rgName[i] = ReadStr(rgPayload, ref iPos);
            if (iMainEntry < 0 && rgName[i].Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                iMainEntry = i;
            if (iIdxRtc < 0 && rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                iIdxRtc = i;
        }
        if (iMainEntry < 0)
            throw new InvalidOperationException($"Bundle main assembly '{sMainName}' not found. Input: '{sPayloadPath}'");

        //区分条目 主程序/boot保留 托管dll剥离 其余进新bundle
        var keepIdx = new List<int>();
        var depIdx = new List<int>();
        rgStripDeps.Clear();
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            if (i == iMainEntry) { keepIdx.Add(i); continue; }
            if (rgName[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                IsManagedDll(i, rgRel, rgSz, rgCsz))
            {
                depIdx.Add(i);
                rgStripDeps.Add(rgName[i].Substring(0, rgName[i].Length - 4));
            }
            else
            {
                keepIdx.Add(i);
            }
        }

        BuildLamApp(rgRel, rgSz, rgCsz, rgName, depIdx, sSeed);

        //重算布局 重建bundle数据与头部
        ComputeBundleStart();
        BuildBundleDataAndHeader(uMajor, sBundleId, keepIdx, rgRel, rgSz, rgCsz, rgType, rgName,
                                 lDepsSz, lRtcSz, lRtcHash);
    }

    //解码器按0x80切成段 返回 [nSeg|iSeg|段表(off,len)xN] + 压缩流(混入解码段)
    private byte[] MergeDecoderIntoMain(byte[] rgComp0)
    {
        int iL = rgComp0.Length;
        const int iSeg = 0x80;
        int nSeg = (rgDecoder.Length + iSeg - 1) / iSeg;
        int iTable = 8 + nSeg * 8;
        byte[] rgOut = new byte[iTable + iL + rgDecoder.Length];
        BitConverter.GetBytes(nSeg).CopyTo(rgOut, 0);
        BitConverter.GetBytes(iSeg).CopyTo(rgOut, 4);
        int iKeep = Math.Min(0x20, iL); //避开流头
        int iSpan = Math.Max(1, (iL - iKeep) / nSeg);//等距间距
        int iSrc = 0, iOut = iTable;
        var rngJ = new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
        for (int j = 0; j < nSeg; j++)
        {
            int iCut = iKeep + j * iSpan;
            if (iSpan > iSeg + 8) iCut += rngJ.Next(-8, 9);//抖动
            iCut = Math.Min(iL, Math.Max(iSrc, iCut));//单调且不越界
            int nCp = iCut - iSrc;
            if (nCp > 0) Array.Copy(rgComp0, iSrc, rgOut, iOut, nCp);
            iOut += nCp; iSrc = iCut;
            int iSegLen = Math.Min(iSeg, rgDecoder.Length - j * iSeg);
            int iSegOff = iOut - iTable;//段在数据区偏移
            Array.Copy(rgDecoder, j * iSeg, rgOut, iOut, iSegLen);
            iOut += iSegLen;
            BitConverter.GetBytes(iSegOff).CopyTo(rgOut, 8 + j * 8);
            BitConverter.GetBytes(iSegLen).CopyTo(rgOut, 12 + j * 8);
        }
        int nTail = iL - iSrc;
        if (nTail > 0) Array.Copy(rgComp0, iSrc, rgOut, iOut, nTail);
        iOut += nTail;
        if (iOut < rgOut.Length)
        {
            byte[] rgT = new byte[iOut];
            Array.Copy(rgOut, 0, rgT, 0, iOut);
            rgOut = rgT;
        }
        return rgOut;
    }

    private void BuildLamApp(long[] rgRel, long[] rgSz, long[] rgCsz, string[] rgName, List<int> rgDepIdx, string sSeed)
    {
        var rgRaw = new List<byte[]>();
        var rgNames = new List<string>();
        int iDecIdx = -1, iJitIdx = -1, iSigIdx = -1, iPheropodIdx = -1;
        var rng = new Random(Environment.TickCount ^ (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        string RandName() => new string(Enumerable.Range(0, rng.Next(4, 16)).Select(_ => (char)('a' + rng.Next(26))).ToArray());

        {
            long lOndisk = rgCsz[iMainEntry] > 0 ? rgCsz[iMainEntry] : rgSz[iMainEntry];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[iMainEntry]), rgDll, 0, (int)lOndisk);
            rgRaw.Add(rgDll); rgNames.Add(sMainName);
        }
        foreach (int i in rgDepIdx)
        {
            long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
            byte[] rgDll = new byte[lOndisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgDll, 0, (int)lOndisk);
            rgRaw.Add(rgDll); rgNames.Add(rgName[i]);
        }

        if (rgJitHook != null)
        {
            uint uJitKey = Fnv1a(SeedKey(Encoding.ASCII.GetBytes(sSeed)));
            var rgAllCrcs = new List<(ulong Hi, ulong Lo)>();
            int iNEncMethods = 0;
            //主程序始终加密 依赖SetEncryptDeps显式指定才加密 不压缩名单优先
            for (int i = 0; i < rgRaw.Count - 1; i++)
            {
                if (i > 0 && (!_encryptDeps.Contains(rgNames[i]) || _noCompressDeps.Contains(rgNames[i]))) continue;
                var rgCrcs = MethodEncryptor.EncryptAll(rgRaw[i], uJitKey);
                rgAllCrcs.AddRange(rgCrcs);
                iNEncMethods += rgCrcs.Count;
            }
            rgAllCrcs.AddRange(rgBootCrcs);//B后半方法(W1/X4/X5)密文指纹 一并进签名表供jithook识别
            Console.WriteLine($"[jithook] encrypted {iNEncMethods} method bodies -> {rgAllCrcs.Count} sigs");
            var rgSigBytes = new byte[rgAllCrcs.Count * 16];
            for (int i = 0; i < rgAllCrcs.Count; i++)
            {
                BitConverter.GetBytes(rgAllCrcs[i].Lo).CopyTo(rgSigBytes, i * 16);//lo64 低32=crc2^mask32
                BitConverter.GetBytes(rgAllCrcs[i].Hi).CopyTo(rgSigBytes, i * 16 + 8);//hi64 uKey2^mask64
            }
            iJitIdx = rgRaw.Count;
            rgRaw.Add(rgJitHook); rgNames.Add(RandName());
            iSigIdx = rgRaw.Count;
            rgRaw.Add(rgSigBytes);  rgNames.Add(RandName());
        }

        //附加jithook钩子与诱饵
        if (rgPheropod != null)
        {
            iPheropodIdx = rgRaw.Count;
            rgRaw.Add(rgPheropod); rgNames.Add(RandName());
            Console.WriteLine($"[pheropod] gzip decoy: {rgPheropod.Length} bytes");
        }

        int iCount = rgRaw.Count;

        //生成随机诱饵条目混淆bundle
        const int iDecoys = 4;
        var rgDecName = new byte[iDecoys][];
        var rgDecData = new byte[iDecoys][];
        var rgDecoyOff = new uint[iDecoys];
        for (int i = 0; i < iDecoys; i++)
        {
            int iNl = rng.Next(4, 16);
            rgDecName[i] = new byte[iNl];
            for (int j = 0; j < iNl; j++)
                rgDecName[i][j] = (byte)('a' + rng.Next(26));
            if (i == 0)
                rgDecData[i] = BuildFakePe(rng);
            else if (i == 1)
            {
                byte[] rgPlain = BuildVmLure(rng);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true))
                    gz.Write(rgPlain, 0, rgPlain.Length);
                rgDecData[i] = ms.ToArray();
            }
            else
            {
                byte[] rgPlain = GenRandomX64(rng, 64, 512);
                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true))
                    gz.Write(rgPlain, 0, rgPlain.Length);
                rgDecData[i] = ms.ToArray();
            }
        }

        var rgNameBytes = new byte[iCount + iDecoys][];
        uint uNameTotal = 0;
        for (int i = 0; i < iCount; i++)
        {
            rgNameBytes[i] = Encoding.UTF8.GetBytes(rgNames[i]);
            uNameTotal += (uint)rgNameBytes[i].Length;
        }
        for (int i = 0; i < iDecoys; i++)
        {
            rgNameBytes[iCount + i] = rgDecName[i];
            uNameTotal += (uint)rgNameBytes[iCount + i].Length;
        }
        uint uNameAreaLen = (uNameTotal + 3) & ~3u;

        var rgBlocks = new byte[iCount][];
        var rgRawLen = new uint[iCount];
        var rgCompLen = new uint[iCount];
        var rgCompOff = new uint[iCount];
        byte[] rgSeedKey = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        uint uAdj = MixAdj(rgSeedKey);
        for (int i = 0; i < iCount; i++)
        {
            rgRawLen[i] = (uint)rgRaw[i].Length;
            if (i == iDecIdx || i == iJitIdx || i == iSigIdx || i == iPheropodIdx)
            {
                rgCompLen[i] = rgRawLen[i];
                rgBlocks[i] = XorBytes(rgRaw[i], rgSeedKey, uAdj);
            }
            else if (i > 0 && (!_bCompressDeps || _noCompressDeps.Contains(rgNames[i])))
            {
                rgCompLen[i] = rgRawLen[i];//明文存储(关闭压缩或排除名单)
                rgBlocks[i] = rgRaw[i];
            }
            else
            {
                uint cbCap = LamarrEncoder.GetMaxEncodedSize(rgRawLen[i]);
                rgBlocks[i] = new byte[cbCap];
                uint pcb = cbCap;
                if (LamarrEncoder.Encode(rgBlocks[i], ref pcb, rgRaw[i], rgRawLen[i]) != 0)
                    throw new InvalidOperationException($"Lamarr encode failed: {rgNames[i]}");
                rgCompLen[i] = pcb;
                if (i == 0 && rgDecoder.Length > 0)
                {
                    //主程序压缩流混入解码器段
                    byte[] rgBlk = new byte[pcb];
                    Array.Copy(rgBlocks[i], 0, rgBlk, 0, pcb);
                    rgBlocks[i] = MergeDecoderIntoMain(rgBlk);
                    rgCompLen[i] = (uint)rgBlocks[i].Length;
                }
            }
        }

        var rgPhys = new List<(int Kind, int Idx)>();
        var rgOrder = Enumerable.Range(0, iCount).OrderBy(_ => rng.Next()).ToArray();
        for (int i = 0; i < rgOrder.Length; i++)
        {
            rgPhys.Add((0, rgOrder[i]));
            if (i == 0) rgPhys.Add((1, 0));
            if (i % 2 == 1) rgPhys.Add((2, -1));
        }
        rgPhys.Add((2, -1));
        for (int i = 1; i < iDecoys; i++)
        {
            rgPhys.Add((2, -1));
            rgPhys.Add((1, i));
        }
        rgPhys.Add((2, -1));

        var rgGarbage = new List<int>();
        uint uDataOff = 0;
        foreach (var (kind, idx) in rgPhys)
        {
            if (kind == 0) { rgCompOff[idx] = uDataOff; uDataOff += rgCompLen[idx]; }
            else if (kind == 1) { rgDecoyOff[idx] = uDataOff; uDataOff += (uint)rgDecData[idx].Length; }
            else { int sz = rng.Next(32, 257); rgGarbage.Add(sz); uDataOff += (uint)sz; }
        }

        uint uTableLen = (uint)((iCount + iDecoys) * 20);
        uint uTotalLen = iTblOff + uTableLen + uNameAreaLen + uDataOff;
        rgLamApp = new byte[uTotalLen];

        uint cbOrigTotal = 0;
        for (int i = 0; i < iCount; i++) cbOrigTotal += rgRawLen[i];

        //由seed派生key与置换表
        uint uKey = LamKey(sSeed);
        int[] rgPerm = LamPerm(uKey);

        //假BSJB根 XOR写入lamapp头部
        int iNameOff = iTblOff + (iCount + iDecoys) * 20;
        int iDoff = iNameOff + (int)uNameAreaLen;
        int iDecOff = iDecIdx >= 0 ? iDoff + (int)rgCompOff[iDecIdx] : 0;
        byte[] rgBsjb = BuildFakeBsjb(iNameOff, (int)uNameAreaLen, iDecOff, iDecIdx >= 0 ? (int)rgCompLen[iDecIdx] : 0);
        for (int i = 0; i < iBsjbLen; i++)
            rgLamApp[i] = (byte)(rgBsjb[i] ^ rgKBsjb[i % rgKBsjb.Length]);

        //seed XOR K_seed 写入lamapp头部
        byte[] rgSeedB = Encoding.ASCII.GetBytes(sSeed);
        for (int i = 0; i < 32; i++)
            rgLamApp[iSeedOff + i] = (byte)(rgSeedB[i] ^ rgKSeed[i % rgKSeed.Length]);

        //payload FNV哈希 供selftest
        BitConverter.GetBytes(Fnv1a(rgRaw[0])).CopyTo(rgLamApp, iHashOff);

        LamWriteXor(rgLamApp, iHeadOff, (uint)iCount, uKey);
        LamWriteXor(rgLamApp, iHeadOff + 4, cbOrigTotal, uKey);
        LamWriteXor(rgLamApp, iHeadOff + 8, uDataOff, uKey);
        LamWriteXor(rgLamApp, iHeadOff + 12, (uint)iDecoys, LamSlot(uKey, iCount));

        for (int i = 0; i < iCount + iDecoys; i++)
        {
            uint uKk = LamSlot(uKey, i);
            uint[] rgF = new uint[4];
            if (i < iCount)
            {
                bool bPlain = i > 0 && i != iDecIdx && i != iJitIdx && i != iSigIdx && i != iPheropodIdx && (!_bCompressDeps || _noCompressDeps.Contains(rgNames[i]));
                rgF[0] = (uint)rgNameBytes[i].Length;
                rgF[1] = bPlain ? 0x7FFFFFFFu : rgRawLen[i];
                rgF[2] = bPlain ? rgRawLen[i] : rgCompLen[i];
                rgF[3] = rgCompOff[i];
            }
            else
            {
                int iDd = i - iCount;
                rgF[0] = (uint)rgNameBytes[i].Length;
                rgF[1] = (uint)rgDecData[iDd].Length;
                rgF[2] = (uint)rgDecData[iDd].Length;
                rgF[3] = rgDecoyOff[iDd];
            }
            int iOff = iTblOff + i * 20;
            for (int iS = 0; iS < 4; iS++)
                BitConverter.GetBytes(rgF[rgPerm[iS]] ^ uKk).CopyTo(rgLamApp, iOff + iS * 4);
        }

        int iNo = iTblOff + (iCount + iDecoys) * 20;
        for (int i = 0; i < iCount + iDecoys; i++)
        {
            Array.Copy(rgNameBytes[i], 0, rgLamApp, iNo, rgNameBytes[i].Length);
            iNo += rgNameBytes[i].Length;
        }

        int iDataBase = iTblOff + (iCount + iDecoys) * 20 + (int)uNameAreaLen;
        uint uPos = 0; int iG = 0;
        foreach (var (kind, idx) in rgPhys)
        {
            if (kind == 0)
            {
                Array.Copy(rgBlocks[idx], 0, rgLamApp, (int)(iDataBase + uPos), rgCompLen[idx]);
                uPos += rgCompLen[idx];
            }
            else if (kind == 1)
            {
                Array.Copy(rgDecData[idx], 0, rgLamApp, (int)(iDataBase + uPos), rgDecData[idx].Length);
                uPos += (uint)rgDecData[idx].Length;
            }
            else
            {
                byte[] rgGb = new byte[rgGarbage[iG++]];
                rng.NextBytes(rgGb);
                Array.Copy(rgGb, 0, rgLamApp, (int)(iDataBase + uPos), rgGb.Length);
                uPos += (uint)rgGb.Length;
            }
        }

        Console.WriteLine($"  .rdata: {iCount} entry(s) + {iDecoys} decoy, {cbOrigTotal} -> {rgLamApp.Length} bytes (Lamarr)");
    }

    private static string MakeSeed() => Guid.NewGuid().ToString("N");

    //假BSJB metadata root #~/#Strings指向诱饵
    private static byte[] BuildFakeBsjb(int iNameOff, int iNameLen, int iDecOff, int iDecLen)
    {
        byte[] rgB = new byte[64];
        rgB[0] = 0x42; rgB[1] = 0x53; rgB[2] = 0x4A; rgB[3] = 0x42;//BSJB
        rgB[4] = 0x01; rgB[5] = 0x00;                        //uMajor 1
        rgB[6] = 0x01; rgB[7] = 0x00;                        //minor 1
        rgB[12] = 0x0C;                                      //version iLen 12
        byte[] rgVer = Encoding.ASCII.GetBytes("v4.0.30319");
        Array.Copy(rgVer, 0, rgB, 16, rgVer.Length);
        rgB[30] = 0x02;                                      //2 streams
        //stream0 #~指向解码器数据区
        BitConverter.GetBytes((uint)iDecOff).CopyTo(rgB, 32);
        BitConverter.GetBytes((uint)iDecLen).CopyTo(rgB, 36);
        rgB[40] = 0x23; rgB[41] = 0x7E;                      //"#~"
        //stream1 #Strings指向名称区
        BitConverter.GetBytes((uint)iNameOff).CopyTo(rgB, 44);
        BitConverter.GetBytes((uint)iNameLen).CopyTo(rgB, 48);
        byte[] rgNs = Encoding.ASCII.GetBytes("#Strings");
        Array.Copy(rgNs, 0, rgB, 52, rgNs.Length);
        return rgB;
    }

    private static byte[] GenRandomX64(Random rng, int cbMin, int cbMax)
    {
        int cbTarget = rng.Next(cbMin, cbMax);
        using var ms = new MemoryStream();
        if (rng.Next(4) == 0)
        {
            ms.Write(new byte[] { 0x55, 0x48, 0x8B, 0xEC }, 0, 4);//push rbp; mov rbp,rsp
            cbTarget += 4;
        }
        while (ms.Length < cbTarget)
        {
            byte[] rgInsn = X64Insn(rng);
            ms.Write(rgInsn, 0, rgInsn.Length);
        }
        if (rng.Next(4) == 0)//epilogue
            ms.Write(new byte[] { 0x5D, 0xC3 }, 0, 2);//pop rbp; ret
        return ms.ToArray();
    }

    private static byte[] X64Insn(Random rng)
    {
        switch (rng.Next(28))
        {
            case 0: return new byte[] { 0x48, 0x8B, (byte)(0xC0 + rng.Next(8)) };//mov rAX,rX
            case 1: return new byte[] { 0x48, 0x89, (byte)(0xC0 + rng.Next(8)) };//mov rX,rAX
            case 2: { var rgI = new byte[10]; rgI[0] = 0x48; rgI[1] = 0xB8; BitConverter.GetBytes(((ulong)(uint)rng.Next() << 32) | (uint)rng.Next()).CopyTo(rgI, 2); return rgI; }//mov rAX,imm64
            case 3: return new byte[] { 0x48, 0x83, (byte)(0xC0 + rng.Next(8)), (byte)rng.Next(256) };//add rX,imm8
            case 4: return new byte[] { 0x48, 0x29, (byte)(0xC0 + rng.Next(8)) };//sub rAX,rX
            case 5: return new byte[] { 0x48, 0x33, (byte)(0xC0 + rng.Next(8)) };//xor rAX,rX
            case 6: return new byte[] { 0x31, (byte)(0xC0 + rng.Next(8)) };//xor rX,rAX 32位
            case 7: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0x8D; rgI[2] = (byte)(0x80 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//lea rX,[rAX+disp32]
            case 8: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0xC7; rgI[2] = (byte)(0xC0 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//mov rX,imm32
            case 9: return new byte[] { (byte)(0x50 + rng.Next(8)) };//push rX
            case 10: return new byte[] { (byte)(0x58 + rng.Next(8)) };//pop rX
            case 11: return new byte[] { 0x48, 0x85, (byte)(0xC0 + rng.Next(8)) };//test rAX,rX
            case 12: return new byte[] { 0x48, 0x39, (byte)(0xC0 + rng.Next(8)) };//cmp rAX,rX
            case 13: return new byte[] { 0x48, 0x63, (byte)(0xC0 + rng.Next(8)) };//movsxd rAX,rX
            case 14: return new byte[] { 0x48, 0x0F, 0xAF, (byte)(0xC0 + rng.Next(8)) };//imul rAX,rX
            case 15: return new byte[] { 0x48, 0x01, (byte)(0xC0 + rng.Next(8)) };//add rAX,rX
            case 16: return new byte[] { 0x48, 0x21, (byte)(0xC0 + rng.Next(8)) };//and rAX,rX
            case 17: return new byte[] { 0x48, 0x09, (byte)(0xC0 + rng.Next(8)) };//or rAX,rX
            case 18: return new byte[] { 0x48, 0xD1, (byte)(0xE0 + rng.Next(8)) };//shl rX,1
            case 19: return new byte[] { 0x48, 0xD1, (byte)(0xE8 + rng.Next(8)) };//shr rX,1
            case 20: return new byte[] { 0x48, 0xC1, (byte)(0xE0 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//shl rX,imm8
            case 21: return new byte[] { 0x48, 0x0F, 0x44, (byte)(0xC0 + rng.Next(8)) };//cmove rAX,rX
            case 22: return new byte[] { 0x48, 0xF7, (byte)(0xD0 + rng.Next(8)) };//not rX
            case 23: return new byte[] { 0x48, 0xF7, (byte)(0xD8 + rng.Next(8)) };//neg rX
            case 24: return new byte[] { 0x0F, 0x1F, 0x40, 0x00 };//多字节nop
            case 25: return new byte[] { 0xF3, 0x90 };//pause
            case 26: return new byte[] { 0xEB, (byte)(1 + rng.Next(127)) };//jmp rel8正向前跳
            default: return new byte[] { 0xC3 };//ret
        }
    }

    private static byte[] DcryptInsn(Random rng)
    {
        switch (rng.Next(8))
        {
            case 0: return new byte[] { 0x05, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//add eax,imm32
            case 1: return new byte[] { 0x2D, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//sub eax,imm32
            case 2: return new byte[] { 0x35, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//xor eax,imm32
            case 3: return new byte[] { 0xC1, 0xC0, (byte)(1 + rng.Next(31)) };//rol eax,imm8
            case 4: return new byte[] { 0xC1, 0xC8, (byte)(1 + rng.Next(31)) };//ror eax,imm8
            case 5: return new byte[] { 0x0F, 0xC8 };//bswap eax
            case 6: return new byte[] { 0xF7, 0xD0 };//not eax
            default: return new byte[] { 0xF7, 0xD8 };//neg eax
        }
    }

    private static byte[] PpcWord(uint u)
    {
        return new byte[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u };
    }

    private static int PpcReg(Random rng)
    {
        int i = rng.Next(24);
        if (i < 8) return new[] { 0, 3, 4, 5, 6, 11, 12, 31 }[rng.Next(8)];
        if (i < 16) return 7 + rng.Next(5);
        if (i < 20) return 13 + rng.Next(6);
        return 20 + rng.Next(12);
    }

    private static byte[] IlToPpc(Random rng, byte bOp)
    {
        int rD = PpcReg(rng), rA = PpcReg(rng), rB = PpcReg(rng);
        uint u;
        switch (bOp)
        {
            case 0x00: return PpcWord(24u << 26);                               //nop = ori r0,r0,0
            case 0x02: case 0x03: case 0x04: case 0x05:                         //ldarg.N -> lwz rD, 8+4N(r1)
                return PpcWord((32u << 26) | ((uint)rD << 21) | (1u << 16) | (uint)(8 + 4 * (bOp - 0x02)));
            case 0x06: case 0x07: case 0x08: case 0x09:                         //ldloc.N -> lwz rD, -(8+4N)(r1)
                return PpcWord((32u << 26) | ((uint)rD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x06)) & 0xFFFF));
            case 0x0A: case 0x0B: case 0x0C: case 0x0D:                         //stloc.N -> stw rD, -(8+4N)(r1)
                return PpcWord((36u << 26) | ((uint)rD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x0A)) & 0xFFFF));
            case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A: case 0x1B: case 0x1C: case 0x1D: case 0x1E://ldc.i4.N -> li rD,N
                return PpcWord((14u << 26) | ((uint)rD << 21) | (uint)(bOp - 0x16));
            case 0x28: return PpcWord((18u << 26) | 1u);                        //call -> bl +0
            case 0x58: u = 266u; break;                                         //add
            case 0x59: u = 40u; break;                                          //sub -> subf
            case 0x5A: u = 235u; break;                                         //mul -> mullw
            case 0x5B: case 0x5D: u = 491u; break;                              //div/rem -> divw
            case 0x5F: u = 28u; break;                                          //and
            case 0x60: u = 444u; break;                                         //or
            case 0x61: u = 316u; break;                                         //xor
            case 0x62: u = 24u; break;                                          //shl -> slw
            case 0x63: u = 536u; break;                                         //shr -> srw
            case 0x65: return PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | (104u << 1));//neg rD,rA
            case 0x66: return PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rB << 16) | ((uint)rB << 11) | (124u << 1));//not -> nor rD,rB,rB
            case 0x2A: return PpcWord((19u << 26) | (20u << 21) | (16u << 1));  //ret -> blr
            case 0x2C: return PpcConcat(PpcWord((11u << 26) | ((uint)rA << 16)), PpcWord((16u << 26) | (12u << 21) | (2u << 16)));//brfalse -> cmpwi rA,0; beq +0
            case 0x2D: return PpcConcat(PpcWord((11u << 26) | ((uint)rA << 16)), PpcWord((16u << 26) | (4u << 21) | (2u << 16)));//brtrue -> cmpwi rA,0; bne +0
            default: return PpcWord(24u << 26);
        }
        return PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | ((uint)rB << 11) | (u << 1));//算术通用
    }

    private static byte[] PpcConcat(byte[] a, byte[] b)
    {
        byte[] r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static byte[] BuildVmLure(Random rng)
    {
        List<byte> rgIl = new();
        List<byte> rgPpc = new();
        List<(byte, int)> rgMap = new();
        int iFrame = 32 + 32 * rng.Next(16);
        int iSave = 8 + 8 * rng.Next(4);
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (339u << 1)));                          //mflr r0
        rgPpc.AddRange(PpcWord((36u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));               //stw r0,iSave(r1)
        rgPpc.AddRange(PpcWord((37u << 26) | (1u << 21) | (1u << 16) | (uint)(-iFrame & 0xFFFF)));//stwu r1,-N(r1)
        int iOff = 12;
        byte[] rgOps = { 0x00,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x16,0x17,0x18,0x19,0x1A,0x1B,0x1E,0x28,0x58,0x59,0x5A,0x5B,0x5D,0x5F,0x60,0x61,0x62,0x63,0x65,0x66,0x2C,0x2D };
        int n = 10 + rng.Next(18);
        for (int i = 0; i < n; i++)
        {
            byte bOp = rgOps[rng.Next(rgOps.Length)];
            byte[] rgP = IlToPpc(rng, bOp);
            rgIl.Add(bOp);
            rgMap.Add((bOp, iOff));
            rgPpc.AddRange(rgP);
            iOff += rgP.Length;
        }
        rgIl.Add(0x2A); rgMap.Add((0x2A, iOff)); rgPpc.AddRange(IlToPpc(rng, 0x2A)); iOff += 4;
        rgPpc.AddRange(PpcWord((14u << 26) | (1u << 21) | (1u << 16) | (uint)iFrame));//addi r1,r1,N
        rgPpc.AddRange(PpcWord((32u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));   //lwz r0,iSave(r1)
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (467u << 1)));              //mtlr r0
        rgPpc.AddRange(PpcWord((19u << 26) | (20u << 21) | (16u << 1)));              //blr
        var rgHandlers = new List<byte[]>();
        int nH = 6 + rng.Next(6);
        for (int i = 0; i < nH; i++)
        {
            using var h = new MemoryStream();
            int c = 2 + rng.Next(5);
            for (int j = 0; j < c; j++) { byte[] gi = DcryptInsn(rng); h.Write(gi, 0, gi.Length); }
            h.Write(new byte[] { 0xC3 }, 0, 1);
            rgHandlers.Add(h.ToArray());
        }
        const ulong uBase = 0x180000000UL;
        int iTable = 47;
        int iHandlers = iTable + rgHandlers.Count * 8;
        int iIlIn = iHandlers + rgHandlers.Sum(x => x.Length) + 6;
        int iMap = iIlIn + rgIl.Count;
        int iPpc = iMap + rgMap.Count * 5;
        int iIlOut = iPpc + rgPpc.Count;
        byte[] rgDisp = new byte[]
        {
            0x48,0xBE,0,0,0,0,0,0,0,0,  //mov rsi, imm64 -> PPC区
            0x9C,                       //pushfq
            0x41,0x57,                  //push r15
            0x41,0x56,                  //push r14
            0x8B,0x06,                  //mov eax,[rsi]
            0x0F,0xC8,                  //bswap eax BE->LE
            0x05,0,0,0,0,               //add eax,KEY1
            0x35,0,0,0,0,               //xor eax,KEY2
            0xC1,0xC0,0x05,             //rol eax,5
            0x25,0xFF,0x00,0x00,0x00,   //and eax,0xFF
            0x48,0x8B,0x04,0xC5,0,0,0,0,//mov rax,[rax*8+handler]
            0xFF,0xE0                   //jmp rax
        };
        Buffer.BlockCopy(BitConverter.GetBytes(uBase + (ulong)iPpc), 0, rgDisp, 2, 8);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rng.Next()), 0, rgDisp, 20, 4);//KEY1
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rng.Next()), 0, rgDisp, 25, 4);//KEY2
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(uBase + (ulong)iTable)), 0, rgDisp, 41, 4);
        using var ms = new MemoryStream();
        ms.Write(rgDisp, 0, rgDisp.Length);
        for (int i = 0; i < rgHandlers.Count; i++)
        {
            int iH = iHandlers + rgHandlers.Take(i).Sum(x => x.Length);
            ms.Write(BitConverter.GetBytes((ulong)(uint)(uBase + (ulong)iH)), 0, 8);
        }
        foreach (var h in rgHandlers) ms.Write(h, 0, h.Length);
        ms.Write(new byte[] { 0x41,0x5E, 0x41,0x5F, 0x9D, 0xC3 }, 0, 6);//pop r14; pop r15; popfq; ret
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        foreach (var (il, o) in rgMap)
        {
            ms.WriteByte(il);
            ms.Write(new byte[] { (byte)(o >> 24), (byte)(o >> 16), (byte)(o >> 8), (byte)o }, 0, 4);
        }
        ms.Write(rgPpc.ToArray(), 0, rgPpc.Count);
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        return ms.ToArray();
    }

    private static byte[] BuildFakePe(Random rng)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 }, 0, 16);
        ms.Write(new byte[0x20], 0, 0x20);
        byte[] rgDos = Encoding.ASCII.GetBytes("This program cannot be run in DOS mode.\r\n\r\n$");
        ms.Write(rgDos, 0, rgDos.Length);
        ms.Position = 0x3C;
        ms.Write(new byte[] { 0x80, 0x00, 0x00, 0x00 }, 0, 4);//e_lfanew -> PE
        ms.Position = 0x80;
        ms.Write(Encoding.ASCII.GetBytes("PE\0\0"), 0, 4);
        var writer = new BinaryWriter(ms, Encoding.ASCII, true);
        writer.Write((ushort)0x8664);//Machine x64
        writer.Write((ushort)2);     //NumberOfSections
        writer.Write(rng.Next());    //TimeDateStamp
        writer.Write(0u); writer.Write(0u);//PtrToSymbolTable / NumSymbols
        writer.Write((ushort)0xF0);  //SizeOfOptionalHeader
        writer.Write((ushort)0x2022);//Characteristics
        writer.Write((ushort)0x20B); //Magic PE32+
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0u);
        writer.Write(0x180000000UL); //ImageBase
        writer.Write(0x1000u); writer.Write(0x200u);
        writer.Write((ushort)6); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write(0x4000u);       //SizeOfImage
        writer.Write(0x400u);        //SizeOfHeaders
        writer.Write(0u);            //Checksum
        writer.Write((ushort)3);     //Subsystem console
        writer.Write((ushort)0);     //DllCharacteristics
        writer.Write(0x100000UL); writer.Write(0x1000UL); writer.Write(0x100000UL); writer.Write(0x1000UL);
        writer.Write(0u); writer.Write(0u);
        for (int i = 0; i < 16; i++) { writer.Write(0u); writer.Write(0u); }//数据目录
        writer.Write(Encoding.ASCII.GetBytes(".text\0\0\0")); writer.Write(0x1000u); writer.Write(0x1000u); writer.Write(0x400u); writer.Write(0x200u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0x60000020u);
        writer.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); writer.Write(0x2000u); writer.Write(0x200u); writer.Write(0x600u); writer.Write(0x600u); writer.Write(0u); writer.Write(0u); writer.Write(0u); writer.Write(0x40000040u);
        writer.Write(GenRandomX64(rng, 64, 256));//假代码段
        byte[] rgRes = ms.ToArray();
        //2字节交错 0x80起MZ+DOS头明文保留
        for (int i = 0x80; i + 1 < rgRes.Length; i += 2)
        {
            byte byTmp = rgRes[i]; rgRes[i] = rgRes[i + 1]; rgRes[i + 1] = byTmp;
        }
        return rgRes;
    }

    private static uint Fnv1a(byte[] rgD)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte bX in rgD) { uH ^= bX; uH *= uLK1A ^ uLK1B; }
        return uH;
    }

    private static uint MixAdj(byte[] rgKey)
    {
        uint uA = rgKey[0] | ((uint)rgKey[1] << 8) | ((uint)rgKey[2] << 16) | ((uint)rgKey[3] << 24);
        uint uB = rgKey[4] | ((uint)rgKey[5] << 8) | ((uint)rgKey[6] << 16) | ((uint)rgKey[7] << 24);
        uint uM = uA ^ uB ^ 0x811C9DC5u;
        return uM != 0 ? uM : 0x811C9DC5u;
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

    private static byte[] XorBytes(byte[] rgD, byte[] rgKey, uint uAdj)
    {
        byte[] rgR = new byte[rgD.Length];
        for (int i = 0; i < rgD.Length; i++) rgR[i] = (byte)(rgD[i] ^ rgKey[i % rgKey.Length] ^ (byte)(uAdj >> (8 * (i % 4))));
        return rgR;
    }

    private static uint LamKey(string sVal)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (char ch in sVal)
        {
            uH ^= ch;
            uH *= uLK1A ^ uLK1B;
        }
        return uH;
    }

    private static int[] LamPerm(uint uK)
    {
        int[] rgA = { 0, 1, 2, 3 };
        uint uS = uK;
        for (int i = 3; i > 0; i--)
        {
            uS = uS * (uLLCA ^ uLLCB) + (uLQCA ^ uLQCB);
            int iJ = (int)(uS % (uint)(i + 1));
            (rgA[i], rgA[iJ]) = (rgA[iJ], rgA[i]);
        }
        return rgA;
    }

    private static uint LamSlot(uint uK, int i) => uK + (uLGAA ^ uLGAB) * (uint)i;

    private static void LamWriteXor(byte[] rgB, int iOff, uint uV, uint uX)
        => BitConverter.GetBytes(uV ^ uX).CopyTo(rgB, iOff);

    //重建bundle boot替换主程序条目 数据写在lamapp之后
    private void BuildBundleDataAndHeader(uint uMajor, string sBundleId, List<int> keepIdx,
        long[] rgRel, long[] rgSz, long[] rgCsz, byte[] rgType, string[] rgName,
        long lDepsSz, long lRtcSz, long lRtcHash)
    {
        int iM = keepIdx.Count;
        rgBundleOffsets = new long[iM];
        rgBundleCsz = new long[iM];
        rgBundleSz = new long[iM];

        using var ms = new MemoryStream();
        long lDepsSzNew = lDepsSz;
        long lRtcSzNew = lRtcSz;
        for (int k = 0; k < iM; k++)
        {
            int i = keepIdx[k];
            byte[] rgData;
            if (i == iMainEntry)
                rgData = rgBoot;
            else
            {
                long lOndisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
                rgData = new byte[lOndisk];
                Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)lOndisk);
                if (rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                {
                    rgData = StripDepsDependencies(rgData);
                    lDepsSzNew = rgData.Length;
                }
                else if (rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                {
                    rgData = RewriteRuntimeConfig(rgData);
                    lRtcSzNew = rgData.Length;
                }
            }
            rgBundleOffsets[k] = ms.Position;
            ms.Write(rgData, 0, rgData.Length);
            rgBundleCsz[k] = i == iMainEntry ? 0 : (rgCsz[i] > 0 ? rgData.Length : 0);
            rgBundleSz[k] = i == iMainEntry
                || rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                ? rgData.Length : rgSz[i];
        }
        rgBundleData = ms.ToArray();
        lBundleDataLen = rgBundleData.Length;
        iEntryCount = iM;
        lDepsSz = lDepsSzNew;

        iNewRtcIdx = -1;
        for (int k = 0; k < iM; k++)
        {
            if (rgName[keepIdx[k]].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            { iNewRtcIdx = k; break; }
        }

        uint uOutMajor = uMajor >= 2 ? 2u : uMajor;
        using var hd = new MemoryStream();
        WriteU32(hd, uOutMajor);
        WriteU32(hd, 0);
        WriteI32(hd, iM);
        WriteStr(hd, sBundleId);

        if (uMajor >= 2)
        {
            int iKDeps = -1;
            for (int k = 0; k < iM; k++)
            {
                if (rgName[keepIdx[k]].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                { iKDeps = k; break; }
            }
            WriteI64(hd, iKDeps >= 0 ? lBundleStart + rgBundleOffsets[iKDeps] : 0);
            WriteI64(hd, lDepsSz);
            WriteI64(hd, iNewRtcIdx >= 0 ? lBundleStart + rgBundleOffsets[iNewRtcIdx] : 0);
            WriteI64(hd, lRtcSzNew);
            WriteI64(hd, lRtcHash);
        }

        for (int k = 0; k < iM; k++)
        {
            WriteI64(hd, lBundleStart + rgBundleOffsets[k]);
            WriteI64(hd, rgBundleSz[k]);
            if (uOutMajor >= 6)
                WriteI64(hd, rgBundleCsz[k]);
            WriteU8(hd, rgType[keepIdx[k]]);
            WriteStr(hd, rgName[keepIdx[k]]);
        }
        rgNewHeader = hd.ToArray();
    }

    private byte[] RewriteRuntimeConfig(byte[] rgOrig)
    {
        using var doc = JsonDocument.Parse(rgOrig);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.NameEquals("runtimeOptions") && p.Value.ValueKind == JsonValueKind.Object)
                {
                    w.WritePropertyName("runtimeOptions");
                    w.WriteStartObject();
                    bool bRoll = false;
                    foreach (var op in p.Value.EnumerateObject())
                    {
                        if (op.NameEquals("frameworks") && op.Value.ValueKind == JsonValueKind.Array)
                        {
                            w.WritePropertyName("frameworks");
                            w.WriteStartArray();
                            foreach (var fw in op.Value.EnumerateArray())
                            {
                                w.WriteStartObject();
                                foreach (var f in fw.EnumerateObject())
                                {
                                    if (f.NameEquals("version")) w.WriteString("version", sRtcVersion);
                                    else f.WriteTo(w);
                                }
                                w.WriteEndObject();
                            }
                            w.WriteEndArray();
                        }
                        else if (op.NameEquals("framework") && op.Value.ValueKind == JsonValueKind.Object)
                        {
                            w.WritePropertyName("framework");
                            w.WriteStartObject();
                            foreach (var f in op.Value.EnumerateObject())
                            {
                                if (f.NameEquals("version")) w.WriteString("version", sRtcVersion);
                                else f.WriteTo(w);
                            }
                            w.WriteEndObject();
                        }
                        else if (op.NameEquals("configProperties") && op.Value.ValueKind == JsonValueKind.Object)
                        {
                            w.WritePropertyName("configProperties");
                            w.WriteStartObject();
                            foreach (var cp in op.Value.EnumerateObject())
                                cp.WriteTo(w);
                            //net10的Tiered后台编译与jithook的compileMethod hook冲突(退出期GC崩溃)
                            if (sTiered != "default") w.WriteBoolean("System.Runtime.TieredCompilation", false);
                            w.WriteEndObject();
                        }
                        else if (op.NameEquals("rollForward"))
                        {
                            w.WriteString("rollForward", "LatestMajor");
                            bRoll = true;
                        }
                        else op.WriteTo(w);
                    }
                    if (!bRoll) w.WriteString("rollForward", "LatestMajor");
                    w.WriteEndObject();
                }
                else p.WriteTo(w);
            }
            w.WriteEndObject();
            w.Flush();
        }
        return ms.ToArray();
    }

    //剔除依赖项 防hostpolicy加载已剥离的dll
    private byte[] StripDepsDependencies(byte[] rgDeps)
    {
        using var doc = JsonDocument.Parse(rgDeps);
        var root = doc.RootElement;
        var rgStripAll = new HashSet<string>(rgStripDeps, StringComparer.OrdinalIgnoreCase);

        //剔除无runtime的依赖包
        if (root.TryGetProperty("targets", out var rgTargets))
            foreach (var tfm in rgTargets.EnumerateObject())
                foreach (var pkg in tfm.Value.EnumerateObject())
                {
                    bool bHasRuntime = false;
                    foreach (var iPos in pkg.Value.EnumerateObject())
                        if (iPos.Name == "runtime" || iPos.Name == "runtimeTargets") { bHasRuntime = true; break; }
                    if (!bHasRuntime)
                        rgStripAll.Add(pkg.Name.Split('/')[0]);
                }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "targets")
                {
                    w.WritePropertyName("targets");
                    w.WriteStartObject();
                    foreach (var tfm in prop.Value.EnumerateObject())
                    {
                        w.WritePropertyName(tfm.Name);
                        w.WriteStartObject();
                        foreach (var pkg in tfm.Value.EnumerateObject())
                        {
                            if (rgStripAll.Contains(pkg.Name.Split('/')[0]))
                                continue;
                            w.WritePropertyName(pkg.Name);
                            w.WriteStartObject();
                            foreach (var iPos in pkg.Value.EnumerateObject())
                            {
                                if (iPos.Name == "dependencies")
                                {
                                    w.WritePropertyName("dependencies");
                                    w.WriteStartObject();
                                    foreach (var d in iPos.Value.EnumerateObject())
                                        if (!rgStripAll.Contains(d.Name))
                                        {
                                            w.WritePropertyName(d.Name);
                                            d.Value.WriteTo(w);
                                        }
                                    w.WriteEndObject();
                                }
                                else
                                {
                                    w.WritePropertyName(iPos.Name);
                                    iPos.Value.WriteTo(w);
                                }
                            }
                            w.WriteEndObject();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                else if (prop.Name == "libraries")
                {
                    w.WritePropertyName("libraries");
                    w.WriteStartObject();
                    foreach (var lib in prop.Value.EnumerateObject())
                    {
                        if (rgStripAll.Contains(lib.Name.Split('/')[0]))
                            continue;
                        w.WritePropertyName(lib.Name);
                        lib.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                else
                {
                    w.WritePropertyName(prop.Name);
                    prop.Value.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    //判断dll是否托管PE 含CLR头
    private bool IsManagedDll(int i, long[] rgRel, long[] rgSz, long[] rgCsz)
    {
        if (rgCsz[i] > 0) return false;
        long lAbs = iBundleDataStart + rgRel[i];
        if (lAbs < 0 || lAbs + rgSz[i] > rgPayload.Length || rgSz[i] < 0x40)
            return false;
        int iPe = BitConverter.ToInt32(rgPayload, (int)lAbs + 0x3C);
        if (iPe + 0x18 > rgSz[i] || BitConverter.ToUInt32(rgPayload, (int)lAbs + iPe) != 0x4550)
            return false;
        ushort usMagic = BitConverter.ToUInt16(rgPayload, (int)lAbs + iPe + 24);
        int iDdOff = usMagic == 0x20B ? 112 : 96;//PE32+/PE32数据目录偏移不同
        long lClr = lAbs + iPe + 24 + iDdOff + 14 * 8;
        if (lClr + 8 > lAbs + rgSz[i])
            return false;
        uint uRva = BitConverter.ToUInt32(rgPayload, (int)lClr);
        uint uSz = BitConverter.ToUInt32(rgPayload, (int)lClr + 4);
        return uRva != 0 && uSz != 0;
    }

    private void PatchStubVars(string sOutPath)
    {
        int iPrefMajor = GetPayloadMajor();

        ReplaceMarker(rgStubCode, "##APPNAME##", Encoding.Unicode.GetBytes(sMainName), 256);
        ReplaceMarker(rgStubCode, "##PREFMAJ##", BitConverter.GetBytes((uint)iPrefMajor), 8);

        int iOff = IndexOf(rgStubCode, new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 });
        if (iOff < 0)
            throw new InvalidOperationException("gHeaderOff marker not found in stub");
        Array.Copy(BitConverter.GetBytes(lNewBundleHeaderOffset), 0, rgStubCode, iOff, 8);
        Console.WriteLine($"  app_name: {sMainName}");
        Console.WriteLine($"  pref_major: {iPrefMajor}");
        Console.WriteLine($"  header_offset: 0x{lNewBundleHeaderOffset:X}");

        //校验stub模板标记是否全部替换
        if (IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##APPNAME##")) >= 0 ||
            IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##PREFMAJ##")) >= 0)
            throw new InvalidOperationException("stub template markers were not fully replaced");
    }

    private static int ParseMajorFromRtc(string sRtc)
    {
        var iM = System.Text.RegularExpressions.Regex.Match(sRtc, "\"tfm\"\\s*:\\s*\"net(\\d+)");
        if (iM.Success && int.TryParse(iM.Groups[1].Value, out int iMaj) && iMaj > 0)
            return iMaj;
        var m2 = System.Text.RegularExpressions.Regex.Match(sRtc, "\"version\"\\s*:\\s*\"(\\d+)\\.(\\d+)");
        return m2.Success && int.TryParse(m2.Groups[1].Value, out int iV2) && iV2 > 0 ? iV2 : 0;
    }

    private int GetPayloadMajor()
    {
        if (iNewRtcIdx < 0) return 0;
        int iOff = (int)rgBundleOffsets[iNewRtcIdx];
        int iLen = (int)rgBundleSz[iNewRtcIdx];
        if (iOff < 0 || iLen <= 0 || iOff + iLen > rgBundleData.Length) return 0;
        return ParseMajorFromRtc(Encoding.UTF8.GetString(rgBundleData, iOff, iLen));
    }

    private static void ReplaceMarker(byte[] b, string sMarker, byte[] rgValue, int iSpace)
    {
        byte[] rgPat = Encoding.ASCII.GetBytes(sMarker);
        int i = IndexOf(b, rgPat);
        if (i < 0)
            throw new InvalidOperationException($"Stub marker '{sMarker}' not found");
        if (rgValue.Length > iSpace)
            throw new InvalidOperationException($"Stub value for '{sMarker}' too long ({rgValue.Length} > {iSpace})");
        Array.Clear(b, i, iSpace);
        Array.Copy(rgValue, 0, b, i, rgValue.Length);
    }

    private static int IndexOf(byte[] b, byte[] rgPat)
    {
        for (int i = 0; i + rgPat.Length <= b.Length; i++)
        {
            bool bOk = true;
            for (int j = 0; j < rgPat.Length; j++)
                if (b[i + j] != rgPat[j]) { bOk = false; break; }
            if (bOk) return i;
        }
        return -1;
    }

    #endregion
    #region 输出

    private void WriteFile(string sOutPath)
    {
        uint uNewHdrs = AlignUp(uSizeOfHdrs, uFileAlign);

        uint uStubRva = AlignUp(0x1000, uSectAlign);
        uint uLamAppRva = AlignUp(uStubRva + (uint)rgStubCode.Length, uSectAlign);
        uint uNewImg = AlignUp(uLamAppRva + (uint)rgLamApp.Length, uSectAlign);

        byte[] rgHdrs = new byte[uNewHdrs];
        Array.Copy(rgPayload, 0, rgHdrs, 0, Math.Min(uSizeOfHdrs, rgHdrs.Length));

        BitConverter.GetBytes((ushort)2).CopyTo(rgHdrs, iPeOff + 6);
        BitConverter.GetBytes(uNewImg).CopyTo(rgHdrs, iOptOff + 56);
        BitConverter.GetBytes(uStubRva + uStubEntryOff).CopyTo(rgHdrs, iOptOff + 16);
        BitConverter.GetBytes(uFileAlign).CopyTo(rgHdrs, iOptOff + 36);
        Array.Clear(rgHdrs, iOptOff + 0x70, Math.Min(16 * 8, rgHdrs.Length - (iOptOff + 0x70)));
        BitConverter.GetBytes(uStubRawSize).CopyTo(rgHdrs, iOptOff + 4);
        BitConverter.GetBytes(uLamAppRawSize).CopyTo(rgHdrs, iOptOff + 8);

        if (iSecOff + 80 > rgHdrs.Length)
            throw new InvalidOperationException("Header too small for new section table");
        Array.Clear(rgHdrs, iSecOff, Math.Min(usSecCount * 40, rgHdrs.Length - iSecOff));
        WriteSection(rgHdrs, iSecOff, ".text", uStubRva, (uint)rgStubCode.Length, uStubRawSize, uStubRaw);
        WriteSection(rgHdrs, iSecOff + 40, ".rdata", uLamAppRva, (uint)rgLamApp.Length, uLamAppRawSize, uLamAppRaw);

        using var fs = new FileStream(sOutPath, FileMode.Create);
        fs.Write(rgHdrs, 0, rgHdrs.Length);
        Pad(fs, (int)(uStubRaw - uNewHdrs));
        fs.Write(rgStubCode, 0, rgStubCode.Length);
        Pad(fs, (int)(uStubRawSize - rgStubCode.Length));
        byte[] rgMarker = new byte[40];
        BitConverter.GetBytes(lNewBundleHeaderOffset).CopyTo(rgMarker, 0);
        Array.Copy(rgSignature, 0, rgMarker, 8, 32);
        fs.Write(rgMarker, 0, 40);
        Pad(fs, (int)(uLamAppRaw - (lMarkerRaw + 40)));
        fs.Write(rgLamApp, 0, rgLamApp.Length);
        Pad(fs, (int)(uLamAppRawSize - rgLamApp.Length));
        Pad(fs, (int)(lBundleStart - (uLamAppRaw + uLamAppRawSize)));
        fs.Write(rgBundleData, 0, rgBundleData.Length);
        fs.Write(rgNewHeader, 0, rgNewHeader.Length);
        fs.Flush(true);
    }

    private static void WriteSection(byte[] rgHdrs, int iOff, string sName, uint uRva, uint uVs, uint uRawSize, uint uRaw)
    {
        byte[] rgName = System.Text.Encoding.ASCII.GetBytes(sName.PadRight(8, '\0'));
        Array.Copy(rgName, 0, rgHdrs, iOff, 8);
        BitConverter.GetBytes(uVs).CopyTo(rgHdrs, iOff + 8);
        BitConverter.GetBytes(uRawSize).CopyTo(rgHdrs, iOff + 16);
        BitConverter.GetBytes(uRva).CopyTo(rgHdrs, iOff + 12);
        BitConverter.GetBytes(uRaw).CopyTo(rgHdrs, iOff + 20);
        uint uChar = sName == ".text" ? 0xE0000020u : 0x40000040u;
        BitConverter.GetBytes(uChar).CopyTo(rgHdrs, iOff + 36);
    }

    #endregion
    #region 辅助

    private static uint AlignUp(uint uV, uint uA) => uA == 0 ? uV : (uV + uA - 1) & ~(uA - 1);
    private static void Pad(FileStream fs, int iN) { while (iN-- > 0) fs.WriteByte(0); }

    private static void WriteU32(Stream sStream, uint uV) { sStream.Write(BitConverter.GetBytes(uV), 0, 4); }
    private static void WriteI32(Stream sStream, int iV) { sStream.Write(BitConverter.GetBytes(iV), 0, 4); }
    private static void WriteI64(Stream sStream, long lV) { sStream.Write(BitConverter.GetBytes(lV), 0, 8); }
    private static void WriteU8(Stream sStream, byte bV) { sStream.WriteByte(bV); }
    private static void WriteStr(Stream sStream, string sV)
    {
        byte[] rgB = Encoding.UTF8.GetBytes(sV);
        if (rgB.Length < 0x80) sStream.WriteByte((byte)rgB.Length);
        else { sStream.WriteByte((byte)(0x80 | (rgB.Length >> 8))); sStream.WriteByte((byte)rgB.Length); }
        sStream.Write(rgB, 0, rgB.Length);
    }

    private static uint ReadU32(byte[] rgB, ref int iPos) { uint uV = BitConverter.ToUInt32(rgB, iPos); iPos += 4; return uV; }
    private static int ReadI32(byte[] rgB, ref int iPos) { int iV = BitConverter.ToInt32(rgB, iPos); iPos += 4; return iV; }
    private static long ReadI64(byte[] rgB, ref int iPos) { long lV = BitConverter.ToInt64(rgB, iPos); iPos += 8; return lV; }
    private static byte ReadU8(byte[] rgB, ref int iPos) { return rgB[iPos++]; }
    private static string ReadStr(byte[] rgB, ref int iPos)
    {
        int iLen = rgB[iPos++];
        if ((iLen & 0x80) != 0) iLen = ((iLen & 0x7F) << 8) | rgB[iPos++];
        string sRes = System.Text.Encoding.UTF8.GetString(rgB, iPos, iLen);
        iPos += iLen;
        return sRes;
    }

    #endregion
}
