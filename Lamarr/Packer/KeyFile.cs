namespace Lamarr.Packer;

using System.Text;

//读根密钥K的钥匙文件 带标签的64hex与裸hex都接受 其余一律报错
internal static class KeyFile
{
    internal const string sTag = "LAMARR-KEY";
    internal const string sVersion = "1";//格式版本必须校验 否则新格式的文件会被静默当成本版解析

    internal static void Keygen(string sPath)
    {
        if (File.Exists(sPath)) throw new InvalidOperationException("refusing to overwrite an existing key file: " + sPath);
        byte[] rg = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(rg);
        File.WriteAllText(sPath, sTag + " " + sVersion + "\n" + Convert.ToHexString(rg) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"[key] wrote {sPath} (32B random build key)");
        Console.WriteLine("[key] KEEP OFFLINE: whoever holds it can rebuild *and* re-sign that product line");
        Console.WriteLine($"[key] fp={Sha512.Fp(rg)}");
        Console.WriteLine("[key] hint: icacls \"<file>\" /inheritance:r /grant:r \"%USERNAME%:R\"");
        Array.Clear(rg, 0, rg.Length);
    }

    internal static byte[] Load(string sPath)
    {
        string[] rgL = File.ReadAllLines(sPath);
        int i = 0;
        while (i < rgL.Length && rgL[i].Trim().Length == 0) i++;
        if (i >= rgL.Length) throw new InvalidOperationException("key file is empty: " + sPath);
        string s0 = rgL[i].Trim();
        string sHex;
        if (s0.StartsWith(sTag, StringComparison.Ordinal))
        {
            string[] rgHdr = s0.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rgHdr.Length < 2 || rgHdr[1] != sVersion)
                throw new InvalidOperationException($"unsupported key format version '{s0}' (this build understands {sTag} {sVersion}): " + sPath);
            if (i + 1 >= rgL.Length) throw new InvalidOperationException("key file has no key material: " + sPath);
            sHex = rgL[i + 1].Trim();
        }
        else sHex = s0;//未带标签的裸hex也接受
        sHex = sHex.Replace("-", "").Replace(" ", "").Replace("0x", "").Replace("0X", "");
        byte[] rg;
        try { rg = Convert.FromHexString(sHex); }
        catch (Exception e) { throw new InvalidOperationException($"key file is not hex ({sPath}): {e.Message}"); }
        if (rg.Length != 32) throw new InvalidOperationException($"key file must hold a 32-byte key (64 hex chars): {sPath}");
        return rg;
    }

    internal static void Info(string sPath)
    {
        byte[] rg = Load(sPath);
        Console.WriteLine($"[key] kind={sTag} bytes={rg.Length} fp={Sha512.Fp(rg)} path={sPath}");
        Array.Clear(rg, 0, rg.Length);
    }
}
