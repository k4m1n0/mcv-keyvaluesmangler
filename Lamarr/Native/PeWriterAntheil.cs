using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;

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

    private byte[] rgBoot = null!;
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

    //lamapp区 BSJB头+seed payload布局
    private const int iBsjbLen = 64;
    private const int iSeedOff = 64;
    private const int iHashOff = 96;
    private const int iHeadOff = 100;
    private const int iTblOff = 116;
    private const string sDecoder = "Iamdec";
    private const string sJit = "!!jhk";//jithook钩子dll
    private const string sSig = "!!sig";//方法体密文CRC32签名表
    private const string sPheropod = "lamdec";

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
    private string sDecoderPath = "";

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
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Bootstrapper is not a PE: {sPath}");
        rgBoot = rg;
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

        //生成seed并重建bundle条目
        string sSeed = MakeSeed();
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

    private void BuildLamApp(long[] rgRel, long[] rgSz, long[] rgCsz, string[] rgName, List<int> rgDepIdx, string sSeed)
    {
        var rgRaw = new List<byte[]>();
        var rgNames = new List<string>();

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
        //追加真解码器Iamdec条目
        rgRaw.Add(rgDecoder); rgNames.Add(sDecoder);

        if (rgJitHook != null)
        {
            uint uJitKey = Fnv1a(rgKSeed);
            var rgAllCrcs = new List<uint>();
            int iNEncMethods = 0;
            for (int i = 0; i < rgRaw.Count - 1; i++)//对每个托管dll加密方法体
            {
                var rgCrcs = MethodBodyEncryptor.EncryptAll(rgRaw[i], uJitKey);
                rgAllCrcs.AddRange(rgCrcs);
                iNEncMethods += rgCrcs.Count;
            }
            Console.WriteLine($"[jithook] encrypted {iNEncMethods} method bodies -> {rgAllCrcs.Count} sigs");
            var rgSigBytes = new byte[rgAllCrcs.Count * 4];
            for (int i = 0; i < rgAllCrcs.Count; i++)
                BitConverter.GetBytes(rgAllCrcs[i]).CopyTo(rgSigBytes, i * 4);
            rgRaw.Add(rgJitHook); rgNames.Add(sJit);
            rgRaw.Add(rgSigBytes);  rgNames.Add(sSig);
        }

        //附加jithook钩子与诱饵
        if (rgPheropod != null)
        {
            rgRaw.Add(rgPheropod); rgNames.Add(sPheropod);
            Console.WriteLine($"[pheropod] gzip decoy: {rgPheropod.Length} bytes");
        }

        int iCount = rgRaw.Count;

        //生成随机诱饵条目混淆bundle
        const int iDecoys = 4;
        var rng = new Random(Environment.TickCount ^ iCount);
        var rgDecName = new byte[iDecoys][];
        var rgDecData = new byte[iDecoys][];
        var rgDecoyOff = new uint[iDecoys];
        uint uDecoyLen = 0;
        for (int i = 0; i < iDecoys; i++)
        {
            int iNl = rng.Next(4, 16);
            rgDecName[i] = new byte[iNl];
            for (int j = 0; j < iNl; j++)
                rgDecName[i][j] = (byte)('a' + rng.Next(26));
            byte[] rgPlain = GenRandomX64(rng, 64, 512);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true))
                gz.Write(rgPlain, 0, rgPlain.Length);
            rgDecData[i] = ms.ToArray();
            rgDecoyOff[i] = uDecoyLen;
            uDecoyLen += (uint)rgDecData[i].Length;
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
        uint uDataOff = 0;
        int iDec = -1;
        for (int i = 0; i < iCount; i++)
        {
            rgRawLen[i] = (uint)rgRaw[i].Length;
            if (rgNames[i] == sDecoder || rgNames[i] == sJit || rgNames[i] == sSig || rgNames[i] == sPheropod)
            {
                if (rgNames[i] == sDecoder) iDec = i;
                rgCompLen[i] = rgRawLen[i];
                rgBlocks[i] = XorBytes(rgRaw[i], rgKSeed);
            }
            else
            {
                uint cbCap = LamarrEncoder.GetMaxEncodedSize(rgRawLen[i]);
                rgBlocks[i] = new byte[cbCap];
                uint pcb = cbCap;
                if (LamarrEncoder.Encode(rgBlocks[i], ref pcb, rgRaw[i], rgRawLen[i]) != 0)
                    throw new InvalidOperationException($"Lamarr encode failed: {rgNames[i]}");
                rgCompLen[i] = pcb;
            }
            rgCompOff[i] = uDataOff;
            uDataOff += rgCompLen[i];
        }

        uint uTableLen = (uint)((iCount + iDecoys) * 20);
        uint uTotalLen = iTblOff + uTableLen + uNameAreaLen + uDataOff + uDecoyLen;
        rgLamApp = new byte[uTotalLen];

        uint cbOrigTotal = 0;
        for (int i = 0; i < iCount; i++) cbOrigTotal += rgRawLen[i];

        //由seed派生key与置换表
        uint uKey = LamKey(sSeed);
        int[] rgPerm = LamPerm(uKey);

        //假BSJB根 XOR写入lamapp头部
        int iNameOff = iTblOff + (iCount + iDecoys) * 20;
        int iDoff = iNameOff + (int)uNameAreaLen;
        int iDecOff = iDec >= 0 ? iDoff + (int)rgCompOff[iDec] : 0;
        byte[] rgBsjb = BuildFakeBsjb(iNameOff, (int)uNameAreaLen, iDecOff, iDec >= 0 ? (int)rgCompLen[iDec] : 0);
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
                rgF[0] = (uint)rgNameBytes[i].Length;
                rgF[1] = rgRawLen[i];
                rgF[2] = rgCompLen[i];
                rgF[3] = rgCompOff[i];
            }
            else
            {
                int iDd = i - iCount;
                rgF[0] = (uint)rgNameBytes[i].Length;
                rgF[1] = (uint)rgDecData[iDd].Length;
                rgF[2] = (uint)rgDecData[iDd].Length;
                rgF[3] = uDataOff + rgDecoyOff[iDd];
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
        for (int i = 0; i < iCount; i++)
            Array.Copy(rgBlocks[i], 0, rgLamApp, iDataBase + (int)rgCompOff[i], rgCompLen[i]);
        for (int i = 0; i < iDecoys; i++)
            Array.Copy(rgDecData[i], 0, rgLamApp, iDataBase + (int)(uDataOff + rgDecoyOff[i]), rgDecData[i].Length);

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
        while (ms.Length < cbTarget)
        {
            byte[] b = X64Insn(rng);
            ms.Write(b, 0, b.Length);
        }
        return ms.ToArray();
    }

    private static byte[] X64Insn(Random rng)
    {
        switch (rng.Next(10))
        {
            case 0: return new byte[] { 0x90 };                                  //nop
            case 1: return new byte[] { (byte)(0x50 + rng.Next(8)) };            //push rX
            case 2: return new byte[] { (byte)(0x58 + rng.Next(8)) };            //pop rX
            case 3: return new byte[] { 0x31, (byte)(0xC0 + rng.Next(8)) };      //xor rX,rAX
            case 4: return new byte[] { 0x48, 0x8B, (byte)(0xC0 + rng.Next(8)) };//mov rAX,rX
            case 5: { var b = new byte[10]; b[0] = 0x48; b[1] = 0xB8;            //mov rAX,imm64
                    BitConverter.GetBytes(((ulong)(uint)rng.Next() << 32) | (uint)rng.Next()).CopyTo(b, 2);
                    return b; }
            case 6: return new byte[] { 0x48, 0x83, (byte)(0xC0 + rng.Next(8)), (byte)rng.Next(256) };//add rX,imm8
            case 7: return new byte[] { 0xEB, (byte)rng.Next(256) };             //jmp rel8
            case 8: { var b = new byte[7]; b[0] = 0x48; b[1] = 0x8D;             //lea rX,[rAX+disp32]
                    b[2] = (byte)(0x80 + rng.Next(8));
                    BitConverter.GetBytes(rng.Next()).CopyTo(b, 3);
                    return b; }
            default: return new byte[] { 0xC3 };                                 //ret
        }
    }

    private static uint Fnv1a(byte[] rgD)
    {
        uint uH = uLK0A ^ uLK0B;
        foreach (byte bX in rgD) { uH ^= bX; uH *= uLK1A ^ uLK1B; }
        return uH;
    }

    private static byte[] XorBytes(byte[] rgD, byte[] rgKey)
    {
        byte[] rgR = new byte[rgD.Length];
        for (int i = 0; i < rgD.Length; i++) rgR[i] = (byte)(rgD[i] ^ rgKey[i % rgKey.Length]);
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
            }
            rgBundleOffsets[k] = ms.Position;
            ms.Write(rgData, 0, rgData.Length);
            rgBundleCsz[k] = i == iMainEntry ? 0 : (rgCsz[i] > 0 ? rgData.Length : 0);
            rgBundleSz[k] = i == iMainEntry || rgName[i].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
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

        using var hd = new MemoryStream();
        WriteU32(hd, uMajor);
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
            WriteI64(hd, lRtcSz);
            WriteI64(hd, lRtcHash);
        }

        for (int k = 0; k < iM; k++)
        {
            WriteI64(hd, lBundleStart + rgBundleOffsets[k]);
            WriteI64(hd, rgBundleSz[k]);
            if (uMajor >= 6)
                WriteI64(hd, rgBundleCsz[k]);
            WriteU8(hd, rgType[keepIdx[k]]);
            WriteStr(hd, rgName[keepIdx[k]]);
        }
        rgNewHeader = hd.ToArray();
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
