using Lamarr;

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
    private int iBundleLen;

    private byte[] rgApphostNorm = null!;
    private byte[] rgCompressed = null!;
    private uint uCompressedSize;
    private long lNewBundleHeaderOffset;
    private uint uStubRaw, uStubRawSize;
    private long iMarkerRaw;
    private uint uLzRaw;
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

        uStubEntryOff = 0;
        for (int i = rgStubCode.Length - 4; i >= 0; i--)
        {
            if (rgStubCode[i] == 0x55 && rgStubCode[i + 1] == 0x48 &&
                rgStubCode[i + 2] == 0x8B && rgStubCode[i + 3] == 0xEC)
            {
                uStubEntryOff = (uint)i;
                break;
            }
        }
        if (uStubEntryOff == 0)
            throw new InvalidOperationException("StubEntry not found in stub DLL");
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
        iBundleLen = rgPayload.Length - iBundleDataStart;
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
        rgApphostNorm = NormalizeApphost();
        uint uLen = (uint)rgApphostNorm.Length;

        RebuildBundle();

        //marker偏移影响压缩结果 需先占位估算再定布局
        byte[] rgEst = (byte[])rgApphostNorm.Clone();
        PatchMarker(rgEst, 0);
        uint uCap = LamarrEncoder.GetMaxEncodedSize(uLen);
        byte[] rgBuf = new byte[uCap];
        uint uEst = uCap;
        if (LamarrEncoder.Encode(rgBuf, ref uEst, rgEst, uLen) != 0)
            throw new InvalidOperationException("Compression failed (estimate)");

        //预留128字节吸收两次压缩之间的尺寸波动
        ComputeLayout(uEst + 128);
        ApplyBundleOffsets();

        PatchMarker(rgApphostNorm, lNewBundleHeaderOffset);
        rgCompressed = new byte[uCap];
        uint uOut = uCap;
        if (LamarrEncoder.Encode(rgCompressed, ref uOut, rgApphostNorm, uLen) != 0)
            throw new InvalidOperationException("Compression failed");
        uCompressedSize = uOut;
        if (uCompressedSize > uEst + 128)
            throw new InvalidOperationException("Compression size grew beyond slack");

        WriteFile(sOutPath);
        Console.WriteLine($"  Done: {sOutPath} ({new FileInfo(sOutPath).Length} bytes)");
    }

    private void ComputeLayout(uint uCompSizeForLayout)
    {
        //[head][stub][marker][lamarr][lamapp][bundle data][bundle head]
        //各段按FileAlignment对齐bundle 数据区起点决定新X'
        uStubRaw = AlignUp(uSizeOfHdrs, uFileAlign);
        uStubRawSize = AlignUp((uint)rgStubCode.Length, uFileAlign);
        iMarkerRaw = uStubRaw + uStubRawSize;
        uLzRaw = AlignUp((uint)(iMarkerRaw + 40), uFileAlign);
        uint uLzRawPadded = AlignUp(uCompSizeForLayout, uFileAlign);
        uLamAppRaw = AlignUp(uLzRaw + uLzRawPadded, uFileAlign);
        uLamAppRawSize = AlignUp((uint)rgLamApp.Length, uFileAlign);
        uint uBundleRaw = AlignUp(uLamAppRaw + uLamAppRawSize + 16, uFileAlign);
        lBundleStart = uBundleRaw;
        lNewBundleHeaderOffset = lBundleStart + lBundleDataLen;
    }

    private long FindMarkerOffset(byte[] b)
    {
        //marker可能远在1mb之外 不设上限
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
        for (int i = 0; i < n && i < 0x1000; i++)//0x1000防恶意头导致死循环
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

    //bundle头部按未压缩大小读取文件数据 普通FDD的coreclr无解压能力
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

        //deps/rtc头按未压缩大小读 必须保持原始不压缩
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
            rgLamApp = new byte[8 + pcb];
            BitConverter.GetBytes((uint)rgDll.Length).CopyTo(rgLamApp, 0);
            BitConverter.GetBytes(pcb).CopyTo(rgLamApp, 4);
            Array.Copy(rgComp, 0, rgLamApp, 8, (int)pcb);
            Console.WriteLine($"  .lamapp: {rgDll.Length} -> {rgLamApp.Length} bytes (Lamarr)");
        }

        //保持bundle文件不压缩 普通FDD coreclr对compressedSize>0直接失败
        using var ms = new MemoryStream();
        rgBundleOffsets = new long[n];
        rgBundleCsz = new long[n];
        rgBundleSz = new long[n];
        for (int i = 0; i < n && i < 0x1000; i++)
        {
            byte[] rgData;
            if (i == iMainEntry)
            {
                rgData = rgBoot;//主条目替换为bootstrapper 原DLL已放入.lamapp段
            }
            else
            {
                long ondisk = rgCsz[i] > 0 ? rgCsz[i] : rgSz[i];
                rgData = new byte[ondisk];
                Array.Copy(rgPayload, (int)(iBundleDataStart + rgRel[i]), rgData, 0, (int)ondisk);
            }
            rgBundleOffsets[i] = ms.Position;
            ms.Write(rgData, 0, rgData.Length);
            rgBundleCsz[i] = i == iMainEntry ? 0 : (rgCsz[i] > 0 ? rgData.Length : 0);
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

    #endregion
    #region apphost 规范化

    private byte[] NormalizeApphost()
    {
        //raw == RVA 且stub解压后直接映射执行 无需处理节偏移
        uint uNewHdrs = AlignUp(uSizeOfHdrs, uSectAlign);
        uint firstRva = BitConverter.ToUInt32(rgPayload, iSecOff + 12);
        if (uNewHdrs > firstRva) uNewHdrs = firstRva;

        uint uNewSizeOfImage = BitConverter.ToUInt32(rgPayload, iOptOff + 56);
        for (int i = 0; i < usSecCount; i++)
        {
            int o = iSecOff + i * 40;
            uint rva = BitConverter.ToUInt32(rgPayload, o + 12);
            uint vs = BitConverter.ToUInt32(rgPayload, o + 8);
            uint end = AlignUp(rva + vs, uSectAlign);
            if (end > uNewSizeOfImage) uNewSizeOfImage = end;
        }

        byte[] norm = new byte[uNewSizeOfImage];
        Array.Copy(rgPayload, 0, norm, 0, Math.Min(uSizeOfHdrs, norm.Length));

        for (int i = 0; i < usSecCount; i++)
        {
            int o = iSecOff + i * 40;
            uint rva = BitConverter.ToUInt32(rgPayload, o + 12);
            uint vs = BitConverter.ToUInt32(rgPayload, o + 8);
            uint raw = BitConverter.ToUInt32(rgPayload, o + 20);
            uint rawSize = BitConverter.ToUInt32(rgPayload, o + 16);
            uint copy = Math.Min(vs, rawSize);
            if (raw + copy <= rgPayload.Length && rva + copy <= norm.Length)
                Array.Copy(rgPayload, (int)raw, norm, (int)rva, (int)copy);
            BitConverter.GetBytes(rva).CopyTo(norm, o + 20);
            BitConverter.GetBytes(AlignUp(vs, uSectAlign)).CopyTo(norm, o + 16);
        }

        BitConverter.GetBytes(uNewSizeOfImage).CopyTo(norm, iOptOff + 56);
        BitConverter.GetBytes(uNewHdrs).CopyTo(norm, iOptOff + 60);
        //FileAlignment = SectionAlignment 映射时无需额外对齐处理
        BitConverter.GetBytes(uSectAlign).CopyTo(norm, iOptOff + 36);
        return norm;
    }

    private void PatchMarker(byte[] b, long offset)
    {
        for (int i = 0; i + 40 <= b.Length; i++)
            if (b[i + 8] == rgSignature[0] && MatchSig(b, i + 8))
            {
                BitConverter.GetBytes(offset).CopyTo(b, i);
                return;
            }
        throw new InvalidOperationException("Marker not found in apphost");
    }

    #endregion
    #region 输出

    private void WriteFile(string sOutPath)
    {
        uint uNewHdrs = AlignUp(uSizeOfHdrs, uFileAlign);
        uint uLzRawSize = AlignUp(uCompressedSize, uFileAlign);
        if (lBundleStart < uLzRaw + uLzRawSize)
            throw new InvalidOperationException("Bundle overlaps .lamarr (layout bug)");

        //节RVA按SectionAlignment对齐 stub在运行时据此映射各段
        uint uStubRva = AlignUp(0x1000, uSectAlign);
        uint uLzRva = AlignUp(uStubRva + (uint)rgStubCode.Length, uSectAlign);
        uint uLamAppRva = AlignUp(uLzRva + uCompressedSize, uSectAlign);
        uint uNewImg = AlignUp(Math.Max(uLamAppRva + (uint)rgLamApp.Length, (uint)rgApphostNorm.Length), uSectAlign);//SizeOfImage需覆盖解压后的apphost镜像 stub按它分配内存

        byte[] rgHdrs = new byte[uNewHdrs];
        Array.Copy(rgPayload, 0, rgHdrs, 0, Math.Min(uSizeOfHdrs, rgHdrs.Length));

        BitConverter.GetBytes((ushort)3).CopyTo(rgHdrs, iPeOff + 6);
        BitConverter.GetBytes(uNewImg).CopyTo(rgHdrs, iOptOff + 56);
        BitConverter.GetBytes(uStubRva + uStubEntryOff).CopyTo(rgHdrs, iOptOff + 16);
        Array.Clear(rgHdrs, iOptOff + 0x70, Math.Min(16 * 8, rgHdrs.Length - (iOptOff + 0x70)));//stub不依赖导入表和重定位表 清零后由入口代码自行解析

        Array.Clear(rgHdrs, iSecOff, Math.Min(usSecCount * 40, rgHdrs.Length - iSecOff));
        WriteSection(rgHdrs, iSecOff, ".stub", uStubRva, (uint)rgStubCode.Length, uStubRawSize, uStubRaw);
        WriteSection(rgHdrs, iSecOff + 40, ".lamarr", uLzRva, uCompressedSize, uLzRawSize, uLzRaw);
        WriteSection(rgHdrs, iSecOff + 80, ".lamapp", uLamAppRva, (uint)rgLamApp.Length, uLamAppRawSize, uLamAppRaw);

        using var fs = new FileStream(sOutPath, FileMode.Create);
        fs.Write(rgHdrs, 0, rgHdrs.Length);
        Pad(fs, (int)(uStubRaw - uNewHdrs));
        fs.Write(rgStubCode, 0, rgStubCode.Length);
        Pad(fs, (int)(uStubRawSize - rgStubCode.Length));
        byte[] rgMarker = new byte[40];
        BitConverter.GetBytes(lNewBundleHeaderOffset).CopyTo(rgMarker, 0);
        Array.Copy(rgSignature, 0, rgMarker, 8, 32);
        fs.Write(rgMarker, 0, 40);
        Pad(fs, (int)(uLzRaw - (iMarkerRaw + 40)));
        fs.Write(rgCompressed, 0, (int)uCompressedSize);
        Pad(fs, (int)(uLzRawSize - uCompressedSize));
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
        uint uChar = sName == ".stub" ? 0xE0000020u : 0x40000040u;//stub: code|exec|read|write (VEH state) : data|read
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