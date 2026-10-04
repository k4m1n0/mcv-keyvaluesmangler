namespace Lamarr.Packer;

using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

//escrow逐字节重放产物并解包其载荷 不参与签名 不含K也不含签名私钥
internal partial class PeWriterAntheil
{
    const int iEscrowVer = 1;
    const string sWrapAlg = "RSA-OAEP-SHA256";

    internal const string sFormatId = "antheil-fmt1";//打包器格式修订号 重放只对同格式成立 所以它进明文头参与认证并在读取时硬比对

    internal const string sScrubFmt = "pe-scrub-1";//scrub blob的布局标识escrow里只存hex 靠它认布局 读到不认识的fmt就当没有材料

    private string sEscrowPath = "";
    private string sEscrowPubKeyPath = "";
    private bool bEscrowDiag;
    private string sReplayEscrowSig = "";//重放时从源escrow复制来的sigEscrow 绝不重签 重签需要K
    private readonly SortedDictionary<string, string> oScrubEntries = new(StringComparer.Ordinal);//清痕逆材料的条目级 按容器条目名存 只进escrow密封区
    private string sArtifactScrub = "";//产物外层头区清痕前的原字节 只进escrow密封区 不随产物分发
    private string sReplayScrub = "";//重放时原样复制源escrow的整份材料 材料核心必须逐字节相同
    private string sVerifyPayload = "", sVerifyMode = "", sVerifyNative = "";

    public void SetReplayEscrowSig(string sHex) => sReplayEscrowSig = sHex ?? "";//重放注入源escrow的成品sigEscrow

    public void SetReplayScrub(string sJson) => sReplayScrub = sJson ?? "";//重放注入源escrow的清痕逆材料 原样JSON 绝不重算

    internal void CaptureArtifactScrub(byte[] rgHdrs) => sArtifactScrub = Convert.ToHexString(PeScrub.CaptureRestore(rgHdrs));//产物外层头区清痕前的唯一捕获点 必须在PeScrub.Scrub之前调

    internal string ScrubJson()//密封区要写的值 重放时以源escrow的原文为准 保证材料核心逐字节相同
    {
        if (sReplayScrub.Length > 0) return sReplayScrub;
        if (oScrubEntries.Count == 0 && sArtifactScrub.Length == 0) return "";
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["fmt"] = sScrubFmt,
            ["entries"] = oScrubEntries,
            ["artifact"] = sArtifactScrub,
        }, sEscrowJson);
    }

    public void SetEscrow(string sPath, string sPubKeyPath, bool bDiag)
    {
        sEscrowPath = sPath; sEscrowPubKeyPath = sPubKeyPath; bEscrowDiag = bDiag;
    }

    public void SetVerifyInfo(string sPayloadFp, string sMode, string sNativeFp)//写escrow时把本次的输入指纹记进密封区 重放时用同一套算法现算并比对
    {
        sVerifyPayload = sPayloadFp; sVerifyMode = sMode; sVerifyNative = sNativeFp;
    }

    internal static string Fp(byte[] rg) => Convert.ToHexString(Sha512.Hash(rg)).Substring(0, 32);//128bit 够判是不是同一份
    internal static string FpOfFile(string sPath) => Fp(File.ReadAllBytes(sPath));
    internal static string FpOfFiles(IEnumerable<string> rgPaths)
        => Fp(Encoding.ASCII.GetBytes(string.Join(",", rgPaths.Select(s => File.Exists(s) ? FpOfFile(s) : "missing"))));

    static readonly string[] rsHdrKeys = { "artifact", "isa", "carrierFmt", "widthCode", "switches", "wrap", "pubFp", "packer" };
    static readonly string[] rsSecKeys =//密封区白名单是最小集 诊断类字段只走-EscrowDiag的明文诊断件
    {
        "vmSeed", "seed", "head", "pubB", "sig", "verify", "scrub", sEscrowSigField
    };
    static readonly string[] rsVerifyKeys = { "payload", "mode", "native" };
    static readonly string[] rsScrubKeys = { "fmt", "entries", "artifact" };//scrub块的子键白名单 写入与读取各查一次

    static void SchemaCheck(IEnumerable<string> rgNames, string[] rsAllowed, string sWhat)
    {
        foreach (string s in rgNames)
        {
            if (Array.IndexOf(rsAllowed, s) >= 0) continue;
            throw new InvalidOperationException($"{sWhat} carries a field outside the whitelist: '{s}' (escrow only carries what replay/unpack need)");
        }
    }

    static void CheckPackFormat(Dictionary<string, string> oHdr)//格式修订号硬比对 打开与自检共用 抽出来是为了让负例能直接打它
    {
        if (!oHdr.TryGetValue("packer", out string? sPack))
            throw new InvalidOperationException("escrow has no 'packer' field: regenerate the escrow with the current packer");
        if (sPack != sFormatId)
            throw new InvalidOperationException($"escrow was written by a different packer format ('{sPack}' != '{sFormatId}'): replay is only byte-identical under the same format revision");
    }

    internal const string sEscrowSigField = "sigEscrow";//escrow的来源签名字段 由K的域分离支路签出且永不落盘

    internal static string EscrowCore(JsonElement oSec)//签名覆盖按白名单顺序序列化的密封区字段并去掉sigEscrow 材料相同签名就仍有效 改任何一处材料都会失效
    {
        var sb = new StringBuilder("{");
        bool bFirst = true;
        foreach (string sKey in rsSecKeys)
        {
            if (sKey == sEscrowSigField) continue;
            if (!oSec.TryGetProperty(sKey, out var v)) continue;
            if (!bFirst) sb.Append(',');
            bFirst = false;
            sb.Append(JsonSerializer.Serialize(sKey)).Append(':').Append(v.GetRawText());
        }
        sb.Append('}');
        return sb.ToString();
    }

    internal static string EscrowSign(byte[] rgK, Dictionary<string, object?> oSec, string sCopied)//签或复制sigEscrow 重放路径给源escrow的成品签名 绝不能重签 那需要K
    {
        if (sCopied.Length == 128) return sCopied;
        if (rgK.Length != 32) throw new InvalidOperationException("cannot sign the escrow: no build key and no copied sigEscrow from the source escrow");
        using var o = JsonDocument.Parse(JsonSerializer.Serialize(oSec, sEscrowJson));
        return Convert.ToHexString(Ed25519.Sign(KdfLabel(rgK, "seal:priv")[..32], Sha512.Hash(Encoding.UTF8.GetBytes(EscrowCore(o.RootElement)))));
    }

    internal static string? CheckEscrowSig(JsonElement oSec)//验escrow来源自洽 返回null表示已用共享钥验过 返回字符串表示没有可验的来源锚 只告警不阻断
    {
        string G(string sK) => oSec.TryGetProperty(sK, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        string sPubB = G("pubB"), sSig = G("sig"), sSigE = G(sEscrowSigField);
        if (sPubB.Length == 0 && sSig.Length == 0 && sSigE.Length == 0)
            return "unsealed build: no pub_B/sig/sigEscrow (bytes can still be replayed, but this escrow carries no provenance anchor)";
        if (sPubB.Length != 64 || sSig.Length != 128 || sSigE.Length != 128)
            throw new InvalidOperationException($"escrow seal materials are incomplete (pubB={sPubB.Length} sig={sSig.Length} sigEscrow={sSigE.Length} hex chars): regenerate the escrow");
        if (!Ed25519.Verify(Convert.FromHexString(sPubB), Sha512.Hash(Encoding.UTF8.GetBytes(EscrowCore(oSec))), Convert.FromHexString(sSigE)))
            throw new InvalidOperationException("escrow signature invalid: its materials were edited, or pub_B/sigEscrow were replaced (self-consistency check; provenance additionally needs a pinned pub_B)");
        return null;
    }

    static byte[] Rnd(int cb)
    {
        byte[] rg = new byte[cb];
        RandomNumberGenerator.Fill(rg);//静态一次填充 比每次Create一个RNG实例少一次分配
        return rg;
    }

    static AesGcm NewAesGcm(byte[] rgKey)//Poly1305用16字节标签 两条分支写出的密文必须相同
    {
#if NET8_0_OR_GREATER
        return new AesGcm(rgKey, 16);
#else
        return new AesGcm(rgKey);
#endif
    }

    static string MakeHeader(string sArtifact, string sIsa, string sCarrierFmt, string sWidthCode, string sSwitches, string sWrap, string sPubFp)
        => "LAMARR-ESCROW " + iEscrowVer + "\n"
         + "artifact: " + sArtifact + "\n"
         + "isa: " + sIsa + "\n"
         + "carrierFmt: " + sCarrierFmt + "\n"
         + "widthCode: " + sWidthCode + "\n"
         + "switches: " + sSwitches + "\n"
         + "wrap: " + sWrap + "\n"
         + "pubFp: " + sPubFp + "\n"
         + "packer: " + sFormatId + "\n";

    static string ArmorLines(string sLabel, byte[] rg)//PEM风味装甲 不用PEM辅助API且格式跨目标框架一致
    {
        string sB64 = Convert.ToBase64String(rg);
        var sb = new StringBuilder(sB64.Length + sB64.Length / 64 + 96);//按最终长度预估容量 免扩展重分配
        sb.Append("-----BEGIN ").Append(sLabel).Append("-----\n");
        for (int i = 0; i < sB64.Length; i += 64) sb.Append(sB64, i, Math.Min(64, sB64.Length - i)).Append('\n');
        sb.Append("-----END ").Append(sLabel).Append("-----\n");
        return sb.ToString();
    }

    static byte[] DearmorLines(string[] rgLines, string sLabel, int iFrom)
    {
        string sBegin = "-----BEGIN " + sLabel + "-----", sEnd = "-----END " + sLabel + "-----";
        int i = iFrom;
        while (i < rgLines.Length && rgLines[i] != sBegin) i++;
        if (i >= rgLines.Length) throw new InvalidOperationException($"escrow is missing the {sLabel} block");
        var sb = new StringBuilder();
        for (i++; i < rgLines.Length && rgLines[i] != sEnd; i++) sb.Append(rgLines[i]);
        if (i >= rgLines.Length) throw new InvalidOperationException($"escrow has an unterminated {sLabel} block");
        return Convert.FromBase64String(sb.ToString());
    }

    static byte[] Concat(params byte[][] rgAll)
    {
        byte[] rg = new byte[rgAll.Sum(a => a.Length)];
        int o = 0;
        foreach (byte[] a in rgAll) { Array.Copy(a, 0, rg, o, a.Length); o += a.Length; }
        return rg;
    }

    internal static void EscrowSeal(string sPath, string sPubKeyPath, string sArtifact, string sIsa, string sCarrierFmt,//信封由公开头与WRAP块与AEAD块组成 公开头明文且同时是认证数据
        string sWidthCode, string sSwitches, string sSecretJson)
    {
        if (sPubKeyPath.Length == 0)
            throw new InvalidOperationException("writing an escrow needs --escrow-pubkey-file (write-only: the public half is enough; the private half must stay offline)");
        using (var oChk = JsonDocument.Parse(Encoding.UTF8.GetBytes(sSecretJson)))
        {
            SchemaCheck(oChk.RootElement.EnumerateObject().Select(p => p.Name), rsSecKeys, "escrow secret payload");
            if (oChk.RootElement.TryGetProperty("verify", out var eVer))
                SchemaCheck(eVer.EnumerateObject().Select(p => p.Name), rsVerifyKeys, "escrow verify block");
            if (oChk.RootElement.TryGetProperty("scrub", out var eScr))
                SchemaCheck(eScr.EnumerateObject().Select(p => p.Name), rsScrubKeys, "escrow scrub block");
        }
        using var rsa = EscrowKey.LoadPublic(sPubKeyPath);
        string sPubFp = EscrowKey.Fingerprint(rsa);
        string sHeader = MakeHeader(sArtifact, sIsa, sCarrierFmt, sWidthCode, sSwitches, sWrapAlg, sPubFp);
        byte[] rgAad = Encoding.UTF8.GetBytes(sHeader);
        byte[] rgDek = Rnd(32), rgNonce = Rnd(12);
        byte[] rgSec = Encoding.UTF8.GetBytes(sSecretJson);
        byte[] rgPt = new byte[rgAad.Length + rgSec.Length];
        Array.Copy(rgAad, 0, rgPt, 0, rgAad.Length);
        Array.Copy(rgSec, 0, rgPt, rgAad.Length, rgSec.Length);
        byte[] rgCt = new byte[rgPt.Length], rgTag = new byte[16];
        using (var aes = NewAesGcm(rgDek)) aes.Encrypt(rgNonce, rgPt, rgCt, rgTag);
        byte[] rgEpk = EscrowKey.Wrap(rsa, rgDek);
        File.WriteAllText(sPath, sHeader + "\n" + ArmorLines("LAMARR WRAP", rgEpk) + ArmorLines("LAMARR AEAD", Concat(rgNonce, rgTag, rgCt)), new UTF8Encoding(false));
        Array.Clear(rgDek, 0, rgDek.Length);
        Array.Clear(rgPt, 0, rgPt.Length);
        Console.WriteLine($"[escrow] {sPath} (armored v{iEscrowVer}, isa={sIsa}, pubFp={sPubFp}, ct={rgCt.Length}B)");
    }

    internal static void EscrowWriteDiag(string sPath, string sArtifact, string sIsa, string sCarrierFmt, string sWidthCode, string sSwitches, string sDiagJson = "")//诊断件只有公开头与诊断字段 没有封块也没有verify 既不能重放也不能解包
    {
        File.WriteAllText(sPath, MakeHeader(sArtifact, sIsa, sCarrierFmt, sWidthCode, sSwitches, "none", "-")
            + (sDiagJson.Length == 0 ? "" : "\ndiag: " + sDiagJson), new UTF8Encoding(false));
        Console.WriteLine($"[escrow] {sPath} (diag only, isa={sIsa})");
    }

    internal static string EscrowOpen(string sPath, string? sPrivKeyPath)
    {
        string[] rgLines = File.ReadAllText(sPath).Replace("\r", "").Split('\n');
        if (rgLines.Length == 0 || !rgLines[0].StartsWith("LAMARR-ESCROW ", StringComparison.Ordinal))
            throw new InvalidOperationException("not a Lamarr escrow file (missing the LAMARR-ESCROW magic)");
        string[] rgMagic = rgLines[0].Split(' ');
        if (rgMagic.Length < 2 || rgMagic[1] != iEscrowVer.ToString())
            throw new InvalidOperationException($"escrow version '{rgLines[0]}' != LAMARR-ESCROW {iEscrowVer}: regenerate the escrow");

        var oHdr = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 1;
        for (; i < rgLines.Length; i++)
        {
            if (rgLines[i].Length == 0) break;
            int iCol = rgLines[i].IndexOf(": ", StringComparison.Ordinal);
            if (iCol <= 0) throw new InvalidOperationException("malformed escrow header line: " + rgLines[i]);
            oHdr[rgLines[i][..iCol]] = rgLines[i][(iCol + 2)..];
        }
        SchemaCheck(oHdr.Keys, rsHdrKeys, "escrow header");
        CheckPackFormat(oHdr);//格式修订号硬比对 用错版本重放会悄悄复述出另一份字节 必须在这里拦住
        if (!oHdr.TryGetValue("wrap", out string? sWrap))//必需头部字段缺失时给清晰报错 不要直接索引抛KeyNotFound
            throw new InvalidOperationException("escrow header has no 'wrap' field (malformed/truncated file): regenerate the escrow");
        if (sWrap == "none")
            throw new InvalidOperationException("this escrow is diag-only (no sealed blocks): it cannot replay or unpack anything");
        if (sWrap != sWrapAlg)
            throw new InvalidOperationException("unknown escrow wrap algorithm: " + sWrap);
        if (string.IsNullOrEmpty(sPrivKeyPath))
            throw new InvalidOperationException("reading an escrow needs --escrow-key-file (the private half stays offline; build machines only ever need the public half)");

        var sbAad = new StringBuilder();//认证数据是头部原样字节含magic行 改任何头部字节都会解密失败
        for (int k = 0; k < i; k++) sbAad.Append(rgLines[k]).Append('\n');
        byte[] rgAad = Encoding.UTF8.GetBytes(sbAad.ToString());

        byte[] rgEpk = DearmorLines(rgLines, "LAMARR WRAP", i);
        byte[] rgBlob = DearmorLines(rgLines, "LAMARR AEAD", i);
        if (rgBlob.Length < 28) throw new InvalidOperationException("escrow AEAD block is truncated");
        byte[] rgNonce = rgBlob[..12], rgTag = rgBlob[12..28], rgCt = rgBlob[28..];

        using var rsa = EscrowKey.LoadPrivate(sPrivKeyPath!);
        string sFpNow = EscrowKey.Fingerprint(rsa);
        if (!oHdr.TryGetValue("pubFp", out string? sFpFile))//pubFp缺失也要清晰报错
            throw new InvalidOperationException("escrow header has no 'pubFp' field (malformed/truncated file): regenerate the escrow");
        if (sFpNow != sFpFile)
            throw new InvalidOperationException($"escrow was written with a different key pair (file pubFp={sFpFile}, this private key fp={sFpNow})");

        byte[] rgDek = EscrowKey.Unwrap(rsa, rgEpk);
        byte[] rgPt = new byte[rgCt.Length];
        try
        {
            using var aes = NewAesGcm(rgDek);
            aes.Decrypt(rgNonce, rgCt, rgTag, rgPt);
        }
        catch (CryptographicException e)
        {
            Array.Clear(rgDek, 0, rgDek.Length);
            Array.Clear(rgPt, 0, rgPt.Length);
            throw new InvalidOperationException("escrow failed AEAD verification (tampered header/ciphertext, or wrong key)", e);
        }
        Array.Clear(rgDek, 0, rgDek.Length);
        if (rgPt.Length < rgAad.Length)
        {
            Array.Clear(rgPt, 0, rgPt.Length);
            throw new InvalidOperationException("escrow blob is shorter than its authenticated header");
        }
        for (int k = 0; k < rgAad.Length; k++)
        {
            if (rgPt[k] == rgAad[k]) continue;
            Array.Clear(rgPt, 0, rgPt.Length);
            throw new InvalidOperationException("escrow header/authenticated prefix mismatch (the header was edited)");
        }
        byte[] rgSec = rgPt[rgAad.Length..];
        using var oSec = JsonDocument.Parse(rgSec);
        SchemaCheck(oSec.RootElement.EnumerateObject().Select(p => p.Name), rsSecKeys, "escrow secret payload");
        if (oSec.RootElement.TryGetProperty("verify", out var eVer2))
            SchemaCheck(eVer2.EnumerateObject().Select(p => p.Name), rsVerifyKeys, "escrow verify block");
        if (oSec.RootElement.TryGetProperty("scrub", out var eScr2))
            SchemaCheck(eScr2.EnumerateObject().Select(p => p.Name), rsScrubKeys, "escrow scrub block");
        string? sNoAnchor = CheckEscrowSig(oSec.RootElement);//来源自洽 无锚就返回原因并告警 有锚但被改就抛
        if (sNoAnchor != null) Console.WriteLine("[escrow] WARN " + sNoAnchor);

        var sb = new StringBuilder("{");
        bool bFirst = true;
        void Add(string sName, string sVal)
        {
            if (!bFirst) sb.Append(',');
            bFirst = false;
            sb.Append(JsonSerializer.Serialize(sName)).Append(':').Append(sVal);
        }
        foreach (var kv in oHdr) Add(kv.Key, JsonSerializer.Serialize(kv.Value));
        foreach (var p in oSec.RootElement.EnumerateObject())
            Add(p.Name, p.Value.ValueKind == JsonValueKind.String ? JsonSerializer.Serialize(p.Value.GetString()!) : p.Value.GetRawText());
        sb.Append('}');
        Array.Clear(rgPt, 0, rgPt.Length);
        return sb.ToString();
    }

    internal static string ReadEscrowJson(string sPath, string? sPrivKeyPath) => EscrowOpen(sPath, sPrivKeyPath);

    static readonly JsonSerializerOptions sEscrowJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string EscrowSwitches(bool bNoMut, bool bMoveInj, bool bNoRen, string sMethodDict, bool bCompDeps, string sEncDeps, string sNoCompDeps)
        => $"noMutate={(bNoMut ? 1 : 0)};moveInject={(bMoveInj ? 1 : 0)};noRename={(bNoRen ? 1 : 0)}"
           + $";methodDict={Path.GetFileName(sMethodDict)};compressDeps={(bCompDeps ? 1 : 0)}"
           + $";encryptDeps={sEncDeps};noCompressDeps={sNoCompDeps}";

    internal static string? EscrowVerifyMismatch(string sEscJson, string sPayloadFp, string sMode, string sNativeFp)//重放侧与打包侧用同一算路比对 两侧都调它
    {
        using var o = JsonDocument.Parse(sEscJson);
        if (!o.RootElement.TryGetProperty("verify", out var e)) return "escrow has no verify block";
        string G(string sK) => e.TryGetProperty(sK, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        if (G("payload") != sPayloadFp) return $"payload fingerprint mismatch (escrow={G("payload")}, now={sPayloadFp})";
        if (G("mode") != sMode) return $"mode mismatch (escrow={G("mode")}, now={sMode})";
        if (G("native") != sNativeFp) return $"native-input fingerprint mismatch (escrow={G("native")}, now={sNativeFp})";
        return null;
    }

    string DiagJson(uint uRollKey)//诊断字段的唯一去处 重放只读vmSeed与seed与head与pubB与sig与sigEscrow与verify 解包只读seed
    {
        byte[] rgSeedKey = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        var oDiag = new Dictionary<string, object?>
        {
            ["sigma"] = OpTable.SigmaHex,
            ["sigmaSeed"] = OpTable.LastSeed.ToString("X16"),
            ["sigmaIsa"] = OpTable.LastIsa,
            ["sigmaDerived"] = OpTable.LastDerived,
            ["sigmaGroups"] = OpTable.GroupMask.Length == 0 ? "(all)" : OpTable.GroupMask,
            ["seedKey"] = Convert.ToHexString(rgSeedKey),
            ["adj"] = MixAdj(rgSeedKey).ToString("X8"),
            ["rollKey"] = uRollKey.ToString("X8"),
            ["engineSum"] = uEngineSum.ToString("X8"),
            ["rollLen"] = rgRoll?.Length ?? 0,
        };
        Array.Clear(rgSeedKey, 0, rgSeedKey.Length);
        return JsonSerializer.Serialize(oDiag, sEscrowJson);
    }

    internal void WriteEscrow(string sPath, uint uRollKey, string sArtifact, byte[]? rgSealPubB = null, byte[]? rgSealSig = null)//写escrow 只写重放与解包所需的东西 没有K也没有签名私钥
    {
        string sIsa = IsaPool.CarrierIsa(uVmSeed, 0);
        string sCarrierFmt = CarrierFmt.VerFor(sIsa).ToString();
        string sWidthCode = $"{CarrierFmt.Code[0]},{CarrierFmt.Code[1]},{CarrierFmt.Code[2]},{CarrierFmt.Code[3]}";
        string sSwitches = EscrowSwitches(bNoMutate, bMoveInject, bNoRename, sMethodDictPath, bCompressDeps,
            string.Join(",", rgEncryptDeps), string.Join(",", rgNoCompressDeps));
        if (bEscrowDiag)
        {
            EscrowWriteDiag(sPath, sArtifact, sIsa, sCarrierFmt, sWidthCode, sSwitches, DiagJson(uRollKey));
            return;
        }
        var oSec = new Dictionary<string, object?>//密封区只留8项加sigEscrow 含重放要的材料与解包要的seed与输入比对verify与清痕逆材料scrub
        {
            ["vmSeed"] = uVmSeed.ToString("X16"),
            ["seed"] = sSeed,
            ["head"] = Convert.ToHexString(rgHead),
            ["pubB"] = rgSealPubB != null ? Convert.ToHexString(rgSealPubB) : "",
            ["sig"] = rgSealSig != null ? Convert.ToHexString(rgSealSig) : "",
            ["verify"] = new Dictionary<string, object?>
            {
                ["payload"] = sVerifyPayload,
                ["mode"] = sVerifyMode,
                ["native"] = sVerifyNative,
            },
        };
        string sScrub = ScrubJson();//把清痕前原字节放进密封区 无材料或重放复制为空时不写
        if (sScrub.Length > 0) oSec["scrub"] = JsonDocument.Parse(sScrub).RootElement.Clone();
        if (sReplayEscrowSig.Length == 128 || rgSealPubB != null)//给密封区加来源签名 与产物签名锚同一把钥 没有成品seal时不加该字段 重放时复制源escrow的成品签名
            oSec[sEscrowSigField] = EscrowSign(rkBuildKey, oSec, sReplayEscrowSig);
        EscrowSeal(sPath, sEscrowPubKeyPath, sArtifact, sIsa, sCarrierFmt, sWidthCode, sSwitches, JsonSerializer.Serialize(oSec, sEscrowJson));
    }

    internal static int EscrowSelfTest(string sDir)//离线自检装甲信封与只干两件事的不变量
    {
        Directory.CreateDirectory(sDir);
        int iBad = 0;
        void Chk(string sName, bool bOk) { Console.WriteLine("  " + (bOk ? "ok   " : "FAIL ") + sName); if (!bOk) iBad++; }
        bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }

        string Splice(string sPath, string sLabel, Func<byte[], byte[]> fT)
        {
            string[] rgL = File.ReadAllText(sPath).Replace("\r", "").Split('\n');
            int iB = Array.IndexOf(rgL, "-----BEGIN " + sLabel + "-----");
            int iE = Array.IndexOf(rgL, "-----END " + sLabel + "-----");
            var sbB = new StringBuilder();
            for (int k = iB + 1; k < iE; k++) sbB.Append(rgL[k]);
            byte[] rgNew = fT(Convert.FromBase64String(sbB.ToString()));
            var rgOut = new List<string>(rgL);
            rgOut.RemoveRange(iB, iE - iB + 1);
            rgOut.Insert(iB, ArmorLines(sLabel, rgNew).TrimEnd('\n'));
            File.WriteAllText(sPath, string.Join("\n", rgOut));
            return sPath;
        }

        string sPriv = Path.Combine(sDir, "publisher.key"), sPub = Path.Combine(sDir, "publisher.pub");
        foreach (string s in new[] { sPriv, sPub }) if (File.Exists(s)) File.Delete(s);

        Console.WriteLine($"[escrow-kat] armored escrow v{iEscrowVer} (write-only; replay+unpack only)");
        EscrowKey.Keygen(sPriv, sPub);
        Chk("keygen writes both files with type tags",
            File.Exists(sPriv) && File.Exists(sPub)
            && File.ReadAllText(sPriv).StartsWith(EscrowKey.sTagPriv) && File.ReadAllText(sPub).StartsWith(EscrowKey.sTagPub));
        Chk("public file rejected where a private key is needed", Throws(() => EscrowKey.LoadPrivate(sPub)));
        Chk("private file rejected where a CI-safe public key is needed", Throws(() => EscrowKey.LoadPublic(sPriv)));

        string sSw = EscrowSwitches(false, false, false, "m.json", false, "", "");
        string sSec = "{\"vmSeed\":\"0123456789ABCDEF\",\"head\":\"00112233445566778899AABBCCDDEEFF\",\"seed\":\"AABBCCDDEEFF00112233445566778899\",\"verify\":{\"payload\":\"P1\",\"mode\":\"antheil\",\"native\":\"N1\"}}";
        string sE1 = Path.Combine(sDir, "e1.escrow"), sE2 = Path.Combine(sDir, "e2.escrow");
        EscrowSeal(sE1, sPub, "WeaponDamageCalc.exe", "alpha", "1", "4,4,4,4", sSw, sSec);
        string sTxt = File.ReadAllText(sE1);
        Chk("seal succeeds with the public key only (CI-safe)", File.Exists(sE1));
        Chk("file is armored (magic + both blocks)",
            sTxt.StartsWith($"LAMARR-ESCROW {iEscrowVer}\n") && sTxt.Contains("-----BEGIN LAMARR WRAP-----") && sTxt.Contains("-----BEGIN LAMARR AEAD-----"));
        Chk("plaintext header carries only the basic info",
            sTxt.Contains("\ncarrierFmt: 1\n") && !sTxt.Contains("vmSeed"));
        Chk("header records the packer format revision (replay needs the same one)",
            sTxt.Contains($"\npacker: {sFormatId}\n"));
        Chk("an escrow without the packer field is refused",
            Throws(() => CheckPackFormat(new Dictionary<string, string> { ["artifact"] = "WeaponDamageCalc.exe" })));
        Chk("an escrow from a different packer format is refused",
            Throws(() => CheckPackFormat(new Dictionary<string, string> { ["packer"] = "antheil-fmt0" })));
        Chk("the current format passes the check",
            !Throws(() => CheckPackFormat(new Dictionary<string, string> { ["packer"] = sFormatId })));
        Chk("open with the private key returns the material", EscrowOpen(sE1, sPriv).Contains("0123456789ABCDEF"));
        Chk("open without a private key is refused", Throws(() => EscrowOpen(sE1, null)));
        Chk("open with the CI-side (public) key file is refused", Throws(() => EscrowOpen(sE1, sPub)));
        Chk("verify block matches when inputs are unchanged", EscrowVerifyMismatch(EscrowOpen(sE1, sPriv), "P1", "antheil", "N1") == null);
        Chk("verify block catches a different payload", EscrowVerifyMismatch(EscrowOpen(sE1, sPriv), "P2", "antheil", "N1") != null);
        Chk("verify block catches a different native set", EscrowVerifyMismatch(EscrowOpen(sE1, sPriv), "P1", "antheil", "N2") != null);

        byte[] rgKatK = Convert.FromHexString("9F1C7D3A55E0B2846CA1DF7093B5E24C8A7D0F361BE49C5207F3A81D64E5B0C9");//escrow的来源签名sigEscrow 与产物签名锚同一把钥 覆盖规范材料核心
        byte[] rgKatSig = Ed25519.Sign(KdfLabel(rgKatK, "seal:priv")[..32], Sha512.Hash(Encoding.UTF8.GetBytes("kat-body")));
        var oKatSec = new Dictionary<string, object?>
        {
            ["vmSeed"] = "0123456789ABCDEF",
            ["head"] = "00112233445566778899AABBCCDDEEFF",
            ["seed"] = "AABBCCDDEEFF00112233445566778899",
            ["pubB"] = Convert.ToHexString(PubB(rgKatK)),
            ["sig"] = Convert.ToHexString(rgKatSig),
            ["verify"] = new Dictionary<string, object?> { ["payload"] = "P1", ["mode"] = "antheil", ["native"] = "N1" },
        };
        oKatSec[sEscrowSigField] = EscrowSign(rgKatK, oKatSec, "");
        string sE3 = Path.Combine(sDir, "e3.escrow");
        EscrowSeal(sE3, sPub, "WeaponDamageCalc.exe", "alpha", "1", "4,4,4,4", sSw, JsonSerializer.Serialize(oKatSec, sEscrowJson));
        string sOpen3 = EscrowOpen(sE3, sPriv);
        using (var dOk = JsonDocument.Parse(sOpen3))
            Chk("L: sigEscrow is present and verifies under the escrow's own pub_B", !Throws(() => CheckEscrowSig(dOk.RootElement)));
        string sMatBad = sOpen3.Replace("\"0123456789ABCDEF\"", "\"FEDCBA9876543210\"");
        using (var dBad = JsonDocument.Parse(sMatBad))
            Chk("L: editing a material field breaks sigEscrow", Throws(() => CheckEscrowSig(dBad.RootElement)));
        string sSigBad = sOpen3.Replace("\"" + sEscrowSigField + "\":\"", "\"" + sEscrowSigField + "\":\"00");
        using (var dSg = JsonDocument.Parse(sSigBad))
            Chk("L: a forged sigEscrow is rejected", Throws(() => CheckEscrowSig(dSg.RootElement)));
        string sPubBad = sOpen3.Replace("\"pubB\":\"", "\"pubB\":\"00");
        using (var dPb = JsonDocument.Parse(sPubBad))
            Chk("L: a replaced pub_B breaks sigEscrow", Throws(() => CheckEscrowSig(dPb.RootElement)));
        using (var dNo = JsonDocument.Parse(EscrowOpen(sE1, sPriv)))
            Chk("L: an unsealed escrow stays readable but reports 'no provenance anchor' (loud, non-blocking)", CheckEscrowSig(dNo.RootElement) != null);

        string sTam = Path.Combine(sDir, "tamper.escrow");
        File.Copy(sE1, sTam, true);
        Splice(sTam, "LAMARR AEAD", rg => { rg[40] ^= 1; return rg; });
        Chk("tampered ciphertext is rejected", Throws(() => EscrowOpen(sTam, sPriv)));

        EscrowSeal(sE2, sPub, "WeaponDamageCalc.exe", "alpha", "1", "4,4,4,4", sSw, sSec);
        string sSwp = Path.Combine(sDir, "swap.escrow");
        File.Copy(sE1, sSwp, true);
        string sW2 = File.ReadAllText(sE2);
        int iWb = sW2.IndexOf("-----BEGIN LAMARR WRAP-----") + "-----BEGIN LAMARR WRAP-----".Length + 1;
        int iWe = sW2.IndexOf("-----END LAMARR WRAP-----");
        byte[] rgWrap2 = Convert.FromBase64String(sW2[iWb..iWe].Replace("\n", ""));
        Splice(sSwp, "LAMARR WRAP", _ => rgWrap2);
        Chk("swapped key wrap is rejected", Throws(() => EscrowOpen(sSwp, sPriv)));

        string sHdr = Path.Combine(sDir, "header.escrow");
        File.Copy(sE1, sHdr, true);
        File.WriteAllText(sHdr, File.ReadAllText(sHdr).Replace("isa: alpha", "isa: ppc"));
        Chk("header tamper is rejected (header is authenticated)", Throws(() => EscrowOpen(sHdr, sPriv)));
        Chk("two seals of the same material differ (random DEK/nonce)", File.ReadAllText(sE1) != File.ReadAllText(sE2));

        string sDiag = Path.Combine(sDir, "diag.escrow");
        EscrowWriteDiag(sDiag, "WeaponDamageCalc.exe", "alpha", "1", "4,4,4,4", sSw);
        Chk("diag file has no sealed blocks and cannot be opened", Throws(() => EscrowOpen(sDiag, sPriv)));
        Chk("sealing without a public key file is refused",
            Throws(() => EscrowSeal(Path.Combine(sDir, "nopub.escrow"), "", "a.exe", "alpha", "1", "4,4,4,4", sSw, sSec)));

        Chk("schema: secret payload whitelist rejects a private-key-ish field",
            Throws(() => EscrowSeal(Path.Combine(sDir, "sch1.escrow"), sPub, "a.exe", "alpha", "1", "4,4,4,4", sSw, "{\"sealPriv\":\"DEADBEEF\"}")));
        Chk("schema: verify whitelist rejects an unknown key",
            Throws(() => EscrowSeal(Path.Combine(sDir, "sch2.escrow"), sPub, "a.exe", "alpha", "1", "4,4,4,4", sSw, "{\"verify\":{\"payload\":\"P\",\"whoops\":\"x\"}}")));
        Chk("schema: scrub whitelist rejects an unknown key",
            Throws(() => EscrowSeal(Path.Combine(sDir, "sch3.escrow"), sPub, "a.exe", "alpha", "1", "4,4,4,4", sSw, "{\"scrub\":{\"whoops\":\"x\"}}")));
        Chk("schema: scrub whitelist rejects an unknown entry-level key",
            Throws(() => EscrowSeal(Path.Combine(sDir, "sch4.escrow"), sPub, "a.exe", "alpha", "1", "4,4,4,4", sSw,
                "{\"scrub\":{\"fmt\":\"pe-scrub-1\",\"entries\":{},\"artifact\":\"\",\"whoops\":\"x\"}}")));
        Chk("schema: a scrub block of the current shape is accepted",
            !Throws(() => EscrowSeal(Path.Combine(sDir, "sch5.escrow"), sPub, "a.exe", "alpha", "1", "4,4,4,4", sSw,
                "{\"scrub\":{\"fmt\":\"pe-scrub-1\",\"entries\":{\"a.dll\":\"00\"},\"artifact\":\"11\"}}")));
        Chk("schema: header whitelist rejects an unknown key",
            Throws(() => SchemaCheck(new[] { "artifact", "sealPriv" }, rsHdrKeys, "header")));

        string sV2 = Path.Combine(sDir, "v2.escrow");
        File.WriteAllText(sV2, "LAMARR-ESCROW 2\nartifact: x\n");
        Chk("old/foreign version magic is refused", Throws(() => EscrowOpen(sV2, sPriv)));
        string sNot = Path.Combine(sDir, "not.escrow");
        File.WriteAllText(sNot, "{\"ver\":2,\"mode\":\"wrapped\"}\n");
        Chk("a non-escrow file is refused", Throws(() => EscrowOpen(sNot, sPriv)));

        Console.WriteLine(iBad == 0 ? "[escrow-kat] RESULT PASS" : $"[escrow-kat] RESULT FAIL ({iBad} checks)");
        return iBad == 0 ? 0 : 2;
    }
}

