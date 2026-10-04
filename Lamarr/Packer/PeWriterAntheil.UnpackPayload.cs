namespace Lamarr.Packer;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lamarr;

internal partial class PeWriterAntheil
{
    //载荷导出的逐层逆 离线动词--unpack entry0是主程序集 entry1起是内置托管依赖 两者在产物里都是密文
    //主件是位置XOR加折叠层加Poly1305加Lamarr压缩 主件与boot的方法体另有一层方法体加密 产出可反编译的dll与manifest并可选重建同载荷native产物
    internal static void ExportPayload(byte[] rgExe, byte[] rgCont, string sSeed, byte[] rgSeedKey, uint uKey,
        string sOutDir, string? sNativeOut, string? sStubPath, string? sBootPath, string sScrubJson)
    {
        //PeScrub的确定逆材料由escrow密封区带回 entries按条目名把载荷还原成打包输入的逐字节形态 每条都自证
        //再清一次痕必须逐字节回到解出来的那份 失败响亮告警 artifact是产物外层头区 交给native重建还原donor头
        var oHdr = ParseScrub(sScrubJson, out string sArtifactHex);
        byte[]? rgArtifactScrub = null;
        if (sArtifactHex.Length > 0)
        {
            try { rgArtifactScrub = Convert.FromHexString(sArtifactHex); }
            catch (Exception eHex)
            {
                Console.Error.WriteLine($"[payload] artifact scrub blob is not hex ({eHex.Message}); the outer header stays scrubbed");
            }
        }
        int iRestored = 0, iRestoreFail = 0;
        var rgDepNames = new List<string>();//native重建的条目序 主件加托管依赖按容器序 再加保留的非dll条目
        int iHead = 68 + ((int)((uKey >> 0) & 0x1Fu) << 2);
        int iDataBase = (iHead + 16 + ((int)((uKey >> 8) & 0x3Fu) << 2) + 7) & ~7;
        if (iHead + 16 > rgCont.Length) { Console.Error.WriteLine("[payload] container header out of range"); return; }
        int iCount = (int)(BitConverter.ToUInt32(rgCont, iHead) ^ uKey);
        uint uTbl = BitConverter.ToUInt32(rgCont, iHead + 8) ^ uKey;
        uint uName = BitConverter.ToUInt32(rgCont, iHead + 12) ^ uKey;
        if (iCount <= 0 || iCount > 4096 || uTbl + 4 > rgCont.Length || uName >= rgCont.Length)
        {
            Console.Error.WriteLine($"[payload] container header bad: count={iCount} tbl=0x{uTbl:X} name=0x{uName:X}");
            return;
        }
        int iTblN = (int)(BitConverter.ToUInt32(rgCont, (int)uTbl) ^ uKey);
        if (iTblN < iCount || (long)uTbl + 4 + (long)iTblN * 16 > rgCont.Length)
        {
            Console.Error.WriteLine($"[payload] entry table bad: total={iTblN} count={iCount}");
            return;
        }

        var rgEnt = new List<(string sName, uint uRawLen, uint uCompLen, uint uCompOff)>();//条目表 字段顺序与逐字段pad都按条目号派生
        for (int i = 0; i < iCount; i++)
        {
            int[] rgP = LamPermAt(uKey, i);
            var rgF = new uint[4];
            for (int s = 0; s < 4; s++)
                rgF[rgP[s]] = BitConverter.ToUInt32(rgCont, (int)uTbl + 4 + i * 16 + s * 4) ^ LamFieldKey(uKey, i, s);
            rgEnt.Add(("", rgF[0], rgF[1], rgF[2]));
        }
        uint uNm = uKey ^ 0x2B7E1516u;//名字块 长度前缀流被uNm链掩码
        int iNp = (int)uName;
        for (int i = 0; i < iCount; i++)
        {
            if (iNp >= rgCont.Length) break;
            int iLen = rgCont[iNp];
            uNm = (uNm * uPermMul) ^ uPermAdd;
            var sb = new StringBuilder(iLen);
            for (int j = 0; j < iLen && iNp + 1 + j < rgCont.Length; j++)
            {
                uNm = (uNm * uPermMul) + 0x1234567u;
                sb.Append((char)(rgCont[iNp + 1 + j] ^ (byte)(uNm >> 24)));
            }
            var e = rgEnt[i];
            rgEnt[i] = (sb.ToString(), e.uRawLen, e.uCompLen, e.uCompOff);
            iNp += 1 + iLen;
        }
        Console.WriteLine($"[payload] container: {iCount} entry(s) -> " +
            string.Join(", ", rgEnt.ConvertAll(e => $"{e.sName}:{e.uRawLen}/{e.uCompLen}@{e.uCompOff}")));

        string sDirPayload = Path.Combine(sOutDir, "payload");
        Directory.CreateDirectory(sDirPayload);
        byte[] rgHead = new byte[64];
        Array.Copy(rgCont, 0, rgHead, 0, Math.Min(64, rgCont.Length));
        var rgManifest = new List<object>();
        var e0 = rgEnt[0];
        int iAbs0 = iDataBase + (int)e0.uCompOff;
        if (e0.uCompLen == 0 || iAbs0 < 0 || e0.uCompLen > rgCont.Length - iAbs0)
        {
            Console.Error.WriteLine($"[payload] main entry out of range: off={iAbs0} len={e0.uCompLen}");
            return;
        }
        byte[] rgMerged = XorBytes(rgCont[iAbs0..(iAbs0 + (int)e0.uCompLen)], rgSeedKey, MixAdj(rgSeedKey));//entry0的位置XOR逆运算 与boot侧的还原同一算式 siteKey取SeedKey(seed) uKeyMix取MixAdj
        int iRegN = BitConverter.ToInt32(rgMerged, 0);
        if (BitConverter.ToUInt32(rgMerged, 4) != 0x80u || iRegN <= 0 || iRegN > 64 || 8 + iRegN * 12 + 4 > rgMerged.Length)
        {
            Console.Error.WriteLine($"[payload] merged main stream header bad: regions={iRegN}");
            return;
        }
        int iCompLen = BitConverter.ToInt32(rgMerged, 8 + iRegN * 12);
        int iCompAt = 8 + iRegN * 12 + 4;
        int iRegBase = iCompAt + iCompLen;
        if (iCompLen <= 16 || iRegBase > rgMerged.Length)
        {
            Console.Error.WriteLine($"[payload] merged main stream bad: comp={iCompLen} len={rgMerged.Length}");
            return;
        }
        byte[] rgComp0 = rgMerged[iCompAt..(iCompAt + iCompLen)];//合并主流自描述 区表加compLen加comp0加各区串联 直接切片即得comp0

        byte[]? RegionOf(int iType)
        {
            for (int r = 0; r < iRegN; r++)
            {
                int t = BitConverter.ToInt32(rgMerged, 8 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 0);
                if (t != iType) continue;
                int off = BitConverter.ToInt32(rgMerged, 12 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 1);
                int len = BitConverter.ToInt32(rgMerged, 16 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 2);
                if (len <= 0 || off < 0 || (long)iRegBase + off + len > rgMerged.Length) continue;
                byte[] rgR = rgMerged[(iRegBase + off)..(iRegBase + off + len)];
                if (RegionReversed(iType)) Array.Reverse(rgR);
                return rgR;
            }
            return null;
        }
        byte[]? rgSig = RegionOf(3);
        byte[]? rgRoll = RegionOf(7);
        byte[]? rgEngine = RegionOf(6);

        var rgRollCand = new List<(string sWhich, byte[] rg)>();//折叠层密钥是内核在roll头32B上的输出 产物里的roll可能还被链式加密 候选逐个用PolyTag自证
        if (rgRoll != null)
        {
            rgRollCand.Add(("plain", rgRoll));
            if (rgEngine != null)
            {
                uint uRollKey = MixAdj(SeedKey(Encoding.ASCII.GetBytes(sSeed))) ^ EngineTextSum(rgEngine);
                foreach (bool bBe in new[] { true, false })
                {
                    byte[] rgP = (byte[])rgRoll.Clone();
                    RollChainDecryptInPlace(rgP, uRollKey, bBe);
                    rgRollCand.Add(("chain:" + (bBe ? "be" : "le"), rgP));
                }
            }
        }
        byte[]? rgMainDll = null;
        string sFoldWin = "(none)";
        foreach (var (sWhich, rgRollC) in rgRollCand)
        {
            byte[] rgKeyB = VmSeedKeyOf(rgRollC);
            byte[] rgTry = XorBytes(rgComp0, rgKeyB, MixAdj(rgKeyB));
            byte[] rgEnc = rgTry[16..];
            if (!PolyTagMatches(rgSeedKey, rgEnc, rgTry)) continue;
            byte[] rgDec = new byte[Math.Max(1u, e0.uRawLen)];
            uint uPcb = (uint)rgDec.Length;
            int iRet = LamarrDecoder.Decode(rgDec, ref uPcb, rgEnc, (uint)rgEnc.Length);
            if (uPcb != e0.uRawLen || iRet != 0)
            {
                Console.WriteLine($"[payload] fold[{sWhich}]: poly ok but decode failed (ret={iRet} pcb={uPcb} want={e0.uRawLen})");
                continue;
            }
            rgMainDll = rgDec;
            sFoldWin = sWhich;
            break;
        }
        if (rgMainDll == null)
        {
            Console.Error.WriteLine("[payload] main assembly: no fold candidate verified (PolyTag/decode failed)");
            return;
        }
        Console.WriteLine($"[payload] main assembly: fold={sFoldWin} encoded={rgComp0.Length - 16} -> {rgMainDll.Length} bytes");

        bool bJit = rgSig != null;//方法体加密逆运算 主程序集恒加密 依赖用内容寻址自动判定
        ulong uJitKey = 0, uMaskKey = 0;
        if (bJit)
        {
            byte[] rgMat = SeedKey(Encoding.ASCII.GetBytes(sSeed));
            uJitKey = Fnv1aK1(rgMat);
            uMaskKey = MaskKeyOf(rgMat);
            Array.Clear(rgMat, 0, rgMat.Length);
        }
        var (iEnc0, iOk0, iFail0) = (0, 0, 0);
        if (bJit) (iEnc0, iOk0, iFail0) = MethodEncryptor.DecryptAll(rgMainDll, uJitKey, uMaskKey, rgSig!);
        string sMainName = e0.sName.Length > 0 ? e0.sName : "main.dll";
        string sRestoreMain = "n/a (no scrub material was supplied)";
        if (oHdr.Count > 0)
        {
            if (RestoreScrubByName(rgMainDll, sMainName, oHdr, out string sWhy)) { iRestored++; sRestoreMain = "ok"; }
            else { iRestoreFail++; sRestoreMain = "failed: " + sWhy; }
        }
        string sMainPath = Path.Combine(sDirPayload, sMainName);
        File.WriteAllBytes(sMainPath, rgMainDll);
        Console.WriteLine($"[payload] main {sMainName}: {rgMainDll.Length} bytes, method bodies encrypted={iEnc0} decrypted={iOk0} failed={iFail0}, PeScrub restore={sRestoreMain}");
        rgManifest.Add(new
        {
            kind = "main",
            name = sMainName,
            rawLen = e0.uRawLen,
            compLen = e0.uCompLen,
            mode = "merged+fold+poly+lamarr",
            fold = sFoldWin,
            hdrRestore = sRestoreMain,
            encMethods = iEnc0,
            decMethods = iOk0,
            failMethods = iFail0,
            sha256 = Sha256Hex(rgMainDll)
        });

        for (int i = 1; i < iCount; i++)//依赖条目 明文条目只有头部扰动 压缩条目是tag加Lamarr流
        {
            var e = rgEnt[i];
            int iAbs = iDataBase + (int)e.uCompOff;
            if (e.uCompLen == 0 || iAbs < 0 || e.uCompLen > rgCont.Length - iAbs) continue;
            byte[] rgBlk = rgCont[iAbs..(iAbs + (int)e.uCompLen)];
            byte[]? rgDep;
            string sMode;
            if (e.uRawLen == 0x7FFFFFFFu)
            {
                UnDisturbHead(rgBlk, rgHead);
                rgDep = rgBlk;
                sMode = "plain(disturbed-head)";
            }
            else if (e.uCompLen == e.uRawLen)
            {
                rgDep = XorBytes(rgBlk, rgSeedKey, MixAdj(rgSeedKey));//辅助件与诱饵条目 decoder与jithook与签名表与pheropod 整条位置XOR 无PolyTag
                sMode = "helper(pos-xor)";
            }
            else if (e.uCompLen > 16)
            {
                byte[] rgEnc = rgBlk[16..];
                rgDep = new byte[Math.Max(1u, e.uRawLen)];
                uint uPcb = (uint)rgDep.Length;
                int iRet = LamarrDecoder.Decode(rgDep, ref uPcb, rgEnc, (uint)rgEnc.Length);
                if (uPcb != e.uRawLen || iRet != 0)
                {
                    Console.WriteLine($"[payload] dep {e.sName}: decode failed (ret={iRet} pcb={uPcb} want={e.uRawLen})");
                    continue;
                }
                sMode = "lamarr+poly";
            }
            else
            {
                Console.Error.WriteLine($"[payload] dep {e.sName}: entry shape bad (raw={e.uRawLen} comp={e.uCompLen})");
                continue;
            }
            bool bPe = HasCliHeader(rgDep);//必须是托管PE才算依赖 诱饵伪PE与辅助件一律进helper
            var (iEnc, iOk, iFail) = (0, 0, 0);
            if (bJit && bPe)
            {
                try { (iEnc, iOk, iFail) = MethodEncryptor.DecryptAll(rgDep, uJitKey, uMaskKey, rgSig!); }
                catch (Exception eD) { Console.Error.WriteLine($"[payload] {e.sName}: method decode skipped ({eD.Message})"); }
            }
            string sKind = bPe ? "dep" : "helper";
            string sRestoreDep = "n/a";
            if (bPe)
            {
                rgDepNames.Add(e.sName);//native重建的.lamapp条目序 主件加托管依赖按容器序
                if (oHdr.Count == 0) sRestoreDep = "n/a (no scrub material was supplied)";
                else if (RestoreScrubByName(rgDep, e.sName, oHdr, out string sWhyD)) { iRestored++; sRestoreDep = "ok"; }
                else { iRestoreFail++; sRestoreDep = "failed: " + sWhyD; }
            }
            string sDirEnt = bPe ? sDirPayload : Path.Combine(sOutDir, "helper");
            Directory.CreateDirectory(sDirEnt);
            File.WriteAllBytes(Path.Combine(sDirEnt, e.sName), rgDep);
            Console.WriteLine($"[payload] {sKind,-6} {e.sName}: {rgDep.Length} bytes ({sMode}), method bodies encrypted={iEnc} decrypted={iOk} failed={iFail}, PeScrub restore={sRestoreDep}");
            rgManifest.Add(new
            {
                kind = sKind,
                name = e.sName,
                rawLen = e.uRawLen,
                compLen = e.uCompLen,
                mode = sMode,
                hdrRestore = sRestoreDep,
                encMethods = iEnc,
                decMethods = iOk,
                failMethods = iFail,
                sha256 = Sha256Hex(rgDep)
            });
        }
        var rgBundle = ExtractBundleEntries(rgExe);//bundle的entry0是boot镜像 entry1起是打包期保留的deps.json与runtimeconfig.json
        if (bJit && rgBundle.Count > 0)
        {
            byte[] rgBootImg = rgBundle[0].rgData;
            var (iEncB, iOkB, iFailB) = (0, 0, 0);
            try { (iEncB, iOkB, iFailB) = MethodEncryptor.DecryptAll(rgBootImg, uJitKey, uMaskKey, rgSig!); }
            catch (Exception eB) { Console.Error.WriteLine($"[payload] boot method decode skipped ({eB.Message})"); }
            File.WriteAllBytes(Path.Combine(sOutDir, "boot.dll"), rgBootImg);
            Console.WriteLine($"[payload] boot boot.dll: {rgBootImg.Length} bytes ({(rgBundle[0].bDeflate ? "deflate" : "stored")}), method bodies encrypted={iEncB} decrypted={iOkB} failed={iFailB}");
            rgManifest.Add(new
            {
                kind = "boot",
                name = "boot.dll",
                rawLen = rgBootImg.Length,
                compLen = 0,
                mode = rgBundle[0].bDeflate ? "bundle entry0(deflate)" : "bundle entry0(stored)",
                encMethods = iEncB,
                decMethods = iOkB,
                failMethods = iFailB,
                sha256 = Sha256Hex(rgBootImg)
            });
            for (int k = 1; k < rgBundle.Count; k++)
            {
                if (rgBundle[k].sName.Length == 0) continue;
                File.WriteAllBytes(Path.Combine(sDirPayload, rgBundle[k].sName), rgBundle[k].rgData);
                Console.WriteLine($"[payload] bundle {rgBundle[k].sName}: {rgBundle[k].rgData.Length} bytes ({(rgBundle[k].bDeflate ? "deflate" : "stored")})");
                rgManifest.Add(new
                {
                    kind = "bundle",
                    name = rgBundle[k].sName,
                    rawLen = rgBundle[k].lRawLen,
                    compLen = rgBundle[k].rgData.Length,
                    mode = rgBundle[k].bDeflate ? "bundle(deflate)" : "bundle(stored)",
                    encMethods = 0,
                    decMethods = 0,
                    failMethods = 0,
                    sha256 = Sha256Hex(rgBundle[k].rgData)
                });
            }
        }

        if (oHdr.Count > 0)//清痕逆汇总 每条都自证过 有失败即材料对不上 必须响亮
            Console.WriteLine($"[payload] PeScrub restore: {iRestored} ok, {iRestoreFail} failed" +
                (iRestoreFail == 0 ? " (payload == pack input, byte for byte)" : ""));
        var rgPartNames = new List<string> { sMainName };//同载荷真native重建 材料是产物自身的头区与前缀与条目与载荷 再加构建侧桩与boot
        rgPartNames.AddRange(rgDepNames);
        for (int k = 1; k < rgBundle.Count; k++)
            if (rgBundle[k].sName.Length > 0) rgPartNames.Add(rgBundle[k].sName);
        TryNativeRebuild(rgExe, sMainName, sStubPath, sBootPath, sOutDir, rgPartNames, sNativeOut, rgArtifactScrub);

        File.WriteAllText(Path.Combine(sOutDir, "manifest.json"),
            JsonSerializer.Serialize(rgManifest, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }
    private static void RollChainDecryptInPlace(byte[] rgRoll, uint uRollKey, bool bBe)//roll的链式at-rest解密 与区域导出同一算式 链式容器只能从单元0起按序解
    {
        if (rgRoll.Length < 8) return;
        int iPos = 4;
        uint uCh = 0;
        while (iPos + 4 <= rgRoll.Length)
        {
            int iLen = BitConverter.ToInt32(rgRoll, iPos);
            iPos += 4;
            if (iLen <= 0 || (iLen & 3) != 0 || iPos + iLen > rgRoll.Length) return;
            byte[] rgOne = new byte[iLen];
            Array.Copy(rgRoll, iPos, rgOne, 0, iLen);
            uCh = PianolaEngine.ChainMix(uCh ^ PianolaEngine.AtRestCryptChain(rgOne, uRollKey ^ uCh, bBe));
            Array.Copy(rgOne, 0, rgRoll, iPos, iLen);
            iPos += iLen;
        }
    }

    private static void UnDisturbHead(byte[] rgD, byte[] rgHead)//明文依赖条目只在前1024字节或整条长度内被XOR扰动 XOR自逆
    {
        byte[] rgK = GenDisturb(rgHead);
        int n = Math.Min(1024, rgD.Length);
        for (int i = 0; i < n; i++) rgD[i] = (byte)(rgD[i] ^ rgK[i & 15]);
    }

    private static bool PolyTagMatches(byte[] rgSeedKey, byte[] rgEnc, byte[] rgFull)
    {
        byte[] rgTag = PolyTag(rgSeedKey, rgEnc, rgEnc.Length);
        for (int i = 0; i < 16; i++) if (rgTag[i] != rgFull[i]) return false;
        return true;
    }

    private static string Sha256Hex(byte[] rg) => Convert.ToHexString(SHA256.HashData(rg));

    internal static bool HasCliHeader(byte[] rgD)//托管PE判据 MZ与PE签名与CLI数据目录非0 诱饵伪PE与gzip与pheropod一律不算
    {
        if (rgD == null || rgD.Length < 0x100) return false;
        if (rgD[0] != 0x4D || rgD[1] != 0x5A) return false;
        int iPe = BitConverter.ToInt32(rgD, 0x3C);
        if (iPe <= 0 || iPe + 0x60 >= rgD.Length || BitConverter.ToUInt32(rgD, iPe) != 0x4550) return false;
        ushort usMagic = BitConverter.ToUInt16(rgD, iPe + 24);
        int iDdStart = iPe + 24 + (usMagic == 0x20B ? 112 : 96);
        if (iDdStart + 14 * 8 + 8 > rgD.Length) return false;
        return BitConverter.ToUInt32(rgD, iDdStart + 14 * 8) != 0;
    }
    internal static List<(string sName, byte[] rgData, long lRawLen, bool bDeflate)> ExtractBundleEntries(byte[] rgExe)//bundle条目表 entry0是boot镜像 其余是打包期保留的deps.json与runtimeconfig.json
    {
        if (rgExe.Length < 64) return new List<(string, byte[], long, bool)>();
        int iMarker = FindMarkerOff(rgExe);
        if (iMarker < 0) return new List<(string, byte[], long, bool)>();
        int iPos = (int)BitConverter.ToInt64(rgExe, iMarker - 8);
        return ExtractBundleEntriesAt(rgExe, iPos);
    }

    internal static bool ExtractBootImage(byte[] rgExe, out byte[]? rgBoot, out string sWhy)//bundle的entry0是boot镜像 这里保留薄封装供离线审计调用
    {
        rgBoot = null;
        sWhy = "";
        var rgBundle = ExtractBundleEntries(rgExe);
        if (rgBundle.Count == 0) { sWhy = "bundle entries not found"; return false; }
        rgBoot = rgBundle[0].rgData;
        sWhy = rgBundle[0].bDeflate ? "bundle entry0(deflate)" : "bundle entry0(stored)";
        return true;
    }

    //真native重建 输出要与--pack --mode native对同一载荷逐字节相同 材料是产物自身与escrow加私钥与构建侧桩与构建侧boot
    //宿主entry0取构建侧LamarrBoot.dll 桩取构建侧lamarr_stub.dll PE头区donor与bundle前缀取产物自身 派生字段由PeWriter重算 其余字节原样带过
    //ant档外层头被清过痕 先用escrow的scrub.artifact还原donor头原值 自证不过就保持清痕态 不传rtc派生态 载荷字节取还原后的明文

    internal static int FindBundleHeaderOff(byte[] rgExe, string sMainName)//bundle头结构扫描 不能靠标记里的签名认它 判据先严后松 8B头偏移指向一个major为2且count合理且bundleId长度合理且entry0名等于主程序集名的清单
    {
        for (int i = 0; i + 48 <= rgExe.Length; i++)
        {
            long lH = BitConverter.ToInt64(rgExe, i);
            if (lH < 0x100 || lH + 64 > rgExe.Length) continue;
            int p = (int)lH;
            if (BitConverter.ToUInt32(rgExe, p) != 2u) continue;
            int iN = BitConverter.ToInt32(rgExe, p + 8);
            if (iN < 1 || iN > 0x1000) continue;
            p += 12;
            if (p >= rgExe.Length) continue;
            int iId = rgExe[p++];
            if ((iId & 0x80) != 0)
            {
                if (p >= rgExe.Length) continue;
                iId = ((iId & 0x7F) << 8) | rgExe[p++];
            }
            if (iId < 4 || p + iId + 56 > rgExe.Length) continue;
            p += iId + 40;
            p += 16 + 1;//绝对偏移与尺寸与类型 major为2时没有csz
            if (p >= rgExe.Length) continue;
            int iNl = rgExe[p++];
            if ((iNl & 0x80) != 0)
            {
                if (p >= rgExe.Length) continue;
                iNl = ((iNl & 0x7F) << 8) | rgExe[p++];
            }
            if (p + iNl > rgExe.Length) continue;
            if (Encoding.UTF8.GetString(rgExe, p, iNl).Equals(sMainName, StringComparison.OrdinalIgnoreCase))
                return (int)lH;
        }
        return -1;
    }

    internal static List<(string sName, byte[] rgData, long lRawLen, bool bDeflate)> ExtractBundleEntriesAt(byte[] rgExe, int iHdr)//bundle条目表 从给定的清单头位置解析
    {
        var rgOut = new List<(string, byte[], long, bool)>();
        int iPos = iHdr;
        if (iPos < 0 || iPos + 16 > rgExe.Length) return rgOut;
        uint uMajor = BitConverter.ToUInt32(rgExe, iPos); iPos += 4;
        iPos += 4;
        int iCount = BitConverter.ToInt32(rgExe, iPos); iPos += 4;
        if (iCount <= 0 || iCount > 0x1000) return rgOut;
        if (iPos >= rgExe.Length) return rgOut;
        iPos += 1 + rgExe[iPos];//bundleId
        if (uMajor >= 2) iPos += 40;
        for (int i = 0; i < iCount && i < 0x1000; i++)
        {
            if (iPos + 17 > rgExe.Length) break;
            long lOff = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            long lSz = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            long lCsz = 0;
            if (uMajor >= 6) { lCsz = BitConverter.ToInt64(rgExe, iPos); iPos += 8; }
            iPos += 1;
            if (iPos >= rgExe.Length) break;
            int iNl = rgExe[iPos];
            iPos += 1;
            string sName = "";
            if (iPos + iNl <= rgExe.Length) { sName = Encoding.UTF8.GetString(rgExe, iPos, iNl); iPos += iNl; }
            long lOnDisk = lCsz > 0 ? lCsz : lSz;
            if (lSz <= 0 || lOff < 0 || lOnDisk <= 0 || lOff + lOnDisk > rgExe.Length) continue;
            byte[] rg = new byte[lOnDisk];
            Array.Copy(rgExe, (int)lOff, rg, 0, (int)lOnDisk);
            if (lCsz > 0)//bundle 条目用 deflate
            {
                try
                {
                    using var msIn = new MemoryStream(rg);
                    using var ds = new DeflateStream(msIn, CompressionMode.Decompress);
                    using var msOut = new MemoryStream();
                    ds.CopyTo(msOut);
                    rg = msOut.ToArray();
                }
                catch { continue; }
                if (rg.Length != lSz) continue;
            }
            rgOut.Add((sName, rg, lSz, lCsz > 0));
        }
        return rgOut;
    }

    private static void TryNativeRebuild(byte[] rgExe, string sMainName, string? sStubPath, string? sBootPath,//可选 同载荷真native重建 桩与boot取构建侧或约定路径
        string sOutDir, IReadOnlyList<string> rgPartNames, string? sNativeOut, byte[]? rgArtifactScrub)
    {
        if (string.IsNullOrEmpty(sNativeOut)) return;
        string sStub = sStubPath ?? FindLamarrStub() ?? "";
        string sBoot = sBootPath ?? FindLamarrBoot(sOutDir) ?? "";
        if (sStub.Length == 0 || !File.Exists(sStub))
        {
            Console.Error.WriteLine("[payload] native repack skipped: lamarr_stub.dll not found (pass --stub <path>)");
            return;
        }
        if (sBoot.Length == 0 || !File.Exists(sBoot))
        {
            Console.Error.WriteLine("[payload] native repack skipped: LamarrBoot.dll not found (pass --boot <path>)");
            return;
        }
        string sFull = Path.GetFullPath(sNativeOut);
        Directory.CreateDirectory(Path.GetDirectoryName(sFull)!);
        try
        {
            if (BuildNativeFromParts(rgExe, sMainName, sStub, sBoot, sOutDir, rgPartNames, sFull, rgArtifactScrub))
                Console.WriteLine($"[payload] native repack -> {sFull} ({new FileInfo(sFull).Length} bytes)");
        }
        catch (Exception eN)
        {
            Console.Error.WriteLine($"[payload] native repack failed: {eN.Message}");
        }
    }

    //真native重建走native生产者PeWriter的同一条路径 LoadStub加LoadBoot加LoadPayloadFromParts再加Pack
    //条目取还原后的主件与容器里的托管依赖按容器序 再加产物bundle里保留的deps与rtc按原序
    private static bool BuildNativeFromParts(byte[] rgExe, string sMainName, string sStub, string sBoot,
        string sOutDir, IReadOnlyList<string> rgPartNames, string sOutPath, byte[]? rgArtifactScrub)
    {
        int iPe = BitConverter.ToInt32(rgExe, 0x3C);
        if (iPe <= 0 || iPe + 0x60 >= rgExe.Length || BitConverter.ToUInt32(rgExe, iPe) != 0x4550)
        {
            Console.Error.WriteLine("[payload] native repack skipped: artifact header unusable");
            return false;
        }
        uint uHdrs = BitConverter.ToUInt32(rgExe, iPe + 24 + 60);
        if (uHdrs < 0x100 || uHdrs > rgExe.Length)
        {
            Console.Error.WriteLine($"[payload] native repack skipped: SizeOfHeaders bad (0x{uHdrs:X})");
            return false;
        }
        int iHdr = FindBundleHeaderOff(rgExe, sMainName);
        if (iHdr < 0)
        {
            Console.Error.WriteLine("[payload] native repack skipped: artifact bundle header not located (structural scan)");
            return false;
        }
        int p = iHdr + 12;                                    //字段序 major与minor与count
        int iId = rgExe[p++];
        if ((iId & 0x80) != 0) { iId = ((iId & 0x7F) << 8) | rgExe[p++]; }
        p += iId + 40;                                        //bundleId与deps与rtc的位置与尺寸与flags
        byte[] rgPrefix = rgExe[iHdr..p];                     //count 与 deps/rtc 的 loc+size 由 LoadPayloadFromParts 重写

        string sDirPayload = Path.Combine(sOutDir, "payload");
        var rgParts = new List<(string sName, byte[] rgData, byte bType)>();
        foreach (string sN in rgPartNames)
        {
            string sF = Path.Combine(sDirPayload, sN);
            if (!File.Exists(sF))
            {
                Console.Error.WriteLine($"[payload] native repack skipped: payload part missing: {sN}");
                return false;
            }
            rgParts.Add((sN, File.ReadAllBytes(sF), 0));
        }
        //donor头是产物自身头区0到SizeOfHeaders 但ant产物那一块被清过痕 用escrow带回的artifact材料还原成清痕前的donor原值
        //自证 还原后再清一次痕必须逐字节回到产物里的那份 材料不对就保持清痕态
        byte[] rgDonorHdr = rgExe[..(int)uHdrs];
        if (rgArtifactScrub != null && rgArtifactScrub.Length > 0)
        {
            byte[] rgFixedHdr = (byte[])rgDonorHdr.Clone();
            if (TryRestoreScrub(rgFixedHdr, rgArtifactScrub, out string sWhyHdr))
            {
                rgDonorHdr = rgFixedHdr;
                Console.WriteLine("[payload] native repack: outer header restored to its pre-scrub (donor) bytes from the escrow");
            }
            else
            {
                Console.Error.WriteLine($"[payload] native repack: outer-header restore refused ({sWhyHdr}): the donor-owned " +
                    "fields stay scrubbed (the output will differ from a same-payload native build there)");
            }
        }
        var pe = new PeWriter();
        pe.LoadStub(sStub);
        pe.LoadBoot(sBoot);
        pe.LoadPayloadFromParts(sMainName, rgDonorHdr, rgParts, rgPrefix);
        //产物.rsrc等于载荷.rsrc 重建时把产物自身的资源节显式覆盖回去 合成出来的载荷载体只有条目没有资源节 从它提不出资源 旧产物无资源就传null
        if (PeWriter.TryExtractRes(rgExe, out byte[]? rgResA, out uint uRvaA, out uint uLenA, out uint uDirOffA, out uint uDirSizeA))
        {
            pe.SetResourceOverride(rgResA, uRvaA, uDirOffA, uDirSizeA);
            Console.WriteLine($"[payload] native repack: .rsrc taken from the artifact (rva=0x{uRvaA:X} len={uLenA} dirOff=0x{uDirOffA:X})");
        }
        else
        {
            pe.SetResourceOverride(null, 0, 0, 0);
            Console.WriteLine("[payload] native repack: artifact carries no .rsrc (DD2=0 kept)");
        }
        pe.Pack(sOutPath);
        Console.WriteLine($"[payload] native repack: stub={Path.GetFileName(sStub)} boot={Path.GetFileName(sBoot)} " +
            $"parts={rgParts.Count} donor=artifact headers(0x{uHdrs:X}), no rtc flags (same as the publish chain)");
        return true;
    }

    //native形态产物的载荷导出 不需要escrow与私钥 .lamapp是native生产者的简单容器
    //拼法是Lamarr!!加count加两段累计长度加20B条目表加名字表加Lamarr压缩块 bundle的entry0是宿主镜像 entry1起是保留的deps.json与runtimeconfig.json
    //native档载荷条目只压缩不清痕 解出来即打包输入的逐字节形态
    internal static int ExportPayloadNative(byte[] rgExe, int iLamRaw, int iLamSz, string sOutDir,
        string? sNativeOut, string? sStubPath, string? sBootPath, bool bScan)
    {
        if (iLamSz < 32 || Encoding.ASCII.GetString(rgExe, iLamRaw, 8) != "Lamarr!!")
        {
            Console.Error.WriteLine("[payload] native .lamapp magic bad");
            return 1;
        }
        int iCount = (int)BitConverter.ToUInt32(rgExe, iLamRaw + 8);
        int iTbl = iLamRaw + 20;
        if (iCount <= 0 || iCount > 0x1000 || iTbl + iCount * 20 > rgExe.Length)
        {
            Console.Error.WriteLine($"[payload] native .lamapp entry table bad: count={iCount}");
            return 1;
        }
        string sDirPayload = Path.Combine(sOutDir, "payload");
        Directory.CreateDirectory(sDirPayload);
        var rgName = new string[iCount];
        int iNp = iTbl + iCount * 20;
        for (int i = 0; i < iCount; i++)
        {
            int iNl = (int)BitConverter.ToUInt32(rgExe, iTbl + i * 20);
            if (iNl <= 0 || iNp + iNl > rgExe.Length)
            {
                Console.Error.WriteLine($"[payload] native name table bad at entry {i}");
                return 1;
            }
            rgName[i] = Encoding.UTF8.GetString(rgExe, iNp, iNl);
            iNp += iNl;
            while ((iNp & 3) != 0) iNp++;
        }
        var rgManifest = new List<object>();
        var rgPartNames = new List<string>();
        var rgAll = new List<(string sName, byte[] rg)>();
        string sMainName = rgName[0];
        for (int i = 0; i < iCount; i++)
        {
            int iRawLen = (int)BitConverter.ToUInt32(rgExe, iTbl + i * 20 + 4);
            int iCompLen = (int)BitConverter.ToUInt32(rgExe, iTbl + i * 20 + 8);
            int iCompOff = (int)BitConverter.ToUInt32(rgExe, iTbl + i * 20 + 12);
            if (iRawLen <= 0 || iCompLen <= 0 || iLamRaw + iCompOff + iCompLen > rgExe.Length)
            {
                Console.Error.WriteLine($"[payload] native entry {i} out of range (raw={iRawLen} comp={iCompLen}@{iCompOff})");
                return 1;
            }
            byte[] rgData = new byte[iRawLen];
            uint uPcb = (uint)iRawLen;
            int iRet = LamarrDecoder.Decode(rgData, ref uPcb, rgExe[(iLamRaw + iCompOff)..(iLamRaw + iCompOff + iCompLen)], (uint)iCompLen);
            if (iRet != 0 || uPcb != iRawLen)
            {
                Console.Error.WriteLine($"[payload] native entry {i} ({rgName[i]}): Lamarr decode failed (ret={iRet} pcb={uPcb} want={iRawLen})");
                return 1;
            }
            bool bPe = HasCliHeader(rgData);
            string sKind = i == 0 ? "main" : (bPe ? "dep" : "helper");
            File.WriteAllBytes(Path.Combine(sDirPayload, rgName[i]), rgData);
            rgPartNames.Add(rgName[i]);
            rgAll.Add((rgName[i], rgData));
            Console.WriteLine($"[payload] {sKind,-6} {rgName[i]}: {rgData.Length} bytes (lamapp(native)), scrubbed=no");
            rgManifest.Add(new
            {
                kind = sKind,
                name = rgName[i],
                rawLen = iRawLen,
                compLen = iCompLen,
                mode = "lamapp(native)",
                hdrRestore = "n/a (native payload is stored verbatim)",
                encMethods = 0,
                decMethods = 0,
                failMethods = 0,
                sha256 = Sha256Hex(rgData)
            });
        }
        int iHdrOff = FindBundleHeaderOff(rgExe, sMainName);
        var rgBundle = iHdrOff < 0 ? new List<(string sName, byte[] rgData, long lRawLen, bool bDeflate)>()
            : ExtractBundleEntriesAt(rgExe, iHdrOff);
        if (rgBundle.Count > 0)
        {
            File.WriteAllBytes(Path.Combine(sOutDir, "boot.dll"), rgBundle[0].rgData);
            Console.WriteLine($"[payload] boot boot.dll: {rgBundle[0].rgData.Length} bytes (native host image, bundle entry0)");
            rgManifest.Add(new
            {
                kind = "boot",
                name = "boot.dll",
                rawLen = rgBundle[0].rgData.Length,
                compLen = 0,
                mode = "bundle entry0(native)",
                hdrRestore = "n/a",
                encMethods = 0,
                decMethods = 0,
                failMethods = 0,
                sha256 = Sha256Hex(rgBundle[0].rgData)
            });
            for (int k = 1; k < rgBundle.Count; k++)
            {
                if (rgBundle[k].sName.Length == 0) continue;
                File.WriteAllBytes(Path.Combine(sDirPayload, rgBundle[k].sName), rgBundle[k].rgData);
                rgPartNames.Add(rgBundle[k].sName);
                Console.WriteLine($"[payload] bundle {rgBundle[k].sName}: {rgBundle[k].rgData.Length} bytes (kept entry)");
                rgManifest.Add(new
                {
                    kind = "bundle",
                    name = rgBundle[k].sName,
                    rawLen = rgBundle[k].lRawLen,
                    compLen = rgBundle[k].rgData.Length,
                    mode = "bundle(stored)",
                    hdrRestore = "n/a",
                    encMethods = 0,
                    decMethods = 0,
                    failMethods = 0,
                    sha256 = Sha256Hex(rgBundle[k].rgData)
                });
            }
        }
        if (bScan)
        {
            int iHits = 0;
            foreach (var (sName, rg) in rgAll)
            {
                if (PeScrub.HasRich(rg)) { Console.WriteLine($"[scan] HIT {sName}: MSVC Rich header"); iHits++; }
                if (PeScrub.HasDebugDir(rg)) { Console.WriteLine($"[scan] HIT {sName}: PE debug directory"); iHits++; }
                uint uTs = PeScrub.TimeStamp(rg);
                if (uTs != 0) { Console.WriteLine($"[scan] HIT {sName}: time stamp 0x{uTs:X8}"); iHits++; }
            }
            Console.WriteLine(iHits == 0 ? "[scan] clean" :
                $"[scan] {iHits} hit(s) - expected in this tier: the native form stores the payload verbatim (no PeScrub)");
        }
        File.WriteAllText(Path.Combine(sOutDir, "manifest.json"),
            JsonSerializer.Serialize(rgManifest, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        TryNativeRebuild(rgExe, sMainName, sStubPath, sBootPath, sOutDir, rgPartNames, sNativeOut, null);//native档的外层头没清痕 无artifact材料 donor头原样带过
        return 0;
    }

    //escrow密封区的scrub块 entries是载荷条目名到清痕前头字节hex的映射 artifact是产物外层头区清痕前的原字节hex
    //fmt只是布局标识 不是当前这一版就当作没有材料 响亮告警不猜
    private static Dictionary<string, string> ParseScrub(string sJson, out string sArtifactHex)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        sArtifactHex = "";
        if (string.IsNullOrEmpty(sJson)) return o;
        try
        {
            using var doc = JsonDocument.Parse(sJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return o;
            if (doc.RootElement.TryGetProperty("fmt", out var eFmt) && eFmt.ValueKind == JsonValueKind.String
                && eFmt.GetString() != sScrubFmt)
            {
                Console.Error.WriteLine($"[payload] scrub material has an unknown layout ('{eFmt.GetString()}' != '{sScrubFmt}'): restoring nothing");
                return o;
            }
            if (doc.RootElement.TryGetProperty("artifact", out var eA) && eA.ValueKind == JsonValueKind.String)
                sArtifactHex = eA.GetString()!;
            if (doc.RootElement.TryGetProperty("entries", out var eE) && eE.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in eE.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) o[p.Name] = p.Value.GetString()!;
        }
        catch (Exception e) { Console.Error.WriteLine($"[payload] scrub parse failed ({e.Message}); restoring nothing"); }
        return o;
    }

    private static bool RestoreScrubByName(byte[] rgDll, string sName, Dictionary<string, string> oHdr, out string sWhy)//把一份载荷条目还原成清痕前的字节 材料按容器条目名取
    {
        if (!oHdr.TryGetValue(sName, out string? sHex)) { sWhy = $"no restore material for '{sName}'"; return false; }
        byte[] rgBlob;
        try { rgBlob = Convert.FromHexString(sHex); }
        catch (Exception e) { sWhy = $"bad restore blob ({e.Message})"; return false; }
        return TryRestoreScrub(rgDll, rgBlob, out sWhy);
    }

    //把PeScrub的确定逆写回缓冲并自证 再清一次痕必须逐字节回到同一份 自证通过才改调用方缓冲
    //材料错或布局不符都退化成保持清痕态 绝不混进去
    private static bool TryRestoreScrub(byte[] rgPe, byte[] rgBlob, out string sWhy)
    {
        sWhy = "";
        byte[] rgFixed = (byte[])rgPe.Clone();
        if (!PeScrub.ApplyRestore(rgFixed, rgBlob)) { sWhy = "restore layout mismatch"; return false; }
        byte[] rgCheck = (byte[])rgFixed.Clone();
        PeScrub.Scrub(rgCheck);
        for (int i = 0; i < rgCheck.Length; i++)
            if (rgCheck[i] != rgPe[i]) { sWhy = "self-check failed (re-scrubbing the restored bytes != the supplied bytes)"; return false; }
        Array.Copy(rgFixed, rgPe, rgPe.Length);
        return true;
    }

    //native重建的主版本取还原出的runtimeconfig的框架版本头段 不改runtimeconfig本身 两个清单逐字节原样保留
    private static int ReadRtcMajor(string? sRtcPath)
    {
        if (sRtcPath == null || !File.Exists(sRtcPath)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(sRtcPath));
            if (!doc.RootElement.TryGetProperty("runtimeOptions", out JsonElement oRt)) return 0;
            int iMj = 0;
            void Take(JsonElement oFw)
            {
                if (!oFw.TryGetProperty("version", out JsonElement oV) || oV.ValueKind != JsonValueKind.String) return;
                string? sVer = oV.GetString();
                if (string.IsNullOrEmpty(sVer)) return;
                int iDot = sVer!.IndexOf('.');
                if (iDot > 0 && int.TryParse(sVer.Substring(0, iDot), out int iOne)) iMj = iOne;
            }
            if (oRt.TryGetProperty("framework", out JsonElement oOne)) Take(oOne);
            if (oRt.TryGetProperty("frameworks", out JsonElement oMany) && oMany.ValueKind == JsonValueKind.Array)
                foreach (JsonElement oFw in oMany.EnumerateArray()) Take(oFw);
            return iMj;
        }
        catch (Exception e) { Console.Error.WriteLine($"[payload] runtimeconfig parse failed ({e.Message})"); return 0; }
    }

    //LamarrBoot.dll约定位置 从仓库根的Lamarr与Native与Boot与bin与Release下找 目录按还原出的runtimeconfig主版本挑 挑不到就取第一份
    internal static string? FindLamarrBoot(string sDirPayload)
    {
        int iMaj = ReadRtcMajor(Directory.GetFiles(sDirPayload, "*.runtimeconfig.json").FirstOrDefault());
        DirectoryInfo? di = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && di != null; i++, di = di.Parent)
        {
            string sRoot = Path.Combine(di.FullName, "Lamarr", "Native", "Boot", "bin", "Release");
            if (!Directory.Exists(sRoot)) continue;
            string[] rg = Directory.GetFiles(sRoot, "LamarrBoot.dll", SearchOption.AllDirectories);
            if (rg.Length == 0) continue;
            if (iMaj > 0)
                foreach (string s in rg)
                    if (Path.GetFileName(Path.GetDirectoryName(s)!).StartsWith("net" + iMaj + ".", StringComparison.Ordinal)) return s;
            return rg[0];
        }
        return null;
    }

    internal static string? FindLamarrStub()//lamarr_stub.dll约定位置 从打包器所在目录向上找仓库根
    {
        string sRel = Path.Combine("Lamarr", "Native", "out", "lamarr_stub.dll");
        DirectoryInfo? di = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && di != null; i++, di = di.Parent)
        {
            string sTry = Path.Combine(di.FullName, sRel);
            if (File.Exists(sTry)) return sTry;
        }
        return null;
    }
}
