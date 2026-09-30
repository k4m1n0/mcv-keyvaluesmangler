namespace Lamarr.Packer;

public static class IsaPool
{
    public static readonly string[] PoolNames =
        { "ppc", "rv64", "mips64", "loongarch", "alpha" };

    public static string CanonicalIsa(string sName) => sName.ToLowerInvariant() switch
    {
        "ppc" or "ppc64" => "ppc",
        "rv64" or "riscv64" => "rv64",
        "mips64" => "mips64",
        "loongarch" or "la64" => "loongarch",
        "alpha" or "alpha64" => "alpha",
        _ => throw new System.ArgumentOutOfRangeException(nameof(sName), sName, "ISA not implemented")
    };

    public static string PoolPick(ulong uSeed, int iRole = 0)//两级抽取先定类再定项 种子只作形态nonce 不参与密钥派生
    {
        ulong s = Mix64(uSeed ^ 0x9E3779B97F4A7C15UL ^ ((ulong)iRole * 0x2545F4914F6CDD1DUL));
        uint iN = (uint)PoolNames.Length;
        uint iClass = (uint)(Mix64(s + 0x100000001B3UL) % iN);
        uint iItem = (uint)(Mix64(s + 0x9E3779B97F4A7C15UL) % iN);
        return PoolNames[(iClass + iItem) % iN];
    }

    public static ulong Mix64(ulong u)
    {
        u ^= u >> 30; u *= 0xBF58476D1CE4E5B9UL;
        u ^= u >> 27; u *= 0x94D049BB133111EBUL;
        return u ^ (u >> 31);
    }

    public static string CarrierIsa(ulong uSeed, int iRole = 0)//产物内核指令字编码与引擎侧解码用 没就绪就回退ppc
    {
        string s = PoolPick(uSeed, iRole);
        return CarrierReady(s) ? s : "ppc";
    }

    public static bool CarrierReady(string sName) => CanonicalIsa(sName) switch
    {
        "ppc" => true,
        "rv64" => true,
        "mips64" => true,
        "loongarch" => true,
        "alpha" => true,
        _ => false
    };
}