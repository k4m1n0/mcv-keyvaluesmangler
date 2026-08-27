using Lamarr;
using System.Text;

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
    private long lMarkerRaw;
    private long lBundleStart;

    private byte[] rgBundleData = null!;
    private byte[] rgNewHeader = null!;
    private long lBundleDataLen;
    private long[] rgBundleOffsets = null!;
    private long[] rgBundleCsz = null!;
    private long[] rgBundleSz = null!;
    private int[] rgEntryOffPos = null!;
    private int iHeaderDepsPos = -1, iHeaderRtcPos = -1;
    private int iIdxDeps = -1, iIdxRtc = -1;
    private long lOrigDepsLoc, lOrigRtcLoc;
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

    //8字节header_off + 32字节签名
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
        uFileAlign = BitConverter.ToUInt32(rgPayload, iOptOff + 36);
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
        ApplyBundleOffsets();
        PatchStubVars(sOutPath);

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
    }

    private void ComputeLayout()
    {
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        lMarkerRaw = uStubRaw + uStubRawSize;
        uLamAppRaw = AlignUp((uint)(lMarkerRaw + 40), uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);
        lBundleStart = uBundleRaw;
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }

    private long FindMarkerOffset(byte[] rgB)
    {
        //不设搜索上限 marker位置随bundle大小变化
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
        for (int i = 0; i < iN && i < 0x1000; i++)//防坏头
        {
            long lOff = ReadI64(rgB, ref iPos);
            ReadI64(rgB, ref iPos); ReadI64(rgB, ref iPos);
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
            rgCsz[i] = ReadI64(rgPayload, ref iPos);
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

        //主dll与托管依赖各自压缩 全部进.lamapp多条目容器 bundle只留Boot+deps+rtc
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
            AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, rgNames[iIdxDeps],
                ReadEntryBytes(rgRel[iIdxDeps], rgCsz[iIdxDeps], rgSz[iIdxDeps]), rgSz[iIdxDeps], rgCsz[iIdxDeps], rgType[iIdxDeps]);
        if (iIdxRtc >= 0)
            AddBundleEntry(ms, rgNewNames, rgNewSz, rgNewCsz, rgNewType, rgNames[iIdxRtc],
                ReadEntryBytes(rgRel[iIdxRtc], rgCsz[iIdxRtc], rgSz[iIdxRtc]), rgSz[iIdxRtc], rgCsz[iIdxRtc], rgType[iIdxRtc]);
        rgBundleData = ms.ToArray();
        lBundleDataLen = rgBundleData.Length;

        BuildNewBundleHeader(uMajor, iHbase, rgNewNames, rgNewSz, rgNewCsz, rgNewType);
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

    //magic "Lamarr!!" + count + totalOrig/totalComp + entry table(20B nameLen/rawLen/compLen/compOff) + names + 压缩块
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
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgData[i].Length);
            byte[] rgComp = new byte[cbCap];
            uint pcb = cbCap;
            if (LamarrEncoder.Encode(rgComp, ref pcb, rgData[i], (uint)rgData[i].Length) != 0)
                throw new InvalidOperationException("Lamarr encode failed (lamapp entry)");
            rgCompOff[i] = ms.Position;
            rgCompLen[i] = pcb;
            ms.Write(rgComp, 0, (int)pcb);
            lTotalOrig += rgData[i].Length;
            lTotalComp += pcb;
        }
        byte[] rgBuf = ms.GetBuffer();
        for (int i = 0; i < rgNames.Count; i++)
        {
            int o = (int)(lTable + i * 20);
            BitConverter.GetBytes(rgNameLen[i]).CopyTo(rgBuf, o);
            BitConverter.GetBytes((uint)rgData[i].Length).CopyTo(rgBuf, o + 4);
            BitConverter.GetBytes((uint)rgCompLen[i]).CopyTo(rgBuf, o + 8);
            BitConverter.GetBytes((uint)rgCompOff[i]).CopyTo(rgBuf, o + 12);
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
            ms.Position += 8 + 8 + 8 + 1;
            WriteBundleStr(ms, rgName);
            if (rgNames[i].Contains("deps.json")) iNewDeps = i;
            if (rgNames[i].Contains("runtimeconfig")) iNewRtc = i;
        }
        rgNewHeader = ms.ToArray();
    }

    private static void WriteBundleStr(MemoryStream ms, byte[] rgB)
    {
        if (rgB.Length < 0x80) ms.WriteByte((byte)rgB.Length);
        else { ms.WriteByte((byte)(0x80 | (rgB.Length >> 8))); ms.WriteByte((byte)(rgB.Length & 0xFF)); }
        ms.Write(rgB, 0, rgB.Length);
    }

    private void ApplyBundleOffsets()
    {
        if (iHeaderDepsPos >= 0 && iNewDeps >= 0)
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[iNewDeps]).CopyTo(rgNewHeader, iHeaderDepsPos);
        if (iHeaderRtcPos >= 0 && iNewRtc >= 0)
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[iNewRtc]).CopyTo(rgNewHeader, iHeaderRtcPos);
        for (int i = 0; i < iEntryCount && i < 0x1000; i++)
        {
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[i]).CopyTo(rgNewHeader, rgEntryOffPos[i]);
            BitConverter.GetBytes(rgNewSzVals[i]).CopyTo(rgNewHeader, rgEntryOffPos[i] + 8);
            BitConverter.GetBytes(rgNewCszVals[i]).CopyTo(rgNewHeader, rgEntryOffPos[i] + 16);
        }
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
    }

    private int GetPayloadMajor()
    {
        if (iNewRtc < 0) return 0;
        int iOff = (int)rgBundleOffsets[iNewRtc];
        int iLen = (int)rgNewSzVals[iNewRtc];
        if (iOff < 0 || iLen <= 0 || iOff + iLen > rgBundleData.Length) return 0;
        string sRtc = Encoding.UTF8.GetString(rgBundleData, iOff, iLen);
        var iM = System.Text.RegularExpressions.Regex.Match(sRtc, "\"tfm\"\\s*:\\s*\"net(\\d+)");
        if (iM.Success && int.TryParse(iM.Groups[1].Value, out int iMaj) && iMaj > 0)
            return iMaj;
        var m2 = System.Text.RegularExpressions.Regex.Match(sRtc, "\"version\"\\s*:\\s*\"(\\d+)\\.(\\d+)");//tfm字段有时缺失
        return m2.Success && int.TryParse(m2.Groups[1].Value, out int iV2) && iV2 > 0 ? iV2 : 0;
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
        uint uNewImg = AlignUp(uLamAppRva + (uint)rgLamApp.Length, uSectAlign);

        byte[] rgHdrs = new byte[uNewHdrs];
        Array.Copy(rgPayload, 0, rgHdrs, 0, Math.Min(uSizeOfHdrs, rgHdrs.Length));

        BitConverter.GetBytes((ushort)2).CopyTo(rgHdrs, iPeOff + 6);
        BitConverter.GetBytes(uNewImg).CopyTo(rgHdrs, iOptOff + 56);
        BitConverter.GetBytes(uStubRva + uStubEntryOff).CopyTo(rgHdrs, iOptOff + 16);
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
        uint uChar = sName == ".stub" ? 0xE0000020u : 0x40000040u;//R|W|X + CNT_CODE : R|X + CNT_INITIALIZED_DATA
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