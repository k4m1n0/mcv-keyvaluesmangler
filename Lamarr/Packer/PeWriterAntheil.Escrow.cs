namespace Lamarr.Packer;

using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

internal partial class PeWriterAntheil
{
    const int iEscrowVer = 1;

    internal static string EscrowPass() => Environment.GetEnvironmentVariable("LAMARR_ESCROW_PASS") ?? "";

    static AesGcm NewAesGcm(byte[] rgKey)//Poly1305用16字节标签 两条分支写出的密文必须相同
    {
#if NET8_0_OR_GREATER
        return new AesGcm(rgKey, 16);
#else
        return new AesGcm(rgKey);
#endif
    }

    byte[] SeedDerivedBytes(string sTag, int iCount)//盐与nonce由运行种子派生 同种子两次写出逐字节相同
    {
        byte[] rg = new byte[iCount];
        Z0SeedRng(uVmSeed, sTag).NextBytes(rg);
        return rg;
    }

    static byte[] Pbkdf2Key(byte[] rgPass, byte[] rgSalt, int iIters, int iLen)//低版本分支用同一套PBKDF2-HMAC-SHA256 两版escrow互相可读
    {
#if NET6_0_OR_GREATER
        return Rfc2898DeriveBytes.Pbkdf2(rgPass, rgSalt, iIters, HashAlgorithmName.SHA256, iLen);
#else
        using var kdf = new Rfc2898DeriveBytes(rgPass, rgSalt, iIters, HashAlgorithmName.SHA256);
        return kdf.GetBytes(iLen);
#endif
    }

    internal void WriteEscrow(string sPath, uint uRollKey)//escrow=发布者钥匙串与配方 写全模式/ISA/材料输入/实际键形状
    {
        string sIsa = IsaPool.CarrierIsa(uVmSeed, 0);
        byte[] rgSeedKey = SeedKey(Encoding.ASCII.GetBytes(sSeed));
        var oKey = new Dictionary<string, object?>
        {
            ["vmSeed"] = uVmSeed.ToString("X16"),
            ["sigma"] = OpTable.SigmaHex,//操作码置换与布局置换从vmSeed派生 留档以复现这两张表
            ["sigmaSeed"] = OpTable.LastSeed.ToString("X16"),
            ["sigmaIsa"] = OpTable.LastIsa,
            ["sigmaDerived"] = OpTable.LastDerived,
            ["sigmaGroups"] = OpTable.GroupMask.Length == 0 ? "(all)" : OpTable.GroupMask,
            ["seed"] = sSeed,
            ["head"] = Convert.ToHexString(rgHead),
            ["seedKey"] = Convert.ToHexString(rgSeedKey),
            ["adj"] = MixAdj(rgSeedKey).ToString("X8"),
            ["rollKey"] = uRollKey.ToString("X8"),
            ["engineSum"] = uEngineSum.ToString("X8"),//用载入时固化的.text摘要 rgEngine已就地加密 现算只得FNV初值
            ["rollLen"] = rgRoll?.Length ?? 0,
        };
        Array.Clear(rgSeedKey, 0, rgSeedKey.Length);
        var oDoc = new Dictionary<string, object?>
        {
            ["ver"] = iEscrowVer,
            ["mode"] = bEscrowPlain ? "plain" : "pass",
            ["isa"] = sIsa,
            ["carrierFmt"] = CarrierFmt.VerFor(sIsa),//载体宽度编码随宽度码表逐载体推进 写该ISA的实际编码 不是全局Ver
            ["widthCode"] = $"{CarrierFmt.Code[0]},{CarrierFmt.Code[1]},{CarrierFmt.Code[2]},{CarrierFmt.Code[3]}",
            ["switches"] = new Dictionary<string, object?>
            {
                ["noMutate"] = bNoMutate,
                ["moveInject"] = bMoveInject,
                ["noRename"] = bNoRename,
                ["methodDict"] = Path.GetFileName(sMethodDictPath),
                ["compressDeps"] = bCompressDeps,
                ["encryptDeps"] = string.Join(",", rgEncryptDeps),
                ["noCompressDeps"] = string.Join(",", rgNoCompressDeps),
            },
        };
        if (bEscrowPlain)
        {
            foreach (var kv in oKey) oDoc[kv.Key] = kv.Value;
        }
        else
        {
            string sPass = EscrowPass();
            if (sPass.Length < 8)
                throw new InvalidOperationException("escrow(B) needs the LAMARR_ESCROW_PASS environment variable (at least 8 characters)");
            string sSecret = JsonSerializer.Serialize(oKey);
            byte[] rgSalt = SeedDerivedBytes("escrow-salt", 16);
            byte[] rgKey = Pbkdf2Key(Encoding.UTF8.GetBytes(sPass), rgSalt, 200000, 32);
            byte[] rgNonce = SeedDerivedBytes("escrow-nonce", 12);
            byte[] rgPt = Encoding.UTF8.GetBytes(sSecret);
            byte[] rgCt = new byte[rgPt.Length];
            byte[] rgTag = new byte[16];
            using var aes = NewAesGcm(rgKey);
            aes.Encrypt(rgNonce, rgPt, rgCt, rgTag);
            byte[] rgBlob = new byte[16 + 12 + 16 + rgCt.Length];
            Array.Copy(rgSalt, 0, rgBlob, 0, 16);
            Array.Copy(rgNonce, 0, rgBlob, 16, 12);
            Array.Copy(rgTag, 0, rgBlob, 28, 16);
            Array.Copy(rgCt, 0, rgBlob, 44, rgCt.Length);
            oDoc["blob"] = Convert.ToBase64String(rgBlob);
            Array.Clear(rgKey, 0, rgKey.Length);
            Array.Clear(rgPt, 0, rgPt.Length);
        }
        File.WriteAllText(sPath, JsonSerializer.Serialize(oDoc, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"[escrow] {sPath} (mode={(bEscrowPlain ? "plain" : "pass")}, isa={sIsa}, head={Convert.ToHexString(rgHead, 0, 8)}...)");
    }

    internal static string ReadEscrowJson(string sPath)//读取escrow返回材料JSON 开启pass时先解密
    {
        var oDoc = JsonDocument.Parse(File.ReadAllText(sPath)).RootElement;
        if (oDoc.GetProperty("mode").GetString() == "plain") return oDoc.GetRawText();
        string sPass = EscrowPass();
        if (sPass.Length < 8)
            throw new InvalidOperationException("reading escrow(B) needs the LAMARR_ESCROW_PASS environment variable (at least 8 characters)");
        byte[] rgBlob = Convert.FromBase64String(oDoc.GetProperty("blob").GetString()!);
        if (rgBlob.Length < 44) throw new InvalidOperationException("escrow blob truncated");
        byte[] rgKey = Pbkdf2Key(Encoding.UTF8.GetBytes(sPass), rgBlob[0..16], 200000, 32);
        byte[] rgPt = new byte[rgBlob.Length - 44];
        using var aes = NewAesGcm(rgKey);
        aes.Decrypt(rgBlob[16..28], rgBlob[44..], rgBlob[28..44], rgPt);
        Array.Clear(rgKey, 0, rgKey.Length);
        string sJson = Encoding.UTF8.GetString(rgPt);
        Array.Clear(rgPt, 0, rgPt.Length);
        return sJson;
    }
}
