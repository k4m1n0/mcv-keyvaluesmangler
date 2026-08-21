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
    private long iMarkerRaw;
    private long lBundleStart;

    private byte[] rgBundleData = null!;
    private byte[] rgNewHeader = null!;
    private long lBundleDataLen;
    private long[] rgBundleOffsets = null!;
    private long[] rgBundleCsz = null!;
    private long[] rgBundleSz = null!;
    private int[] rgEntryOffPos = null!;
    private int iHeaderDepsPos = -1, iHeaderRtcPos = -1;
    private int rgIdxDeps = -1, rgIdxRtc = -1;
    private long lOrigDepsLoc, lOrigRtcLoc;
    private int rgEntryCount;

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
        iMarkerRaw = uStubRaw + uStubRawSize;
        uLamAppRaw = AlignUp((uint)(iMarkerRaw + 40), uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);
        lBundleStart = uBundleRaw;
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
            ReadI64(b, ref p); ReadI64(b, ref p);
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
        ReadStr(rgPayload, ref p);

        int hbase = (int)lBundleHeaderOffset;
        iHeaderDepsPos = -1; iHeaderRtcPos = -1; rgIdxDeps = -1; rgIdxRtc = -1;
        if (major >= 2)
        {
            iHeaderDepsPos = p - hbase; lOrigDepsLoc = ReadI64(rgPayload, ref p);
            ReadI64(rgPayload, ref p);
            iHeaderRtcPos = p - hbase; lOrigRtcLoc = ReadI64(rgPayload, ref p);
            ReadI64(rgPayload, ref p);
            ReadI64(rgPayload, ref p);
        }

        rgEntryCount = n;
        rgEntryOffPos = new int[n];
        var rgRel = new long[n];
        var rgSz = new long[n];
        var rgCsz = new long[n];
        iMainEntry = -1;
        for (int i = 0; i < n && i < 0x1000; i++)
        {
            rgEntryOffPos[i] = p - hbase;
            rgRel[i] = ReadI64(rgPayload, ref p) - iBundleDataStart;
            rgSz[i] = ReadI64(rgPayload, ref p);
            rgCsz[i] = ReadI64(rgPayload, ref p);
            ReadU8(rgPayload, ref p);
            string sName = ReadStr(rgPayload, ref p);
            if (iMainEntry < 0 && sName.Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                iMainEntry = i;
        }
        if (iMainEntry < 0)
            throw new InvalidOperationException($"Bundle main assembly '{sMainName}' not found. Input: '{sPayloadPath}'");

        for (int i = 0; i < n && i < 0x1000; i++)
        {
            long origAbs = iBundleDataStart + rgRel[i];
            if (rgIdxDeps < 0 && major >= 2 && origAbs == lOrigDepsLoc) rgIdxDeps = i;
            if (rgIdxRtc < 0 && major >= 2 && origAbs == lOrigRtcLoc) rgIdxRtc = i;
        }

        {
            int m = iMainEntry;
            long ondisk = rgCsz[m] > 0 ? rgCsz[m] : rgSz[m];
            byte[] rgDll = new byte[ondisk];
            Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[m]), rgDll, 0, (int)ondisk);
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgDll.Length);
            byte[] rgComp = new byte[cbCap];
            uint pcb = cbCap;
            if (LamarrEncoder.Encode(rgComp, ref pcb, rgDll, (uint)rgDll.Length) != 0)
                throw new InvalidOperationException("Lamarr encode failed (main DLL)");
            byte[] rgMarkerPad = { 0x42, 0x53, 0x90, 0x00, 0x4A, 0x42, 0x06, 0x00 };
            rgLamApp = new byte[8 + rgMarkerPad.Length + pcb];
            BitConverter.GetBytes((uint)rgDll.Length).CopyTo(rgLamApp, 0);
            BitConverter.GetBytes(pcb).CopyTo(rgLamApp, 4);
            Array.Copy(rgMarkerPad, 0, rgLamApp, 8, rgMarkerPad.Length);
            Array.Copy(rgComp, 0, rgLamApp, 8 + rgMarkerPad.Length, (int)pcb);
            Console.WriteLine($"  .lamapp: {rgDll.Length} -> {rgLamApp.Length} bytes (Lamarr)");
        }

        using var ms = new MemoryStream();
        rgBundleOffsets = new long[n];
        rgBundleCsz = new long[n];
        rgBundleSz = new long[n];
        for (int i = 0; i < n && i < 0x1000; i++)
        {
            byte[] rgData;
            if (i == iMainEntry)
            {
                rgData = rgBoot;//原DLL已经进.lamapp了 这里放boot
            }
            else
            {
                long ondisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
                rgData = new byte[ondisk];
                Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)ondisk);
            }
            rgBundleOffsets[i] = ms.Position;
            ms.Write(rgData, 0, rgData.Length);
            rgBundleCsz[i] = i == iMainEntry ? 0 : (rgCsz[i] > 0 ? rgData.Length : 0);//fdd的coreclr不会解压 csz直接置0
            rgBundleSz[i] = i == iMainEntry ? rgData.Length : rgSz[i];
        }
        rgBundleData = ms.ToArray();
        lBundleDataLen = rgBundleData.Length;

        int hlen = rgPayload.Length - hbase;
        rgNewHeader = new byte[hlen];
        Array.Copy(rgPayload, hbase, rgNewHeader, 0, hlen);
    }

    private void ApplyBundleOffsets()
    {
        if (iHeaderDepsPos >= 0 && rgIdxDeps >= 0)
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[rgIdxDeps]).CopyTo(rgNewHeader, iHeaderDepsPos);
        if (iHeaderRtcPos >= 0 && rgIdxRtc >= 0)
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[rgIdxRtc]).CopyTo(rgNewHeader, iHeaderRtcPos);
        for (int i = 0; i < rgEntryCount && i < 0x1000; i++)
        {
            BitConverter.GetBytes(lBundleStart + rgBundleOffsets[i]).CopyTo(rgNewHeader, rgEntryOffPos[i]);
            BitConverter.GetBytes(rgBundleSz[i]).CopyTo(rgNewHeader, rgEntryOffPos[i] + 8);
            BitConverter.GetBytes(rgBundleCsz[i]).CopyTo(rgNewHeader, rgEntryOffPos[i] + 16);
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
        if (rgIdxRtc < 0) return 0;
        int off = (int)rgBundleOffsets[rgIdxRtc];
        int len = (int)rgBundleSz[rgIdxRtc];
        if (off < 0 || len <= 0 || off + len > rgBundleData.Length) return 0;
        string sRtc = Encoding.UTF8.GetString(rgBundleData, off, len);
        var m = System.Text.RegularExpressions.Regex.Match(sRtc, "\"tfm\"\\s*:\\s*\"net(\\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int maj) && maj > 0)
            return maj;
        var m2 = System.Text.RegularExpressions.Regex.Match(sRtc, "\"version\"\\s*:\\s*\"(\\d+)\\.(\\d+)");//tfm字段有时缺失
        return m2.Success && int.TryParse(m2.Groups[1].Value, out int v2) && v2 > 0 ? v2 : 0;
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