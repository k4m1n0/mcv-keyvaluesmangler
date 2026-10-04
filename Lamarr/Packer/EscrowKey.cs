namespace Lamarr.Packer;

using System.Security.Cryptography;
using System.Text;

//escrow v2的收件密钥 取RSA-OAEP-SHA256而非ECDH 因为它跨目标框架行为一致 十年后还能离线恢复
//密钥文件带类型标签 写escrow只接受LAMARR-PUB 避免把私钥带进构建机这类事故静默发生
internal static class EscrowKey
{
    internal const string sTagPriv = "LAMARR-PRIV";
    internal const string sTagPub = "LAMARR-PUB";
    internal const string sVersion = "1";//格式版本必须校验 否则将来的新格式文件会被静默当成本版解析
    internal const int iBits = 3072;

    static (string sKind, string sPem) Split(string sPath)
    {
        string[] rg = File.ReadAllLines(sPath);
        int i = 0;
        while (i < rg.Length && rg[i].Trim().Length == 0) i++;
        if (i >= rg.Length) throw new InvalidOperationException($"key file is empty: {sPath}");
        string[] rgTag = rg[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rgTag.Length < 2 || (rgTag[0] != sTagPriv && rgTag[0] != sTagPub))
            throw new InvalidOperationException($"not a Lamarr key file (missing {sTagPriv}/{sTagPub} tag): {sPath}");
        if (rgTag[1] != sVersion)
            throw new InvalidOperationException($"unsupported key format version '{rg[i].Trim()}' (this build understands {rgTag[0]} {sVersion}): {sPath}");
        var sb = new StringBuilder();
        for (int k = i + 1; k < rg.Length; k++) sb.AppendLine(rg[k]);
        return (rgTag[0], sb.ToString());
    }

    static (string sKind, string sPem) Require(string sPath, string sWant, string sWhat)
    {
        var t = Split(sPath);
        if (t.sKind != sWant)
            throw new InvalidOperationException($"{sWhat} needs a {sWant} key file, but {sPath} is {t.sKind} (key files are type tagged so this cannot pass silently)");
        return t;
    }

    internal static RSA LoadPublic(string sPath)
    {
        var t = Require(sPath, sTagPub, "writing an escrow");
        var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Dearmor(t.sPem), out _);
        return rsa;
    }

    internal static RSA LoadPrivate(string sPath)
    {
        var t = Require(sPath, sTagPriv, "reading an escrow");
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Dearmor(t.sPem), out _);
        try { rsa.ExportParameters(true); }
        catch (CryptographicException) { rsa.Dispose(); throw new InvalidOperationException($"private key file has no private half: {sPath}"); }
        return rsa;
    }

    internal static string Fingerprint(RSA rsa)
        => Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))[..16];

    static string Armor(string sLabel, byte[] rgDer)//手工PEM 不按目标框架分叉 否则不同框架构建的打包器写出的escrow互不兼容
    {
        string sB64 = Convert.ToBase64String(rgDer);
        var sb = new StringBuilder();
        sb.Append("-----BEGIN ").Append(sLabel).Append("-----\n");
        for (int i = 0; i < sB64.Length; i += 64)
            sb.Append(sB64, i, Math.Min(64, sB64.Length - i)).Append('\n');
        sb.Append("-----END ").Append(sLabel).Append("-----\n");
        return sb.ToString();
    }

    static byte[] Dearmor(string sPem)
    {
        var sb = new StringBuilder();
        foreach (string sLine in sPem.Split('\n'))
        {
            string s = sLine.Trim();
            if (s.Length == 0 || s.StartsWith("-----", StringComparison.Ordinal)) continue;
            sb.Append(s);
        }
        return Convert.FromBase64String(sb.ToString());
    }

    internal static void Keygen(string sPrivPath, string sPubPath)
    {
        if (File.Exists(sPrivPath) || File.Exists(sPubPath))
            throw new InvalidOperationException($"refusing to overwrite an existing key file ({sPrivPath} / {sPubPath})");
        using var rsa = RSA.Create(iBits);
        File.WriteAllText(sPrivPath, sTagPriv + " " + sVersion + "\n" + Armor("PRIVATE KEY", rsa.ExportPkcs8PrivateKey()), new UTF8Encoding(false));
        File.WriteAllText(sPubPath, sTagPub + " " + sVersion + "\n" + Armor("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()), new UTF8Encoding(false));
        Console.WriteLine($"[escrow-key] private -> {sPrivPath}  (KEEP OFFLINE: never on a build machine / in CI)");
        Console.WriteLine($"[escrow-key] public  -> {sPubPath}  (safe on build machines: write-only escrow)");
        Console.WriteLine($"[escrow-key] bits={rsa.KeySize} fp={Fingerprint(rsa)}");
        Console.WriteLine("[escrow-key] hint: icacls \"<priv>\" /inheritance:r /grant:r \"%USERNAME%:R\"");
    }

    internal static void Info(string sPath)
    {
        using var rsa = ReadAny(sPath, out bool bPriv);
        Console.WriteLine($"[escrow-key] kind={(bPriv ? "PRIVATE (can read escrows)" : "PUBLIC (write-only escrow)")} bits={rsa.KeySize} fp={Fingerprint(rsa)} path={sPath}");
    }

    static RSA ReadAny(string sPath, out bool bPriv)
    {
        var t = Split(sPath);
        var rsa = RSA.Create();
        byte[] rgDer = Dearmor(t.sPem);
        if (t.sKind == sTagPriv) { bPriv = true; rsa.ImportPkcs8PrivateKey(rgDer, out _); }
        else { bPriv = false; rsa.ImportSubjectPublicKeyInfo(rgDer, out _); }
        return rsa;
    }

    internal static byte[] Wrap(RSA rsaPub, byte[] rgDek) => rsaPub.Encrypt(rgDek, RSAEncryptionPadding.OaepSHA256);

    internal static byte[] Unwrap(RSA rsaPriv, byte[] rgEpk)
    {
        try { return rsaPriv.Decrypt(rgEpk, RSAEncryptionPadding.OaepSHA256); }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException("escrow key unwrap failed: wrong private key file, or the blob was tampered with", e);
        }
    }
}
