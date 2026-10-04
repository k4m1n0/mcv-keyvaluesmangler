namespace Lamarr.Packer;

using System.Text;
using System.Text.Json;

internal partial class PeWriterAntheil
{
    static bool RegionReversed(int iType) => iType == 1 || iType == 2 || iType == 3 || iType == 6 || iType == 7 || iType == 8;//region在容器内是否整块字节反转

    internal static int Unpack(string sExe, string sOutDir, bool bScan, string? sEscrow = null, string? sEscrowPriv = null, string? sKeyHex = null,
        string? sNativeOut = null, string? sStubPath = null, string? sBootPath = null)
    {
        byte[] rgExe = File.ReadAllBytes(sExe);
        int iPe = BitConverter.ToInt32(rgExe, 0x3C);
        if (iPe + 0x18 > rgExe.Length || BitConverter.ToUInt32(rgExe, iPe) != 0x4550)
        {
            Console.Error.WriteLine("[unpack] not a PE");
            return 1;
        }
        int iSecCount = BitConverter.ToUInt16(rgExe, iPe + 6);
        int iSecOff = iPe + 24 + BitConverter.ToUInt16(rgExe, iPe + 20);
        int iRaw = -1, iRawSz = 0, iLam = -1, iLamSz = 0;
        for (int i = 0; i < iSecCount; i++)
        {
            int o = iSecOff + i * 40;
            if (o < 0 || o + 40 > rgExe.Length) { Console.Error.WriteLine("[unpack] section table truncated"); return 1; }
            string sNm = Encoding.ASCII.GetString(rgExe, o, 8).TrimEnd('\0');
            if (sNm == ".rdata") { iLamSz = 0; iRawSz = (int)BitConverter.ToUInt32(rgExe, o + 16); iRaw = (int)BitConverter.ToUInt32(rgExe, o + 20); }
            if (sNm == ".lamapp") { iLamSz = (int)BitConverter.ToUInt32(rgExe, o + 16); iLam = (int)BitConverter.ToUInt32(rgExe, o + 20); }
        }
        //native产物只有.stub与.lamapp 没有容器与种子与入口层 载荷在.lamapp里只压缩不清痕 所以不需要escrow与私钥
        if (iLam >= 0 && iLam + iLamSz <= rgExe.Length)
        {
            Console.WriteLine($"[unpack] native-form artifact: .lamapp @0x{iLam:X} ({iLamSz} bytes) - no escrow/seed/entry-layer in this form");
            return ExportPayloadNative(rgExe, iLam, iLamSz, sOutDir, sNativeOut, sStubPath, sBootPath, bScan);
        }
        if (iRaw < 0 || iRaw + iRawSz > rgExe.Length)
        {
            Console.Error.WriteLine("[unpack] .rdata not found");
            return 1;
        }
        byte[] rgCont = new byte[iRawSz];
        Array.Copy(rgExe, iRaw, rgCont, 0, iRawSz);

        if (rgCont.Length < 64) { Console.Error.WriteLine("[unpack] .rdata too small"); return 1; }
        byte[] rgHead = new byte[64];//容器头64B明文 它不能反推出种子
        Array.Copy(rgCont, 0, rgHead, 0, 64);
        string sSeed;
        string sScrubJson = "";//清痕逆材料 由密封区带回 只有escrow路径才有
        if (sEscrow != null)
        {
            var oSEsc = JsonDocument.Parse(ReadEscrowJson(sEscrow, sEscrowPriv)).RootElement;//材料只能从escrow取 产物不自带捷径
            sSeed = oSEsc.TryGetProperty("seed", out var eSd) ? eSd.GetString()! : "";
            if (sSeed.Length == 0) { Console.Error.WriteLine("[unpack] escrow carries no 'seed': cannot decrypt the container"); return 1; }
            sScrubJson = oSEsc.TryGetProperty("scrub", out var eSc) ? eSc.GetRawText() : "";
            if (sScrubJson.Length == 0)
                Console.Error.WriteLine("[unpack] NOTE this escrow carries no 'scrub' block: the payload restores to the " +
                    "*scrubbed* bytes only (8-byte PeScrub boundary) and the native re-pack keeps the scrubbed outer header " +
                    "(see Lamarr_VM_Design_M1.md §18.16.15/§18.16.19)");
        }
        else if (!string.IsNullOrEmpty(sKeyHex))
        {
            byte[] rgK;
            try { rgK = Convert.FromHexString(sKeyHex); }
            catch (Exception eHex) { Console.Error.WriteLine($"[unpack] --key is not hex: {eHex.Message}"); return 1; }
            if (rgK.Length != 32) { Console.Error.WriteLine("[unpack] --key must be 64 hex chars (32 bytes)"); return 1; }
            sSeed = KdfSeedHex(rgK);
        }
        else
        {
            Console.Error.WriteLine("[unpack] needs the escrow (-Escrow plus -EscrowKeyFile) or --key <64hex>:");
            Console.Error.WriteLine("          the artifact does not carry a recoverable seed.");
            return 1;
        }
        rgSigOut = GenAnchorSig(sSeed);//unpack路径也要有锚点键 否则审计与标记定位会空引用
        uint uKey = LamKey(sSeed);
        int iHead = 68 + ((int)((uKey >> 0) & 0x1Fu) << 2);
        int iDataBase = (iHead + 16 + ((int)((uKey >> 8) & 0x3Fu) << 2) + 7) & ~7;
        byte[] rgSeedKey = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        uint uAdj = MixAdj(rgSeedKey);
        Console.WriteLine($"[unpack] seed={sSeed} uKey=0x{uKey:X8} iHead={iHead} iDataBase={iDataBase}");
        if (sEscrow != null)
        {
            var oEsc = JsonDocument.Parse(ReadEscrowJson(sEscrow, sEscrowPriv)).RootElement;//用escrow清单交叉校验 派生出的种子与键要和清单相同
            string sSeedE = oEsc.TryGetProperty("seed", out var eSeed) ? eSeed.GetString()! : "";
            ulong uVmSeedE = oEsc.TryGetProperty("vmSeed", out var eVm) ? Convert.ToUInt64(eVm.GetString()!, 16) : 0;
            uint uRollE = oEsc.TryGetProperty("rollKey", out var eRk) ? Convert.ToUInt32(eRk.GetString()!, 16) : 0;
            string sHeadE = oEsc.TryGetProperty("head", out var eHd) ? eHd.GetString()! : "";
            string sAdjE = oEsc.TryGetProperty("adj", out var eAdj) ? eAdj.GetString()! : "";
            Console.WriteLine($"[escrow] vmSeed={uVmSeedE:X16} isa={IsaPool.CarrierIsa(uVmSeedE, 0)} rollKey=0x{uRollE:X8} adj=0x{sAdjE}");
            Console.WriteLine($"[escrow] seed matches artifact? {sSeedE == sSeed}");
            Console.WriteLine($"[escrow] head matches artifact? {sHeadE.Equals(Convert.ToHexString(rgHead), StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"[escrow] seedKey/adj matches artifact? {oEsc.TryGetProperty("seedKey", out var eSk) && eSk.GetString()!.Equals(Convert.ToHexString(rgSeedKey), StringComparison.OrdinalIgnoreCase)} adj={MixAdj(rgSeedKey).ToString("X8")}");
        }

        int iPos0 = -1, iCount = 0;//块0含区表 不在iDataBase 前有伪BSJB区 按count与magic=0x80定位 窗口需容纳扰动后布局
        for (int iOff = iDataBase; iOff + 8 <= rgCont.Length && iOff < iDataBase + 0x4000; iOff++)
        {
            byte[] rg = XorBytes(rgCont[iOff..(iOff + 8)], rgSeedKey, uAdj);
            int c = BitConverter.ToInt32(rg, 0);
            if (BitConverter.ToUInt32(rg, 4) == 0x80u && c >= 1 && c <= 64) { iPos0 = iOff; iCount = c; break; }
        }
        if (iPos0 < 0) { Console.Error.WriteLine("[unpack] block0 not located"); return 1; }
        int iBlkLen = rgCont.Length - iPos0;//块长按容器剩余取 设上限会截断region表
        byte[] rgBlk = XorBytes(rgCont[iPos0..(iPos0 + iBlkLen)], rgSeedKey, uAdj);
        int iHdr = 8 + iCount * 12 + 4;
        int iCompLen = BitConverter.ToInt32(rgBlk, 8 + iCount * 12);
        int iRegBase = iHdr + iCompLen;
        Console.WriteLine($"[unpack] block0@{iPos0} count={iCount} compLen={iCompLen}");

        Directory.CreateDirectory(sOutDir);

        byte[]? rgEngine = null;//先取region 6 引擎镜像 roll密钥要用引擎.text摘要
        for (int r = 0; r < iCount; r++)
        {
            int iTypeR = BitConverter.ToInt32(rgBlk, 8 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 0);
            if (iTypeR != 6) continue;
            int iOffE = BitConverter.ToInt32(rgBlk, 12 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 1);
            int iLenE = BitConverter.ToInt32(rgBlk, 16 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 2);
            if (iLenE <= 0 || iOffE < 0 || iLenE > rgBlk.Length - iRegBase - iOffE) continue;
            rgEngine = new byte[iLenE];
            Array.Copy(rgBlk, iRegBase + iOffE, rgEngine, 0, iLenE);
            Array.Reverse(rgEngine);
            Console.WriteLine($"[unpack] engine .text sum = 0x{EngineTextSum(rgEngine):X8}");
            break;
        }

        var rgMeta = new List<object>();
        var rgAll = new List<(string sName, byte[] rg)>();
        for (int r = 0; r < iCount; r++)
        {
            int iType = BitConverter.ToInt32(rgBlk, 8 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 0);
            int iOff = BitConverter.ToInt32(rgBlk, 12 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 1);
            int iLen = BitConverter.ToInt32(rgBlk, 16 + r * 12) ^ (int)RegionFieldMask(rgSeedKey, r, 2);
            if (iLen <= 0 || iOff < 0 || iLen > rgBlk.Length - iRegBase - iOff) continue;
            byte[] rgR = new byte[iLen];
            Array.Copy(rgBlk, iRegBase + iOff, rgR, 0, iLen);
            if (RegionReversed(iType)) Array.Reverse(rgR);
            string sFile = $"r{iType}_{r}.bin";
            File.WriteAllBytes(Path.Combine(sOutDir, sFile), rgR);
            rgMeta.Add(new { type = iType, index = r, iOff = iOff, len = iLen, file = sFile });
            rgAll.Add(($"region[{iType}]", rgR));
            if (iType == 7)
            {
                uint uRollKey = MixAdj(SeedKey(Encoding.ASCII.GetBytes(sSeed))) ^ EngineTextSum(rgEngine);//roll逐4字节BE字与LCG流异或 ISA字节序未知 be和le两种都试
                //链式roll 单元i的at-rest键等于roll钥匙异或前序明文折叠链 只能从单元0起按序解
                void RollChainDecryptInPlace(byte[] rgRoll, bool bBe, out string sBad)
                {
                    sBad = "";
                    if (rgRoll == null || rgRoll.Length < 8) { sBad = $"roll blob too short ({rgRoll?.Length ?? -1}B)"; return; }
                    int iPos = 4, iUnit = 0;
                    uint uCh = 0;
                    while (iPos + 4 <= rgRoll.Length)
                    {
                        int iLen = BitConverter.ToInt32(rgRoll, iPos);
                        iPos += 4;
                        if (iLen <= 0 || (iLen & 3) != 0 || iPos + iLen > rgRoll.Length)
                        {
                            //链式roll只能从单元0起按序解 中途失败不能静默返回 否则半解的roll会被当成解得开
                            sBad = $"unit {iUnit} @0x{(iPos - 4):X} len={iLen} (blob {rgRoll.Length}B)";
                            return;
                        }
                        byte[] rgOne = new byte[iLen];
                        Array.Copy(rgRoll, iPos, rgOne, 0, iLen);
                        uCh = PianolaEngine.ChainMix(uCh ^ PianolaEngine.AtRestCryptChain(rgOne, uRollKey ^ uCh, bBe));
                        Array.Copy(rgOne, 0, rgRoll, iPos, iLen);
                        iPos += iLen;
                        iUnit++;
                    }
                }
                foreach (bool bBe in new[] { true, false })
                {
                    byte[] rgP = (byte[])rgR.Clone();
                    RollChainDecryptInPlace(rgP, bBe, out string sBad);//链式roll只能从单元0起按序解 没有整段单流解这条路径
                    if (sBad.Length > 0) Console.WriteLine($"[unpack] WARN chain roll decrypt stopped early: {sBad} (the dumped roll is only partially decrypted)");
                    string sPf = $"roll.{(bBe ? "be" : "le")}.bin";
                    File.WriteAllBytes(Path.Combine(sOutDir, sPf), rgP);
                    rgAll.Add((sPf, rgP));
                }
                Console.WriteLine($"[unpack] roll key=0x{uRollKey:X8} (includes the engine digest; both be and le decryptions written)");
            }
        }
        File.WriteAllText(Path.Combine(sOutDir, "regions.json"),
            JsonSerializer.Serialize(rgMeta, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        Console.WriteLine($"[unpack] {iCount} region(s) -> {sOutDir}");

        //载荷导出 主程序集加内置依赖加boot 含方法体解密的层1逆 可选lamarr native同载荷重建
        ExportPayload(rgExe, rgCont, sSeed, rgSeedKey, uKey, sOutDir, sNativeOut, sStubPath, sBootPath, sScrubJson);

        ExportBootLayer(rgExe, sOutDir);//入口层app条目=入口层镜像+加密boot镜像+16B尾部记录 还原供离线核查
        if (!AuditEntryLayer(rgExe, out string sAudit, sOutDir)) Console.WriteLine($"[unpack] WARN entry layer audit: {sAudit}");//顺带把入口层那份roll与引擎还原出来

        if (bScan)
        {
            string[] rgTok = { "__VM_PASSTHROUGH", "pianola", "notenrolle", "engine.dll",
                "pianolarun", "dec_loop", "h_tbl", "notenrolleisa", "compilejitkeyroll" };
            int iHits = 0;
            foreach (var (sName, rg) in rgAll)
            {
                string sLow = Encoding.Latin1.GetString(rg).ToLowerInvariant();
                foreach (string sT in rgTok)
                    if (sLow.Contains(sT)) { Console.WriteLine($"[scan] HIT {sName}: \"{sT}\""); iHits++; }
                if (PeScrub.HasRich(rg)) { Console.WriteLine($"[scan] HIT {sName}: MSVC Rich header"); iHits++; }//构建特征 含Rich块与Debug目录与真实构建时刻的时间戳
                if (PeScrub.HasDebugDir(rg)) { Console.WriteLine($"[scan] HIT {sName}: PE debug directory"); iHits++; }
                uint uTs = PeScrub.TimeStamp(rg);
                long llNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (uTs != 0 && uTs >= llNow - 86400 && uTs <= llNow + 86400)
                { Console.WriteLine($"[scan] HIT {sName}: time stamp looks like a real build time ({uTs})"); iHits++; }
            }
            Console.WriteLine(iHits == 0 ? "[scan] clean (no residue marker after decryption)" : $"[scan] {iHits} hit(s)");
        }
        return 0;
    }
}
