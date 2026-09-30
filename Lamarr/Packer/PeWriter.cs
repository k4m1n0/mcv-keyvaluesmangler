using Lamarr;
using System.Text;
using System.Text.Json;

namespace Lamarr.Packer;

internal class PeWriter
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
    private int[] rgEntryOffPos = null!;
    private int iHeaderDepsPos = -1, iHeaderRtcPos = -1;
    private int iIdxDeps = -1, iIdxRtc = -1;
    private long lOrigDepsLoc, lOrigRtcLoc;
    private readonly HashSet<string> rgStripDeps = new(StringComparer.OrdinalIgnoreCase);
    //null=不改版本
    private string? sRtcVersion = null;
    public void SetRtcVersion(string sV) { sRtcVersion = string.IsNullOrEmpty(sV) ? null : sV; }
    private string sTiered = "default";
    public void SetTiered(string sM) { sTiered = sM; }
    //载荷TFM主版本=桩的首要主版本 由构建链传入
    private int iPayloadMajor = 0;
    public void SetPayloadMajor(int iV) { iPayloadMajor = iV; }
    //null=沿用载荷值 否则取该主版本最新运行时 缺失取更高一级
    private string? sRollForward = null;
    public void SetRollForward(string? sV) { sRollForward = string.IsNullOrEmpty(sV) ? null : sV; }
    internal const string sRollForwardDefault = "Major";
    private int iEntryCount;
    private int iEntryStart = -1;
    private int iNewDeps = -1, iNewRtc = -1;
    private long[] rgNewSzVals = null!;
    private long[] rgNewCszVals = null!;

    private byte[] rgBoot = null!;
    private byte[] rgLamApp = null!;
    private uint uLamAppRaw, uLamAppRawSize;
    private int iMainEntry = -1;
    private string sMainName = "";
    private string sPayloadPath = "";
    private byte[]? rgRes;
    private uint uResRva, uResLen, uResDirOff, uResDirSize;

    //8字节头部偏移加32字节签名
    private static readonly byte[] rgSignature = new byte[]
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

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
        if (uRaw > (uint)rgStub.Length || uVs > (uint)(rgStub.Length - (int)uRaw)) throw new InvalidDataException("stub section truncated");
        rgStubCode = new byte[uVs];
        Array.Copy(rgStub, (int)uRaw, rgStubCode, 0, (int)uVs);

        int iOptStub = iPe + 24;
        uint uResDirRva = BitConverter.ToUInt32(rgStub, iOptStub + 0x70 + 2 * 8);
        uResDirSize = BitConverter.ToUInt32(rgStub, iOptStub + 0x70 + 2 * 8 + 4);
        if (uResDirRva != 0 && uResDirSize != 0)
        {
            for (int i = 0; i < usCnt; i++)
            {
                int iOff = iS + i * 40;
                uint ua = BitConverter.ToUInt32(rgStub, iOff + 12);
                uint uv = BitConverter.ToUInt32(rgStub, iOff + 8);
                uint ur = BitConverter.ToUInt32(rgStub, iOff + 20);
                uint urs = BitConverter.ToUInt32(rgStub, iOff + 16);
                if (ua == 0 || uResDirRva < ua || uResDirRva >= ua + Math.Max(uv, urs)) continue;
                uResRva = ua;
                uResLen = Math.Max(uv, urs);
                uResDirOff = uResDirRva - uResRva;
                rgRes = new byte[uResLen];
                long lCopy = Math.Min(uResLen, Math.Max(0, rgStub.Length - (int)ur));
                Array.Copy(rgStub, (int)ur, rgRes, 0, (int)lCopy);
                break;
            }
        }

        //push rbp mov rbp,rsp
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
        uFileAlign = BitConverter.ToUInt32(rgPayload, iOptOff + 36);
        uSizeOfHdrs = BitConverter.ToUInt32(rgPayload, iOptOff + 60);

        lBundleHeaderOffset = FindMarkerOffset(rgPayload);
        if (lBundleHeaderOffset <= 0)
            throw new InvalidOperationException("Input is not a .NET single-file bundle (marker not found)");
        iBundleDataStart = (int)ParseBundleFirstEntryOffset(rgPayload, lBundleHeaderOffset);
        if (iBundleDataStart <= 0 || iBundleDataStart >= rgPayload.Length)
            throw new InvalidOperationException("Invalid bundle layout");
        sMainName = $"{Path.GetFileNameWithoutExtension(sPath)}.dll";
    }

    public void LoadBoot(string sPath)
    {
        byte[] rg = File.ReadAllBytes(sPath);
        int iPe = BitConverter.ToInt32(rg, 0x3C);
        if (iPe + 0x18 > rg.Length || BitConverter.ToUInt32(rg, iPe) != 0x4550)
            throw new InvalidOperationException($"Bootstrapper is not a PE: {sPath}");
        rgBoot = rg;
    }

    public void Pack(string sOutPath)
    {
        if (rgBoot == null)
            throw new InvalidOperationException("Bootstrapper not loaded (--boot required)");

        RebuildBundle();

        ComputeLayout();
        ApplyBundleOffsets();
        PatchStubVars(sOutPath);

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
    }

    private void ComputeLayout()
    {
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        uLamAppRaw = AlignUp(uStubRaw + uStubRawSize, uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        lMarkerRaw = uLamAppRaw + uLamAppRawSize;//标记在容器之后 按签名反搜定位 与位置无关
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 40, uFileAlign);
        lBundleStart = uBundleRaw;
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }

    private long FindMarkerOffset(byte[] rgB)
    {
        //不设搜索上限 标记位置随bundle大小变化
        for (int i = 0; i + 40 <= rgB.Length; i++)
            if (rgB[i + 8] == rgSignature[0] && MatchSig(rgB, i + 8))
                return BitConverter.ToInt64(rgB, i);
        return -1;
    }

    //从载荷runtimeconfig派生 框架主版本/版本/分层设置 未显式传参时用
    public static bool PeekPayload(string sPath, out int iMajor, out string? sVersion, out string? sTiered)
    {
        iMajor = 0; sVersion = null; sTiered = null;
        try
        {
            byte[] rg = File.ReadAllBytes(sPath);
            var pe = new PeWriter();
            if (!pe.FindRtcEntry(rg, out int iOfs, out int iSize, out int iCsz)) return false;
            byte[] rgJson = ReadMember(rg, iOfs, iSize, iCsz);
            if (rgJson.Length == 0) return false;
            using var doc = JsonDocument.Parse(rgJson);
            if (!doc.RootElement.TryGetProperty("runtimeOptions", out JsonElement oRt)) return false;
            if (oRt.TryGetProperty("framework", out JsonElement oFw) && oFw.TryGetProperty("version", out JsonElement oVer) && oVer.ValueKind == JsonValueKind.String)
            {
                sVersion = oVer.GetString();
                if (!string.IsNullOrEmpty(sVersion))
                {
                    int iDot = sVersion!.IndexOf('.');
                    if (iDot > 0 && int.TryParse(sVersion.Substring(0, iDot), out int iMj)) iMajor = iMj;
                }
            }
            sTiered = "default";
            if (oRt.TryGetProperty("configProperties", out JsonElement oCfg) && oCfg.ValueKind == JsonValueKind.Object
                && oCfg.TryGetProperty("System.Runtime.TieredCompilation", out JsonElement oT) && oT.ValueKind == JsonValueKind.False)
                sTiered = "off";
            return sVersion != null || iMajor > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool FindRtcEntry(byte[] rg, out int iOfs, out int iSize, out int iCsz)//定位单文件bundle里的runtimeconfig成员
    {
        iOfs = -1; iSize = 0; iCsz = 0;
        long lHdr = FindMarkerOffset(rg);
        if (lHdr <= 0) return false;
        int iDataStart = (int)ParseBundleFirstEntryOffset(rg, lHdr);
        if (iDataStart <= 0 || iDataStart >= rg.Length) return false;
        int iPos = (int)lHdr;
        uint uMajor = ReadU32(rg, ref iPos);
        ReadU32(rg, ref iPos);
        int iN = ReadI32(rg, ref iPos);
        ReadStr(rg, ref iPos);
        if (uMajor >= 2)
        {
            ReadI64(rg, ref iPos); ReadI64(rg, ref iPos); ReadI64(rg, ref iPos);
            ReadI64(rg, ref iPos); ReadI64(rg, ref iPos);
        }
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            long lRel = ReadI64(rg, ref iPos);
            long lSz = ReadI64(rg, ref iPos);
            long lCsz = uMajor >= 6 ? ReadI64(rg, ref iPos) : 0;
            ReadU8(rg, ref iPos);
            string sName = ReadStr(rg, ref iPos);
            if (!sName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) continue;
            long lAbs = iDataStart + lRel;
            if (lAbs < 0 || lAbs >= rg.Length) return false;
            int iLen = (int)(lCsz > 0 && lCsz < lSz ? lCsz : lSz);
            if (iLen <= 0 || lAbs + iLen > rg.Length) return false;
            iOfs = (int)lAbs; iSize = (int)lSz; iCsz = (int)lCsz;
            return true;
        }
        return false;
    }

    private static byte[] ReadMember(byte[] rg, int iOfs, int iSize, int iCsz)//成员可能是原样 也可能是压缩存放
    {
        if (iCsz > 0 && iCsz != iSize)
        {
            using var ms = new MemoryStream(rg, iOfs, iCsz);
            using var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress);
            using var oOut = new MemoryStream(iSize > 0 ? iSize : iCsz * 4);
            ds.CopyTo(oOut);
            return oOut.ToArray();
        }
        byte[] rgOut = new byte[iSize];
        Buffer.BlockCopy(rg, iOfs, rgOut, 0, iSize);
        return rgOut;
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
        for (int i = 0; i < iN && i < 0x1000; i++)//防坏头
        {
            long lOff = ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos);
            if (uMajor >= 6)
                ReadI64(rgB, ref iPos);             //csz
            ReadU8(rgB, ref iPos);
            ReadStr(rgB, ref iPos);
            if (i == 0) lFirst = lOff;
        }
        return lFirst;
    }

    #endregion
    #region bundle重建

    private void RebuildBundle()
    {
        int iPos = (int)lBundleHeaderOffset;
        uint uMajor = ReadU32(rgPayload, ref iPos);
        ReadU32(rgPayload, ref iPos);
        int iN = ReadI32(rgPayload, ref iPos);
        ReadStr(rgPayload, ref iPos);

        int iHbase = (int)lBundleHeaderOffset;
        iHeaderDepsPos = -1; iHeaderRtcPos = -1; iIdxDeps = -1; iIdxRtc = -1;
        if (uMajor >= 2)
        {
            iHeaderDepsPos = iPos - iHbase; lOrigDepsLoc = ReadI64(rgPayload, ref iPos);
            ReadI64(rgPayload, ref iPos);
            iHeaderRtcPos = iPos - iHbase; lOrigRtcLoc = ReadI64(rgPayload, ref iPos);
            ReadI64(rgPayload, ref iPos);
            ReadI64(rgPayload, ref iPos);
        }

        iEntryStart = iPos - iHbase;

        var rgNames = new string[iN];
        var rgRel = new long[iN];
        var rgSz = new long[iN];
        var rgCsz = new long[iN];
        var rgType = new byte[iN];
        iMainEntry = -1;
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            rgRel[i] = ReadI64(rgPayload, ref iPos) - iBundleDataStart;
            rgSz[i] = ReadI64(rgPayload, ref iPos);
            rgCsz[i] = uMajor >= 6 ? ReadI64(rgPayload, ref iPos) : 0;
            rgType[i] = ReadU8(rgPayload, ref iPos);
            rgNames[i] = ReadStr(rgPayload, ref iPos);
            if (iMainEntry < 0 && rgNames[i].Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                iMainEntry = i;
        }
        if (iMainEntry < 0)
            throw new InvalidOperationException($"Bundle main assembly '{sMainName}' not found. Input: '{sPayloadPath}'");

        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            long lOrigAbs = iBundleDataStart + rgRel[i];
            if (iIdxDeps < 0 && uMajor >= 2 && lOrigAbs == lOrigDepsLoc) iIdxDeps = i;
            if (iIdxRtc < 0 && uMajor >= 2 && lOrigAbs == lOrigRtcLoc) iIdxRtc = i;
        }

        //主程序集与托管依赖各自压缩进多条目容器 bundle只留Boot/依赖清单/runtimeconfig
        var rgLamNames = new List<string> { sMainName };
        var rgLamData = new List<byte[]> { ReadEntryBytes(rgRel[iMainEntry], rgCsz[iMainEntry], rgSz[iMainEntry]) };
        var rgKeepIdx = new List<int>();
        for (int i = 0; i < iN && i < 0x1000; i++)
        {
            if (i == iMainEntry || i == iIdxDeps || i == iIdxRtc) continue;
            if (rgNames[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                rgLamNames.Add(rgNames[i]);
                rgLamData.Add(ReadEntryBytes(rgRel[i], rgCsz[i], rgSz[i]));
                rgStripDeps.Add(rgNames[i].Substring(0, rgNames[i].Length - 4));
            }
            else
            {
                rgKeepIdx.Add(i);
            }
        }
        BuildLamApp(rgLamNames, rgLamData);

        using var ms = new MemoryStream();
        var rgNewNames = new List<string>();
        var rgNewSz = new List<long>();
        var rgNewCsz = new List<long>();
        var rgNewType = new List<byte>();
        rgBundleOffsets = new long[iN];
        iEntryCount = 0;

        AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, sMainName, rgBoot, rgBoot.Length, 0, rgType[iMainEntry]);
        foreach (int i in rgKeepIdx)
            AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, rgNames[i],
                ReadEntryBytes(rgRel[i], rgCsz[i], rgSz[i]), rgSz[i], rgCsz[i], rgType[i]);
        if (iIdxDeps >= 0)
        {
            byte[] rgDeps = ReadEntryBytes(rgRel[iIdxDeps], rgCsz[iIdxDeps], rgSz[iIdxDeps]);
            rgDeps = StripDepsDependencies(rgDeps);
            AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, rgNames[iIdxDeps],
                rgDeps, rgDeps.Length, 0, rgType[iIdxDeps]);
        }
        if (iIdxRtc >= 0)
        {
            byte[] rgRtc = ReadEntryBytes(rgRel[iIdxRtc], rgCsz[iIdxRtc], rgSz[iIdxRtc]);
            rgRtc = RewriteRuntimeConfig(rgRtc);
            AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, rgNames[iIdxRtc],
                rgRtc, rgRtc.Length, 0, rgType[iIdxRtc]);
        }
        rgBundleData = ms.ToArray();
        lBundleDataLen = rgBundleData.Length;

        BuildNewBundleHeader(uMajor, iHbase, rgNewNames, rgNewSz, rgNewCsz, rgNewType);
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
                                    //仅调用方要求时改写框架版本 否则保持已发布值
                                    if (f.NameEquals("version"))
                                    {
                                        if (!string.IsNullOrEmpty(sRtcVersion)) w.WriteString("version", sRtcVersion);
                                        else f.WriteTo(w);
                                    }
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
                                if (f.NameEquals("version"))
                                {
                                    if (!string.IsNullOrEmpty(sRtcVersion)) w.WriteString("version", sRtcVersion);
                                    else f.WriteTo(w);
                                }
                                else f.WriteTo(w);
                            }
                            w.WriteEndObject();
                        }
                        else if (op.NameEquals("configProperties") && op.Value.ValueKind == JsonValueKind.Object)
                        {
                            w.WritePropertyName("configProperties");
                            w.WriteStartObject();
                            foreach (var cp in op.Value.EnumerateObject())
                            {
                                if (sTiered == "off" && cp.NameEquals("System.Runtime.TieredCompilation")) continue;//统一写一次 同名键出现两次宿主行为不确定
                                cp.WriteTo(w);
                            }
                            //分层只在显式关闭时写false 缺省保留运行时默认
                            if (sTiered == "off") w.WriteBoolean("System.Runtime.TieredCompilation", false);
                            w.WriteEndObject();
                        }
                        else if (op.NameEquals("rollForward"))
                        {
                            //调用方给值优先 载荷已发布值保留 都没有才写默认策略
                            if (sRollForward != null) w.WriteString("rollForward", sRollForward);
                            else op.WriteTo(w);
                            bRoll = true;
                        }
                        else op.WriteTo(w);
                    }
                    if (!bRoll) w.WriteString("rollForward", sRollForward ?? sRollForwardDefault);
                    w.WriteEndObject();
                }
                else p.WriteTo(w);
            }
            w.WriteEndObject();
            w.Flush();
        }
        return ms.ToArray();
    }

    //已剥离的程序集仍会被宿主按依赖表加载
    private byte[] StripDepsDependencies(byte[] rgDeps)
    {
        using var doc = JsonDocument.Parse(rgDeps);
        var jeRoot = doc.RootElement;
        var rgStripAll = new HashSet<string>(rgStripDeps, StringComparer.OrdinalIgnoreCase);

        //剔除没有运行时资产的依赖包
        if (jeRoot.TryGetProperty("targets", out var rgTargets))
            foreach (var jpTfm in rgTargets.EnumerateObject())
                foreach (var jpPkg in jpTfm.Value.EnumerateObject())
                {
                    bool bHasRuntime = false;
                    foreach (var iPos in jpPkg.Value.EnumerateObject())
                        if (iPos.Name == "runtime" || iPos.Name == "runtimeTargets") { bHasRuntime = true; break; }
                    if (!bHasRuntime)
                        rgStripAll.Add(jpPkg.Name.Split('/')[0]);
                }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var jpProp in jeRoot.EnumerateObject())
            {
                if (jpProp.Name == "targets")
                {
                    w.WritePropertyName("targets");
                    w.WriteStartObject();
                    foreach (var jpTfm in jpProp.Value.EnumerateObject())
                    {
                        w.WritePropertyName(jpTfm.Name);
                        w.WriteStartObject();
                        foreach (var jpPkg in jpTfm.Value.EnumerateObject())
                        {
                            if (rgStripAll.Contains(jpPkg.Name.Split('/')[0]))
                                continue;
                            w.WritePropertyName(jpPkg.Name);
                            w.WriteStartObject();
                            foreach (var iPos in jpPkg.Value.EnumerateObject())
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
                else if (jpProp.Name == "libraries")
                {
                    w.WritePropertyName("libraries");
                    w.WriteStartObject();
                    foreach (var jpLib in jpProp.Value.EnumerateObject())
                    {
                        if (rgStripAll.Contains(jpLib.Name.Split('/')[0]))
                            continue;
                        w.WritePropertyName(jpLib.Name);
                        jpLib.Value.WriteTo(w);
                    }
                    w.WriteEndObject();
                }
                else
                {
                    w.WritePropertyName(jpProp.Name);
                    jpProp.Value.WriteTo(w);
                }
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private byte[] ReadEntryBytes(long lRel, long lCsz, long lSz)
    {
        long l = lCsz > 0 ? lCsz : lSz;
        byte[] rg = new byte[l];
        Array.Copy(rgPayload, (int)(iBundleDataStart + lRel), rg, 0, (int)l);
        return rg;
    }

    private void AddBundleEntry(MemoryStream ms, List<string> rgNames, List<long> rgSz, List<long> rgCsz, List<byte> rgType, string sName, byte[] rgData, long lSz, long lCsz, byte bType)
    {
        rgNames.Add(sName);
        rgSz.Add(lSz);
        rgCsz.Add(lCsz);
        rgType.Add(bType);
        rgBundleOffsets[iEntryCount] = ms.Position;
        ms.Write(rgData, 0, rgData.Length);
        iEntryCount++;
    }

    //容器头含标记与条目数与原长与压缩长 后随条目表与名字表与压缩块
    private void BuildLamApp(List<string> rgNames, List<byte[]> rgData)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("Lamarr!!"), 0, 8);
        ms.Write(BitConverter.GetBytes((uint)rgNames.Count), 0, 4);
        long lSizePos = ms.Position; ms.Write(new byte[8], 0, 8);
        long lTable = ms.Position; ms.Position += 20L * rgNames.Count;
        var rgNameLen = new uint[rgNames.Count];
        for (int i = 0; i < rgNames.Count; i++)
        {
            byte[] rgB = Encoding.UTF8.GetBytes(rgNames[i]);
            rgNameLen[i] = (uint)rgB.Length;
            ms.Write(rgB, 0, rgB.Length);
            while ((ms.Position & 3) != 0) ms.WriteByte(0);
        }
        var rgCompOff = new long[rgNames.Count];
        var rgCompLen = new long[rgNames.Count];
        long lTotalOrig = 0, lTotalComp = 0;
        for (int i = 0; i < rgNames.Count; i++)
        {
            uint uCap = LamarrEncoder.GetMaxEncodedSize((uint)rgData[i].Length);
            byte[] rgComp = new byte[uCap];
            uint uPcb = uCap;
            if (LamarrEncoder.Encode(rgComp, ref uPcb, rgData[i], (uint)rgData[i].Length) != 0)
                throw new InvalidOperationException("Lamarr encode failed (lamapp entry)");
            rgCompOff[i] = ms.Position;
            rgCompLen[i] = uPcb;
            ms.Write(rgComp, 0, (int)uPcb);
            lTotalOrig += rgData[i].Length;
            lTotalComp += uPcb;
        }
        byte[] rgBuf = ms.GetBuffer();
        for (int i = 0; i < rgNames.Count; i++)
        {
            int iO = (int)(lTable + i * 20);
            BitConverter.GetBytes(rgNameLen[i]).CopyTo(rgBuf, iO);
            BitConverter.GetBytes((uint)rgData[i].Length).CopyTo(rgBuf, iO + 4);
            BitConverter.GetBytes((uint)rgCompLen[i]).CopyTo(rgBuf, iO + 8);
            BitConverter.GetBytes((uint)rgCompOff[i]).CopyTo(rgBuf, iO + 12);
        }
        BitConverter.GetBytes((uint)lTotalOrig).CopyTo(rgBuf, lSizePos);
        BitConverter.GetBytes((uint)lTotalComp).CopyTo(rgBuf, lSizePos + 4);
        rgLamApp = ms.ToArray();
        Console.WriteLine($"  .lamapp: {rgNames.Count} entries, {lTotalOrig} -> {lTotalComp} bytes (Lamarr)");
    }

    private void BuildNewBundleHeader(uint uMajor, int iHbase, List<string> rgNames, List<long> rgSz, List<long> rgCsz, List<byte> rgType)
    {
        byte[] rgPrefix = new byte[iEntryStart];
        Array.Copy(rgPayload, iHbase, rgPrefix, 0, iEntryStart);
        BitConverter.GetBytes(rgNames.Count).CopyTo(rgPrefix, 8);
        uint uOutMajor = uMajor >= 2 ? 2u : uMajor;
        BitConverter.GetBytes(uOutMajor).CopyTo(rgPrefix, 0);

        using var ms = new MemoryStream();
        ms.Write(rgPrefix, 0, rgPrefix.Length);
        rgEntryOffPos = new int[rgNames.Count];
        rgNewSzVals = new long[rgNames.Count];
        rgNewCszVals = new long[rgNames.Count];
        iNewDeps = -1; iNewRtc = -1;
        for (int i = 0; i < rgNames.Count; i++)
        {
            rgEntryOffPos[i] = (int)ms.Position;
            rgNewSzVals[i] = rgSz[i];
            rgNewCszVals[i] = rgCsz[i];
            byte[] rgName = Encoding.UTF8.GetBytes(rgNames[i]);
            ms.Position += 8 + 8 + 1;//major 2没csz字段
            WriteBundleStr(ms, rgName);
            if (rgNames[i].Contains("deps.json")) iNewDeps = i;
            if (rgNames[i].Contains("runtimeconfig")) iNewRtc = i;
        }
        rgNewHeader = ms.ToArray();
    }

    private static void WriteBundleStr(MemoryStream ms, byte[] rgB)
    {
        if (rgB.Length >= 0x8000) throw new InvalidOperationException($"bundle string too long for the 2 byte prefix: {rgB.Length}");//前缀只有7加8位 再长就会静默截断
        if (rgB.Length < 0x80) ms.WriteByte((byte)rgB.Length);
        else { ms.WriteByte((byte)(0x80 | (rgB.Length >> 8))); ms.WriteByte((byte)(rgB.Length & 0xFF)); }
        ms.Write(rgB, 0, rgB.Length);
    }

    private void ApplyBundleOffsets()
    {
        if (iHeaderDepsPos >= 0 && iNewDeps >= 0)
        {
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[iNewDeps]).CopyTo(rgNewHeader, iHeaderDepsPos);
            BitConverter.GetBytes(rgNewSzVals[iNewDeps]).CopyTo(rgNewHeader, iHeaderDepsPos + 8);//deps size
        }
        if (iHeaderRtcPos >= 0 && iNewRtc >= 0)
        {
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[iNewRtc]).CopyTo(rgNewHeader, iHeaderRtcPos);
            BitConverter.GetBytes(rgNewSzVals[iNewRtc]).CopyTo(rgNewHeader, iHeaderRtcPos + 8);//rtc size
        }
        for (int i = 0; i < iEntryCount && i < 0x1000; i++)
        {
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[i]).CopyTo(rgNewHeader, rgEntryOffPos[i]);
            BitConverter.GetBytes(rgNewSzVals[i]).CopyTo(rgNewHeader, rgEntryOffPos[i] + 8);
        }
    }

    private void PatchStubVars(string sOutPath)
    {
        //首要主版本来自构建期的载荷目标框架主版本
        int iPrefMajor = iPayloadMajor;

        ReplaceMarker(rgStubCode, "##APPNAME##", Encoding.Unicode.GetBytes(sMainName), 256);
        ReplaceMarker(rgStubCode, "##PREFMAJ##", BitConverter.GetBytes((uint)iPrefMajor), 8);

        //01122334455667788h
        int iOff = IndexOf(rgStubCode, new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 });
        if (iOff < 0)
            throw new InvalidOperationException("gHeaderOff marker not found in stub");
        Array.Copy(BitConverter.GetBytes(lNewBundleHeaderOffset), 0, rgStubCode, iOff, 8);
        Console.WriteLine($"  app_name: {sMainName}");
        Console.WriteLine($"  pref_major: {iPrefMajor}");
        Console.WriteLine($"  header_offset: 0x{lNewBundleHeaderOffset:X}");
    }

    private static void ReplaceMarker(byte[] rgB, string sMarker, byte[] rgValue, int iSpace)
    {
        byte[] rgPat = Encoding.ASCII.GetBytes(sMarker);
        int iOff = IndexOf(rgB, rgPat);
        if (iOff < 0)
            throw new InvalidOperationException($"Stub marker '{sMarker}' not found");
        if (rgValue.Length > iSpace)
            throw new InvalidOperationException($"Stub value for '{sMarker}' too long ({rgValue.Length} > {iSpace})");
        Array.Clear(rgB, iOff, iSpace);
        Array.Copy(rgValue, 0, rgB, iOff, rgValue.Length);
    }

    private static int IndexOf(byte[] rgB, byte[] rgPat)
    {
        for (int i = 0; i + rgPat.Length <= rgB.Length; i++)
        {
            bool bOk = true;
            for (int j = 0; j < rgPat.Length; j++)
                if (rgB[i + j] != rgPat[j]) { bOk = false; break; }
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
        bool bRes = rgRes?.Length > 0;
        uint uResRvaNew = AlignUp(uLamAppRva + (uint)rgLamApp.Length, uSectAlign);
        uint uResRaw = 0;
        if (bRes)
        {
            uResRaw = AlignUp((uint)(lBundleStart + rgBundleData.Length + rgNewHeader.Length), uFileAlign);
            RebaseRes(uResRvaNew - uResRva);//资源树data entry存绝对RVA 换段须整体平移
        }
        uint uNewImg = bRes
            ? AlignUp(uResRvaNew + (uint)rgRes!.Length, uSectAlign)
            : AlignUp(uLamAppRva + (uint)rgLamApp.Length, uSectAlign);

        byte[] rgHdrs = new byte[uNewHdrs];
        Array.Copy(rgPayload, 0, rgHdrs, 0, Math.Min(uSizeOfHdrs, rgHdrs.Length));
        PeScrub.Scrub(rgHdrs);//产物头部同样清除构建特征
        //桩没有重定位表与LoadConfig 这几个标志位置零
        //0x0040 DYNAMIC_BASE 0x0020 HIGH_ENTROPY_VA 0x4000 GUARD_CF
        ushort usDllChar = (ushort)(BitConverter.ToUInt16(rgHdrs, iOptOff + 70) & ~(0x0040 | 0x0020 | 0x4000));
        BitConverter.GetBytes(usDllChar).CopyTo(rgHdrs, iOptOff + 70);

        BitConverter.GetBytes((ushort)(bRes ? 3 : 2)).CopyTo(rgHdrs, iPeOff + 6);
        BitConverter.GetBytes(uNewImg).CopyTo(rgHdrs, iOptOff + 56);
        BitConverter.GetBytes(uStubRva + uStubEntryOff).CopyTo(rgHdrs, iOptOff + 16);
        //桩没有导入表与重定位表 入口自行解析
        Array.Clear(rgHdrs, iOptOff + 0x70, Math.Min(16 * 8, rgHdrs.Length - (iOptOff + 0x70)));
        if (bRes)
        {
            BitConverter.GetBytes(uResRvaNew + uResDirOff).CopyTo(rgHdrs, iOptOff + 0x70 + 2 * 8);
            BitConverter.GetBytes(uResDirSize).CopyTo(rgHdrs, iOptOff + 0x70 + 2 * 8 + 4);
        }
        BitConverter.GetBytes(uStubRawSize).CopyTo(rgHdrs, iOptOff + 4);
        BitConverter.GetBytes(uLamAppRawSize).CopyTo(rgHdrs, iOptOff + 8);

        Array.Clear(rgHdrs, iSecOff, Math.Min(usSecCount * 40, rgHdrs.Length - iSecOff));
        WriteSection(rgHdrs, iSecOff, ".stub", uStubRva, (uint)rgStubCode.Length, uStubRawSize, uStubRaw);
        WriteSection(rgHdrs, iSecOff + 40, ".lamapp", uLamAppRva, (uint)rgLamApp.Length, uLamAppRawSize, uLamAppRaw);
        if (bRes) WriteSection(rgHdrs, iSecOff + 80, ".rsrc", uResRvaNew, (uint)rgRes!.Length, AlignUp((uint)rgRes!.Length, uFileAlign), uResRaw);

        using var fs = new FileStream(sOutPath, FileMode.Create);
        fs.Write(rgHdrs, 0, rgHdrs.Length);
        Pad(fs, (int)(uStubRaw - uNewHdrs));
        fs.Write(rgStubCode, 0, rgStubCode.Length);
        Pad(fs, (int)(uStubRawSize - rgStubCode.Length));
        Pad(fs, (int)(uLamAppRaw - (uStubRaw + uStubRawSize)));
        fs.Write(rgLamApp, 0, rgLamApp.Length);
        Pad(fs, (int)(uLamAppRawSize - rgLamApp.Length));
        byte[] rgMarker = new byte[40];
        BitConverter.GetBytes(lNewBundleHeaderOffset).CopyTo(rgMarker, 0);
        Array.Copy(rgSignature, 0, rgMarker, 8, 32);
        fs.Write(rgMarker, 0, 40);
        Pad(fs, (int)(lBundleStart - (lMarkerRaw + 40)));
        fs.Write(rgBundleData, 0, rgBundleData.Length);
        fs.Write(rgNewHeader, 0, rgNewHeader.Length);
        if (bRes)
        {
            Pad(fs, (int)(uResRaw - (lBundleStart + rgBundleData.Length + rgNewHeader.Length)));
            fs.Write(rgRes!, 0, rgRes!.Length);
        }
        fs.Flush(true);
    }

    private void RebaseRes(uint uDelta)//资源目录data entry存绝对RVA 换段后整体平移 目录内偏移相对不变
    {
        if (rgRes == null) return;
        Walk(uResDirOff);
        void Walk(uint uDirOff)
        {
            uint uN = (uint)(BitConverter.ToUInt16(rgRes, (int)uDirOff + 12) + BitConverter.ToUInt16(rgRes, (int)uDirOff + 14));
            for (uint i = 0; i < uN; i++)
            {
                uint uEnt = uDirOff + 16 + i * 8;
                uint uOff = BitConverter.ToUInt32(rgRes, (int)uEnt + 4);
                if ((uOff & 0x80000000u) != 0) { Walk(uOff & 0x7FFFFFFFu); continue; }
                uint uDe = uResDirOff + uOff;
                BitConverter.GetBytes(BitConverter.ToUInt32(rgRes, (int)uDe) + uDelta).CopyTo(rgRes, (int)uDe);
            }
        }
    }

    private static void WriteSection(byte[] rgHdrs, int iOff, string sName, uint uRva, uint uVs, uint uRawSize, uint uRaw)
    {
        byte[] rgName = System.Text.Encoding.ASCII.GetBytes(sName.PadRight(8, '\0'));
        Array.Copy(rgName, 0, rgHdrs, iOff, 8);
        BitConverter.GetBytes(uVs).CopyTo(rgHdrs, iOff + 8);
        BitConverter.GetBytes(uRawSize).CopyTo(rgHdrs, iOff + 16);
        BitConverter.GetBytes(uRva).CopyTo(rgHdrs, iOff + 12);
        BitConverter.GetBytes(uRaw).CopyTo(rgHdrs, iOff + 20);
        uint uChar = sName == ".stub" ? 0xE0000020u : 0x40000040u;//R|W|X + CNT_CODE R|X + CNT_INITIALIZED_DATA
        BitConverter.GetBytes(uChar).CopyTo(rgHdrs, iOff + 36);
    }

    #endregion
    #region 辅助

    private static uint AlignUp(uint uV, uint uA) => uA == 0 ? uV : (uV + uA - 1) & ~(uA - 1);
    private static void Pad(FileStream fs, int iN) { while (iN-- > 0) fs.WriteByte(0); }

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
