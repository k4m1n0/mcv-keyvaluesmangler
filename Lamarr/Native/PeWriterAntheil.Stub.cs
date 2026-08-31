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
    #region 配置改写

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
                            //分层只在显式off时写false 缺省保留默认
                            if (sTiered == "off") w.WriteBoolean("System.Runtime.TieredCompilation", false);
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

    //剔除依赖项
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

    #endregion
    #region stub变量填充

    private void PatchStubVars(string sOutPath)
    {
        int iPrefMajor = GetPayloadMajor();

        ScrambleBuffers(rgStubCode, new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks));

        ReplaceMarker(rgStubCode, "##APPNAME##", Encoding.Unicode.GetBytes(sMainName), 256);
        ReplaceMarker(rgStubCode, "##PREFMAJ##", BitConverter.GetBytes((uint)iPrefMajor), 8);
        ReplaceMarker(rgStubCode, "##KBSJB##", rgKBsjb, 32);

        int iOff = IndexOf(rgStubCode, new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 });
        if (iOff < 0)
            throw new InvalidOperationException("gHeaderOff marker not found in stub");
        Array.Copy(BitConverter.GetBytes(lNewBundleHeaderOffset), 0, rgStubCode, iOff, 8);
        //stub固定imm派生化 TEA delta/Murmur乘子
        uint uImmK = DeriveBranch(rgHead, 24, 16, 0x13579BDFu);
        ReplaceAllImm(rgStubCode, new byte[] { 0xB9, 0x79, 0x37, 0x9E }, 0x9E3779B9u ^ (uImmK & 0xFFFFu));
        ReplaceAllImm(rgStubCode, new byte[] { 0xF5, 0x79, 0x2B, 0x6D }, 0x6D2B79F5u ^ (uImmK >> 16));
        //等长等语义指纹随机化 stub全表 跳过数据区(##STRST##起)
        int iScrEnd = IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##STRST##"));
        ScrambleFingerprint(rgStubCode, new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks), false, 0, iScrEnd >= 0 ? iScrEnd : rgStubCode.Length);
        //API字符串区加密 与stub start解密一致
        int iSS = IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##STRST##"));
        int iSE = IndexOf(rgStubCode, Encoding.ASCII.GetBytes("##STREN##"));
        if (iSS >= 0 && iSE > iSS + 8)
        {
            uint uStrKey = DeriveBranch(rgHead, 40, 16, 0x2B7E1516u);
            int iKey = IndexOf(rgStubCode, new byte[] { 0xED, 0x5E, 0xDE, 0xC0, 0xED, 0x5E, 0xDE, 0xC0 });
            if (iKey >= 0)
            {
                //低4字节uStrKey 高4字节派生填充
                byte[] rgK8 = new byte[8];
                BitConverter.GetBytes(uStrKey).CopyTo(rgK8, 0);
                BitConverter.GetBytes(uStrKey ^ 0x9E3779B9u ^ (uImmK & 0xFFFFu)).CopyTo(rgK8, 4);
                Array.Copy(rgK8, 0, rgStubCode, iKey, 8);
            }
            uint uK = uStrKey;
            for (int i = iSS + 8; i < iSE; i++)
            {
                rgStubCode[i] ^= (byte)uK;
                uK = uK * 0x13u + 0x5Au;
            }
            Array.Clear(rgStubCode, iSS, 8);
            Array.Clear(rgStubCode, iSE, 8);
            Console.WriteLine($"  api_str: {iSE - iSS - 8} bytes encrypted");
        }
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

    //全0缓冲随机化 gFall/gBest必须0(efb标志) 填充从##STREN##标记9B后align8起
    private static void ScrambleBuffers(byte[] rgB, Random rng)
    {
        int iSE = IndexOf(rgB, Encoding.ASCII.GetBytes("##STREN##"));
        int iKB = IndexOf(rgB, Encoding.ASCII.GetBytes("##KBSJB##"));
        if (iSE < 0 || iKB < 0 || iKB <= iSE) return;
        int iStart = (iSE + 9 + 7) & ~7;//gDotnetRootW起(标记9B后align8)
        int iFall = iStart + 520 * 5 + 640;//gFindData尾
        int iEnd2 = iFall + 140 + 140;//gBest尾 = gPadBuf起
        int iCnt = 0;
        for (int i = iStart; i < iFall && i < rgB.Length; i++) { rgB[i] = (byte)rng.Next(256); iCnt++; }
        for (int i = iEnd2; i < iKB && i < rgB.Length; i++) { rgB[i] = (byte)rng.Next(256); iCnt++; }
        Console.WriteLine($"  buf_rnd: {iCnt} bytes randomized");
    }

    private static void ReplaceMarker(byte[] rgB, string sMarker, byte[] rgValue, int iSpace)
    {
        byte[] rgPat = Encoding.ASCII.GetBytes(sMarker);
        int i = IndexOf(rgB, rgPat);
        if (i < 0)
            throw new InvalidOperationException($"Stub marker '{sMarker}' not found");
        if (rgValue.Length > iSpace)
            throw new InvalidOperationException($"Stub value for '{sMarker}' too long ({rgValue.Length} > {iSpace})");
        Array.Clear(rgB, i, iSpace);
        Array.Copy(rgValue, 0, rgB, i, rgValue.Length);
    }

    private static void ReplaceAllImm(byte[] rgB, byte[] rgPat, uint uVal)
    {
        int i = 0;
        while (i + rgPat.Length <= rgB.Length)
        {
            bool ok = true;
            for (int j = 0; j < rgPat.Length; j++) if (rgB[i + j] != rgPat[j]) { ok = false; break; }
            if (ok) { Array.Copy(BitConverter.GetBytes(uVal), 0, rgB, i, 4); i += 4; }
            else i++;
        }
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
    #region 文件输出

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
    #region 流IO辅助

    private static uint AlignUp(uint uV, uint uA) => uA == 0 ? uV : (uV + uA - 1) & ~(uA - 1);
    private static readonly Random _rndPad = new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
    private static void Pad(FileStream fs, int iN) { byte[] rg = new byte[Math.Max(0, iN)]; _rndPad.NextBytes(rg); fs.Write(rg, 0, rg.Length); }

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
