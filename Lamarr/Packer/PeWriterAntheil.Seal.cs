namespace Lamarr.Packer;

using System.Text;

//产物侧签名锚 seal私钥按K派生seal:priv 确定性且永不落盘 pub_B是它的公钥 sig覆盖body
//LAMARR_NOSEAL只在triage构建里有分支 发行打包器恒bSealOn=true 环境变量关不掉签名锚
internal partial class PeWriterAntheil
{
#if LAMARR_TRIAGE
    internal static readonly bool bSealOn = Environment.GetEnvironmentVariable("LAMARR_NOSEAL") != "1";
#else
    internal static readonly bool bSealOn = true;//发行构建不读LAMARR_NOSEAL
#endif
    const int iSealSize = 104;          //4 magic + 2 ver + 2 flags + 32 pubB + 64 sig

    internal static byte[] SealMagic(byte[] rgPubB) => Sha512.Hash(rgPubB)[..4];//标记逐产物派生 不用静态魔数以免成为全网可扫描特征

    internal static byte[] PubB(byte[] rgK) => Ed25519.PublicKey(KdfLabel(rgK, "seal:priv")[..32]);

    private byte[] rkSealPubB = Array.Empty<byte>();//重放用成品seal 直接复制不重签 因此重放不需要K
    private byte[] rkSealSig = Array.Empty<byte>();
    public void SetReplaySeal(byte[] rgPubB, byte[] rgSig) { rkSealPubB = rgPubB; rkSealSig = rgSig; }
    internal bool HasSeal => bSealOn && (rkSealPubB.Length == 32 || rkBuildKey.Length == 32);
    internal byte[] SealPubB() => rkSealPubB.Length == 32 ? rkSealPubB : PubB(rkBuildKey);
    internal byte[] SealSig(byte[] rgBody) => rkSealSig.Length == 64 ? rkSealSig : Ed25519.Sign(KdfLabel(rkBuildKey, "seal:priv")[..32], Sha512.Hash(rgBody));

    internal static byte[] SealRegionOf(byte[] rgPubB, byte[] rgSig) => SealBuild(rgPubB, rgSig);

    static byte[] SealBuild(byte[] rgPubB, byte[] rgSig)//seal区唯一构造点 两个调用方都走这里以免逐字节重复而漂移
    {
        byte[] rg = new byte[iSealSize];
        SealMagic(rgPubB).CopyTo(rg, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(rg, 4);
        BitConverter.GetBytes((ushort)0).CopyTo(rg, 6);
        rgPubB.CopyTo(rg, 8);
        rgSig.CopyTo(rg, 40);
        return rg;
    }

    internal static uint SealFold(byte[] rgPubB) => BitConverter.ToUInt32(Sha512.Hash(rgPubB), 0);

    internal static byte[] SealBody(byte[] rgAll)//body是产物去掉尾部固定长度seal区
    {
        if (rgAll.Length < iSealSize) throw new InvalidOperationException("artifact is shorter than the seal region");
        return rgAll[..(rgAll.Length - iSealSize)];
    }

    internal static byte[] SealRegion(byte[] rgK, byte[] rgBody)
        => SealBuild(PubB(rgK), Ed25519.Sign(KdfLabel(rgK, "seal:priv")[..32], Sha512.Hash(rgBody)));

    internal static int VerifySealFile(string sExe) => VerifySealFile(sExe, null);

    internal static int VerifySealFile(string sExe, string? sExpect)//靠尾部固定偏移定位 给了--expect-pubb时pub_B必须相等 不等返回6
    {
        byte[] rgAll = File.ReadAllBytes(sExe);
        if (rgAll.Length < iSealSize) { Console.Error.WriteLine("[seal] file is shorter than the seal region"); return 2; }
        byte[] rgSeal = rgAll[^(iSealSize)..];
        byte[] rgPubB = rgSeal[8..40];
        if (!rgSeal[..4].AsSpan().SequenceEqual(SealMagic(rgPubB)))
        {
            Console.Error.WriteLine("[seal] no seal region at the end of this artifact (marker mismatch)");
            return 2;
        }
        int iVer = BitConverter.ToUInt16(rgSeal, 4);
        byte[] rgSig = rgSeal[40..104];
        byte[] rgBody = SealBody(rgAll);
        bool bOk = iVer == 1 && Ed25519.Verify(rgPubB, Sha512.Hash(rgBody), rgSig);
        bool bPin = !string.IsNullOrEmpty(sExpect);
        Console.WriteLine($"[seal] file={sExe} ver={iVer} pubB={Convert.ToHexString(rgPubB)[..16]} body={rgBody.Length}B sig={(bOk ? "VALID" : "INVALID")}{(bPin ? "" : " (unverified: no pinned pub_B => self-consistency only, NOT provenance; pass --expect-pubb)")}");
        if (!bOk) return 2;
        if (!bPin) return 0;
        byte[] rgWant;
        try { rgWant = ParsePubBArg(sExpect!); }
        catch (Exception e) { Console.Error.WriteLine("[seal] bad --expect-pubb: " + e.Message); return 1; }
        if (!rgPubB.AsSpan().SequenceEqual(rgWant))
        {
            Console.Error.WriteLine($"[seal] NOT THE SAME SOURCE: artifact pub_B={Convert.ToHexString(rgPubB)} != pinned {Convert.ToHexString(rgWant)}");
            return 6;
        }
        Console.WriteLine("[seal] SAME SOURCE: pub_B matches the constant you pinned (sig valid under that key)");
        return 0;
    }

    internal static byte[] ParsePubBArg(string sArg)//收64hex或存放hex的文件路径 便于离线时把常量放盘上
    {
        string s = sArg.Trim();
        if (File.Exists(s)) s = File.ReadAllText(s).Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Replace("-", "").Replace(" ", "").Replace("\r", "").Replace("\n", "");
        if (s.Length != 64) throw new InvalidOperationException($"want 64 hex chars (a 32-byte Ed25519 public key), got {s.Length}");
        return Convert.FromHexString(s);
    }

    internal static int SealSelfTest(string sDir)//同K同产物得同一seal 改body或改sig或改pubB都必须验不过
    {
        Directory.CreateDirectory(sDir);
        int iBad = 0;
        void Chk(string sName, bool bOk) { Console.WriteLine("  " + (bOk ? "ok   " : "FAIL ") + sName); if (!bOk) iBad++; }
        byte[] rgK = Convert.FromHexString("9F1C7D3A55E0B2846CA1DF7093B5E24C8A7D0F361BE49C5207F3A81D64E5B0C9");
        byte[] rgBody = Encoding.UTF8.GetBytes("body-of-the-artifact");
        Console.WriteLine("[seal-kat] ed25519 artifact seal (deterministic pub_B from K; private key never lands)");

        byte[] rgR1 = SealRegion(rgK, rgBody);
        byte[] rgR2 = SealRegion(rgK, rgBody);
        Chk("same K + same body => identical seal (pin stays stable)", rgR1.AsSpan().SequenceEqual(rgR2));
        byte[] rgK2 = (byte[])rgK.Clone(); rgK2[0] ^= 1;
        Chk("different K => different pub_B", !SealRegion(rgK2, rgBody).AsSpan(8, 32).SequenceEqual(rgR1.AsSpan(8, 32)));

        byte[] rgPubB = rgR1[8..40], rgSig = rgR1[40..104];
        Chk("verify accepts the untouched body", Ed25519.Verify(rgPubB, Sha512.Hash(rgBody), rgSig));
        byte[] rgBody2 = (byte[])rgBody.Clone(); rgBody2[0] ^= 1;
        Chk("a single flipped body byte is rejected", !Ed25519.Verify(rgPubB, Sha512.Hash(rgBody2), rgSig));
        byte[] rgSig2 = (byte[])rgSig.Clone(); rgSig2[10] ^= 1;
        Chk("a single flipped signature byte is rejected", !Ed25519.Verify(rgPubB, Sha512.Hash(rgBody), rgSig2));
        byte[] rgPubB2 = (byte[])rgPubB.Clone(); rgPubB2[0] ^= 1;
        Chk("a single flipped public-key byte is rejected", !Ed25519.Verify(rgPubB2, Sha512.Hash(rgBody), rgSig));
        Chk("seal fold changes with pub_B (tampering pub_B breaks the folded chain)", SealFold(rgPubB2) != SealFold(rgPubB));

        string sF = Path.Combine(sDir, "sealed.bin");//端到端 附上seal区后离线验签要能用 body口径是去掉尾部104B
        byte[] rgFile = new byte[rgBody.Length + iSealSize];
        rgBody.CopyTo(rgFile, 0);
        rgR1.CopyTo(rgFile, rgBody.Length);
        File.WriteAllBytes(sF, rgFile);
        int iRc = VerifySealFile(sF);
        Chk("offline --verify-seal accepts a sealed file", iRc == 0);
        byte[] rgBad = (byte[])rgFile.Clone(); rgBad[0] ^= 1;
        string sB = Path.Combine(sDir, "sealed-bad.bin");
        File.WriteAllBytes(sB, rgBad);
        Chk("offline --verify-seal rejects a body-tampered file", VerifySealFile(sB) != 0);
        string sNo = Path.Combine(sDir, "plain.bin");
        File.WriteAllBytes(sNo, rgBody);
        Chk("offline --verify-seal reports a missing seal region", VerifySealFile(sNo) != 0);
        byte[] rgPrivFull = KdfLabel(rgK, "seal:priv");
        Chk("seal region carries no private material (Kdf(seal:priv) / its 32B seed absent from the 104B zone)",
            rgR1.AsSpan().IndexOf(rgPrivFull.AsSpan(0, 32)) < 0 && rgR1.AsSpan().IndexOf(rgPrivFull) < 0);

        Console.WriteLine(iBad == 0 ? "[seal-kat] RESULT PASS" : $"[seal-kat] RESULT FAIL ({iBad} checks)");
        return iBad == 0 ? 0 : 2;
    }
}
