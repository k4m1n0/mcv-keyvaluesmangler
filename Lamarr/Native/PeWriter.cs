using Lamarr;
using System.Text;
using System.Text.Json;

namespace Lamarr.NativePack;

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
    private long iMarkerRaw;
    private long lBundleStart;

    private byte[] rgBundleData = null!;
    private byte[] rgNewHeader = null!;
    private long lBundleDataLen;
    private long[] rgBundleOffsets = null!;
    private long[] rgBundleCsz = null!;
    private long[] rgBundleSz = null!;
    private int iNewRtcIdx = -1;
    private int rgIdxRtc = -1;
    private int rgEntryCount;
    private readonly HashSet<string> rgStripDeps = new(StringComparer.OrdinalIgnoreCase);

    private byte[] rgBoot = null!;
    private byte[] rgLamApp = null!;
    private uint uLamAppRaw, uLamAppRawSize;
    private int iMainEntry = -1;
    private string sMainName = "";
    private string sPayloadPath = "";

    //8字节header_off + 32字节签名
    private static readonly byte[] rgSignature = new byte[]
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

    private static readonly byte[][] rgX64Pad = new byte[][]
    {
        new byte[] { 0x90, 0x00 },//nop
        new byte[] { 0x48, 0x90 },//xchg rax, rax
        new byte[] { 0x66, 0x90 },//nop
        new byte[] { 0xEB, 0x00 },//jmp +0
        new byte[] { 0x75, 0x00 },//jmp +0
        new byte[] { 0xC3, 0x00 } //ret
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
            int o = iS + i * 40;
            if (BitConverter.ToUInt32(rgStub, o + 12) != 0)
            {
                uVa = BitConverter.ToUInt32(rgStub, o + 12);
                uRaw = BitConverter.ToUInt32(rgStub, o + 20);
                uVs = BitConverter.ToUInt32(rgStub, o + 8);
                break;
            }
        }
        rgStubCode = new byte[uVs];
        Array.Copy(rgStub, (int)uRaw, rgStubCode, 0, (int)Math.Min(uVs, rgStub.Length - uRaw));

        //push rbp; mov rbp,rsp
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
        uFileAlign = 0x200;//合法最小文件对齐 压缩段间padding
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

    public void Pack(string sOutPath)
    {
        if (rgBoot == null)
            throw new InvalidOperationException("Bootstrapper not loaded (--boot required)");

        RebuildBundle();

        ComputeLayout();
        PatchStubVars(sOutPath);

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
    }

    private void ComputeBundleStart()
    {
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        iMarkerRaw = uStubRaw + uStubRawSize;
        uLamAppRaw = AlignUp((uint)(iMarkerRaw + 40), uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);
        lBundleStart = uBundleRaw;
    }

    private void ComputeLayout()
    {
        ComputeBundleStart();
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }

    private long FindMarkerOffset(byte[] b)
    {
        //不设搜索上限 marker位置随bundle大小变化
        for (int i = 0; i + 40 <= b.Length; i++)
            if (b[i + 8] == rgSignature[0] && MatchSig(b, i + 8))
                return BitConverter.ToInt64(b, i);
        return -1;
    }

    private bool MatchSig(byte[] b, int off)
    {
        for (int j = 0; j < 32; j++)
            if (b[off + j] != rgSignature[j]) return false;
        return true;
    }

    private long ParseBundleFirstEntryOffset(byte[] b, long headerOff)
    {
        int p = (int)headerOff;
        uint major = ReadU32(b, ref p);
        ReadU32(b, ref p);
        int n = ReadI32(b, ref p);
        ReadStr(b, ref p);
        if (major >= 2)
        {
            ReadI64(b, ref p); ReadI64(b, ref p);
            ReadI64(b, ref p); ReadI64(b, ref p);
            ReadI64(b, ref p);
        }
        long first = 0;
        for (int i = 0; i < n && i < 0x1000; i++)//防坏头
        {
            long off = ReadI64(b, ref p);
            ReadI64(b, ref p);
            if (major >= 6)
                ReadI64(b, ref p);
            ReadU8(b, ref p);
            ReadStr(b, ref p);
            if (i == 0) first = off;
        }
        return first;
    }

    #endregion
    #region bundle 重建

    private void RebuildBundle()
    {
        int p = (int)lBundleHeaderOffset;
        uint major = ReadU32(rgPayload, ref p);
        ReadU32(rgPayload, ref p);
        int n = ReadI32(rgPayload, ref p);
        string sBundleId = ReadStr(rgPayload, ref p);

        long lDepsSz = 0, lRtcSz = 0, lRtcHash = 0;
        if (major >= 2)
        {
            ReadI64(rgPayload, ref p);
            lDepsSz = ReadI64(rgPayload, ref p);
            ReadI64(rgPayload, ref p);
            lRtcSz = ReadI64(rgPayload, ref p);
            lRtcHash = ReadI64(rgPayload, ref p);
        }

        var rgRel = new long[n];
        var rgSz = new long[n];
        var rgCsz = new long[n];
        var rgType = new byte[n];
        var rgName = new string[n];
        iMainEntry = -1;
        rgIdxRtc = -1;
        for (int i = 0; i < n && i < 0x1000; i++)
        {
            rgRel[i] = ReadI64(rgPayload, ref p) - iBundleDataStart;
            rgSz[i] = ReadI64(rgPayload, ref p);
            if (major >= 6)
                rgCsz[i] = ReadI64(rgPayload, ref p);
            else
                rgCsz[i] = 0;
            rgType[i] = ReadU8(rgPayload, ref p);
            rgName[i] = ReadStr(rgPayload, ref p);
            if (iMainEntry < 0 && rgName[i].Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                iMainEntry = i;
            if (rgIdxRtc < 0 && rgName[i].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                rgIdxRtc = i;
        }
        if (iMainEntry < 0)
            throw new InvalidOperationException($"Bundle main assembly '{sMainName}' not found. Input: '{sPayloadPath}'");

        //条目分类 主程序集位置放boot 托管依赖进.lamapp 其余保留bundle
        var keepIdx = new List<int>();
        var depIdx = new List<int>();
        rgStripDeps.Clear();
        for (int i = 0; i < n && i < 0x1000; i++)
        {
            if (i == iMainEntry) { keepIdx.Add(i); continue; }
            if (rgName[i].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                IsManagedDll(i, rgRel, rgSz, rgCsz))
            {
                depIdx.Add(i);
                rgStripDeps.Add(rgName[i].Substring(0, rgName[i].Length - 4));//去.dll
            }
            else
            {
                keepIdx.Add(i);
            }
        }

        BuildLamApp(rgRel, rgSz, rgCsz, rgName, depIdx);

        //bundle头偏移是文件绝对偏移 必须先定bundle数据区起始
        ComputeBundleStart();
        BuildBundleDataAndHeader(major, sBundleId, keepIdx, rgRel, rgSz, rgCsz, rgType, rgName,
                                 lDepsSz, lRtcSz, lRtcHash);
    }

    private void BuildLamApp(long[] rgRel, long[] rgSz, long[] rgCsz, string[] rgName, List<int> depIdx)
    {
        var rgRaw = new List<byte[]>();
        var rgNames = new List<string>();

        {
            long ondisk = rgCsz[iMainEntry] > 0 ? rgCsz[iMainEntry] : rgSz[iMainEntry];
            byte[] rgDll = new byte[ondisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[iMainEntry]), rgDll, 0, (int)ondisk);
            rgRaw.Add(rgDll); rgNames.Add(sMainName);
        }
        foreach (int i in depIdx)
        {
            long ondisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
            byte[] rgDll = new byte[ondisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgDll, 0, (int)ondisk);
            rgRaw.Add(rgDll); rgNames.Add(rgName[i]);
        }

        int count = rgRaw.Count;
        var nameBytes = new byte[count][];
        uint nameTotal = 0;
        for (int i = 0; i < count; i++)
        {
            nameBytes[i] = Encoding.UTF8.GetBytes(rgNames[i]);
            nameTotal += (uint)nameBytes[i].Length;
        }
        uint nameAreaLen = (nameTotal + 3) & ~3u;

        var rgBlocks = new byte[count][];
        var rawLen = new uint[count];
        var compLen = new uint[count];
        var compOff = new uint[count];
        uint dataOff = 0;
        for (int i = 0; i < count; i++)
        {
            rawLen[i] = (uint)rgRaw[i].Length;
            uint cbCap = LamarrEncoder.GetMaxEncodedSize(rawLen[i]);
            rgBlocks[i] = new byte[cbCap];
            uint pcb = cbCap;
            if (LamarrEncoder.Encode(rgBlocks[i], ref pcb, rgRaw[i], rawLen[i]) != 0)
                throw new InvalidOperationException($"Lamarr encode failed: {rgNames[i]}");
            compLen[i] = pcb;
            compOff[i] = dataOff;
            dataOff += pcb;
        }

        uint tableLen = (uint)(count * 20);
        uint totalLen = 20 + tableLen + nameAreaLen + dataOff;
        rgLamApp = new byte[totalLen];

        uint cbOrigTotal = 0;
        for (int i = 0; i < count; i++) cbOrigTotal += rawLen[i];

        //伪BSJB+伪随机x86指令填充 让段头看起来像dotnet元数据流
        int iPrefMajor = GetOrigRtcMajor(rgRel, rgSz, rgCsz);
        byte[] rgX64 = rgX64Pad[iPrefMajor % rgX64Pad.Length];
        byte[] rgPad = { 0x42, 0x53, rgX64[0], rgX64[1], 0x4A, 0x42, 0x01, 0x00 };
        Array.Copy(rgPad, 0, rgLamApp, 0, 8);

        BitConverter.GetBytes((uint)count).CopyTo(rgLamApp, 8);
        BitConverter.GetBytes(cbOrigTotal).CopyTo(rgLamApp, 12);
        BitConverter.GetBytes(dataOff).CopyTo(rgLamApp, 16);

        for (int i = 0; i < count; i++)
        {
            int o = 20 + i * 20;
            BitConverter.GetBytes((uint)nameBytes[i].Length).CopyTo(rgLamApp, o);
            BitConverter.GetBytes(rawLen[i]).CopyTo(rgLamApp, o + 4);
            BitConverter.GetBytes(compLen[i]).CopyTo(rgLamApp, o + 8);
            BitConverter.GetBytes(compOff[i]).CopyTo(rgLamApp, o + 12);
            BitConverter.GetBytes(0u).CopyTo(rgLamApp, o + 16);
        }

        int no = 20 + count * 20;
        for (int i = 0; i < count; i++)
        {
            Array.Copy(nameBytes[i], 0, rgLamApp, no, nameBytes[i].Length);
            no += nameBytes[i].Length;
        }

        int doff = 20 + count * 20 + (int)nameAreaLen;
        for (int i = 0; i < count; i++)
            Array.Copy(rgBlocks[i], 0, rgLamApp, doff + (int)compOff[i], compLen[i]);

        Console.WriteLine($"  .lamapp: {count} entry(s), {cbOrigTotal} -> {rgLamApp.Length} bytes (Lamarr)");
    }

    private int GetOrigRtcMajor(long[] rgRel, long[] rgSz, long[] rgCsz)
    {
        if (rgIdxRtc < 0) return 0;
        long origAbs = iBundleDataStart + rgRel[rgIdxRtc];
        int len = (int)(rgCsz[rgIdxRtc] > 0 ? rgCsz[rgIdxRtc] : rgSz[rgIdxRtc]);
        if (origAbs < 0 || len <= 0 || origAbs + len > rgPayload.Length) return 0;
        return ParseMajorFromRtc(Encoding.UTF8.GetString(rgPayload, (int)origAbs, len));
    }

    //重建bundle数据区与头 只保留boot与配置文件 依赖已移入.lamapp
    private void BuildBundleDataAndHeader(uint major, string sBundleId, List<int> keepIdx,
        long[] rgRel, long[] rgSz, long[] rgCsz, byte[] rgType, string[] rgName,
        long lDepsSz, long lRtcSz, long lRtcHash)
    {
        int m = keepIdx.Count;
        rgBundleOffsets = new long[m];
        rgBundleCsz = new long[m];
        rgBundleSz = new long[m];

        using var ms = new MemoryStream();
        long lDepsSzNew = lDepsSz;
        for (int k = 0; k < m; k++)
        {
            int i = keepIdx[k];
            byte[] rgData;
            if (i == iMainEntry)
                rgData = rgBoot;//原DLL已进.lamapp bundle这里放boot
            else
            {
                long ondisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
                rgData = new byte[ondisk];
                Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)ondisk);
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
        rgEntryCount = m;
        lDepsSz = lDepsSzNew;

        iNewRtcIdx = -1;
        for (int k = 0; k < m; k++)
        {
            if (rgName[keepIdx[k]].EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            { iNewRtcIdx = k; break; }
        }

        using var hd = new MemoryStream();
        WriteU32(hd, major);
        WriteU32(hd, 0);
        WriteI32(hd, m);
        WriteStr(hd, sBundleId);

        if (major >= 2)
        {
            int kDeps = -1;
            for (int k = 0; k < m; k++)
            {
                if (rgName[keepIdx[k]].EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                { kDeps = k; break; }
            }
            WriteI64(hd, kDeps >= 0 ? lBundleStart + rgBundleOffsets[kDeps] : 0);
            WriteI64(hd, lDepsSz);
            WriteI64(hd, iNewRtcIdx >= 0 ? lBundleStart + rgBundleOffsets[iNewRtcIdx] : 0);
            WriteI64(hd, lRtcSz);
            WriteI64(hd, lRtcHash);
        }

        for (int k = 0; k < m; k++)
        {
            WriteI64(hd, lBundleStart + rgBundleOffsets[k]);
            WriteI64(hd, rgBundleSz[k]);
            if (major >= 6)
                WriteI64(hd, rgBundleCsz[k]);
            WriteU8(hd, rgType[keepIdx[k]]);
            WriteStr(hd, rgName[keepIdx[k]]);
        }
        rgNewHeader = hd.ToArray();
    }

    //从deps.json剥离已移入.lamapp的依赖和无runtime资产的构建工具 防hostpolicy预检报缺失
    private byte[] StripDepsDependencies(byte[] rgDeps)
    {
        using var doc = JsonDocument.Parse(rgDeps);
        var root = doc.RootElement;
        var rgStripAll = new HashSet<string>(rgStripDeps, StringComparer.OrdinalIgnoreCase);

        //收集无runtime资产的条目如纯构建/分析期依赖 运行时不需要
        if (root.TryGetProperty("targets", out var rgTargets))
            foreach (var tfm in rgTargets.EnumerateObject())
                foreach (var pkg in tfm.Value.EnumerateObject())
                {
                    bool bHasRuntime = false;
                    foreach (var p in pkg.Value.EnumerateObject())
                        if (p.Name == "runtime" || p.Name == "runtimeTargets") { bHasRuntime = true; break; }
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
                            foreach (var p in pkg.Value.EnumerateObject())
                            {
                                if (p.Name == "dependencies")
                                {
                                    w.WritePropertyName("dependencies");
                                    w.WriteStartObject();
                                    foreach (var d in p.Value.EnumerateObject())
                                        if (!rgStripAll.Contains(d.Name))
                                        {
                                            w.WritePropertyName(d.Name);
                                            d.Value.WriteTo(w);
                                        }
                                    w.WriteEndObject();
                                }
                                else
                                {
                                    w.WritePropertyName(p.Name);
                                    p.Value.WriteTo(w);
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

    //判定条目是否为托管dll(pe且含clr头) 压缩条目视为非托管保留bundle
    private bool IsManagedDll(int i, long[] rgRel, long[] rgSz, long[] rgCsz)
    {
        if (rgCsz[i] > 0) return false;
        long abs = iBundleDataStart + rgRel[i];
        if (abs < 0 || abs + rgSz[i] > rgPayload.Length || rgSz[i] < 0x40)
            return false;
        int pe = BitConverter.ToInt32(rgPayload, (int)abs + 0x3C);
        if (pe + 0x18 > rgSz[i] || BitConverter.ToUInt32(rgPayload, (int)abs + pe) != 0x4550)
            return false;
        ushort magic = BitConverter.ToUInt16(rgPayload, (int)abs + pe + 24);
        int ddOff = magic == 0x20B ? 112 : 96;//标准字段长度PE32+/PE32
        long clr = abs + pe + 24 + ddOff + 14 * 8;
        if (clr + 8 > abs + rgSz[i])
            return false;
        uint uRva = BitConverter.ToUInt32(rgPayload, (int)clr);
        uint uSz = BitConverter.ToUInt32(rgPayload, (int)clr + 4);
        return uRva != 0 && uSz != 0;
    }

    private void PatchStubVars(string sOutPath)
    {
        int iPrefMajor = GetPayloadMajor();

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

        //自检确认stub模板占位符已全部替换 防打包器回归
        if (IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##APPNAME##")) >= 0 ||
            IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##PREFMAJ##")) >= 0)
            throw new InvalidOperationException("stub template markers were not fully replaced");
    }

    private static int ParseMajorFromRtc(string sRtc)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sRtc, "\"tfm\"\\s*:\\s*\"net(\\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int maj) && maj > 0)
            return maj;
        var m2 = System.Text.RegularExpressions.Regex.Match(sRtc, "\"version\"\\s*:\\s*\"(\\d+)\\.(\\d+)");//tfm字段有时缺失
        return m2.Success && int.TryParse(m2.Groups[1].Value, out int v2) && v2 > 0 ? v2 : 0;
    }

    private int GetPayloadMajor()
    {
        if (iNewRtcIdx < 0) return 0;
        int off = (int)rgBundleOffsets[iNewRtcIdx];
        int len = (int)rgBundleSz[iNewRtcIdx];
        if (off < 0 || len <= 0 || off + len > rgBundleData.Length) return 0;
        return ParseMajorFromRtc(Encoding.UTF8.GetString(rgBundleData, off, len));
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
            bool ok = true;
            for (int j = 0; j < rgPat.Length; j++)
                if (b[i + j] != rgPat[j]) { ok = false; break; }
            if (ok) return i;
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
        //stub无导入表/重定位表 入口自行解析
        Array.Clear(rgHdrs, iOptOff + 0x70, Math.Min(16 * 8, rgHdrs.Length - (iOptOff + 0x70)));
        BitConverter.GetBytes(uStubRawSize).CopyTo(rgHdrs, iOptOff + 4);
        BitConverter.GetBytes(uLamAppRawSize).CopyTo(rgHdrs, iOptOff + 8);

        Array.Clear(rgHdrs, iSecOff, Math.Min(usSecCount * 40, rgHdrs.Length - iSecOff));
        WriteSection(rgHdrs, iSecOff, ".stub", uStubRva, (uint)rgStubCode.Length, uStubRawSize, uStubRaw);
        WriteSection(rgHdrs, iSecOff + 40, ".lamapp", uLamAppRva, (uint)rgLamApp.Length, uLamAppRawSize, uLamAppRaw);

        using var fs = new FileStream(sOutPath, FileMode.Create);
        fs.Write(rgHdrs, 0, rgHdrs.Length);
        Pad(fs, (int)(uStubRaw - uNewHdrs));
        fs.Write(rgStubCode, 0, rgStubCode.Length);
        Pad(fs, (int)(uStubRawSize - rgStubCode.Length));
        byte[] rgMarker = new byte[40];
        BitConverter.GetBytes(lNewBundleHeaderOffset).CopyTo(rgMarker, 0);
        Array.Copy(rgSignature, 0, rgMarker, 8, 32);
        fs.Write(rgMarker, 0, 40);
        Pad(fs, (int)(uLamAppRaw - (iMarkerRaw + 40)));
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
        uint uChar = sName == ".stub" ? 0xE0000020u : 0x40000040u;//R|W|X + CNT_CODE : R|X + CNT_INITIALIZED_DATA
        BitConverter.GetBytes(uChar).CopyTo(rgHdrs, iOff + 36);
    }

    #endregion
    #region 辅助

    private static uint AlignUp(uint v, uint a) => a == 0 ? v : (v + a - 1) & ~(a - 1);
    private static void Pad(FileStream fs, int n) { while (n-- > 0) fs.WriteByte(0); }

    private static void WriteU32(Stream s, uint v) { s.Write(BitConverter.GetBytes(v), 0, 4); }
    private static void WriteI32(Stream s, int v) { s.Write(BitConverter.GetBytes(v), 0, 4); }
    private static void WriteI64(Stream s, long v) { s.Write(BitConverter.GetBytes(v), 0, 8); }
    private static void WriteU8(Stream s, byte v) { s.WriteByte(v); }
    private static void WriteStr(Stream s, string v)
    {
        byte[] b = Encoding.UTF8.GetBytes(v);
        if (b.Length < 0x80) s.WriteByte((byte)b.Length);
        else { s.WriteByte((byte)(0x80 | (b.Length >> 8))); s.WriteByte((byte)b.Length); }
        s.Write(b, 0, b.Length);
    }

    private static uint ReadU32(byte[] b, ref int p) { uint v = BitConverter.ToUInt32(b, p); p += 4; return v; }
    private static int ReadI32(byte[] b, ref int p) { int v = BitConverter.ToInt32(b, p); p += 4; return v; }
    private static long ReadI64(byte[] b, ref int p) { long v = BitConverter.ToInt64(b, p); p += 8; return v; }
    private static byte ReadU8(byte[] b, ref int p) { return b[p++]; }
    private static string ReadStr(byte[] b, ref int p)
    {
        int len = b[p++];
        if ((len & 0x80) != 0) len = ((len & 0x7F) << 8) | b[p++];
        string s = System.Text.Encoding.UTF8.GetString(b, p, len);
        p += len;
        return s;
    }

    #endregion
}