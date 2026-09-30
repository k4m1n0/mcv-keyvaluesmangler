namespace Lamarr.Packer;

public static class CarrierFmt
{
    public const int Ver = 2;//载体宽度编码的版本号 写进escrow

    static readonly string[] SaA3 = { "ppc", "rv64", "mips64", "loongarch", "alpha" };//escrow按载体查版本 必须写该载体实际用的 不是全局版本
    public static int VerFor(string sIsa) => System.Array.IndexOf(SaA3, sIsa) >= 0 ? 2 : 1;

    public static readonly byte[] Code = { 0, 1, 2, 3 };//宽度码0=8 1=16 2=32 3=64写到载体位[4:3]
    public static readonly byte[] Inv = { 0, 1, 2, 3 };//载体位[4:3] 宽度码反查

    static ulong _uS = 0x2545F4914F6CDD1DUL;

    public static void Begin(ulong uSeed)
    {
        _uS = uSeed ^ 0x2545F4914F6CDD1DUL;
        var rg = new byte[] { 0, 1, 2, 3 };
        for (int i = 3; i > 0; i--)
        {
            int j = (int)(Next() % (ulong)(i + 1));
            (rg[i], rg[j]) = (rg[j], rg[i]);
        }
        for (int w = 0; w < 4; w++) { Code[w] = rg[w]; Inv[rg[w]] = (byte)w; }
    }

    static ulong Next()//splitmix64逐位可复现 不依赖运行时版本
    {
        _uS += 0x9E3779B97F4A7C15UL;
        ulong uZ = _uS;
        uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9UL;
        uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBUL;
        return uZ ^ (uZ >> 31);
    }

    public static int Noise() => (int)(Next() & 7UL);//每条需要第三字段的指令消耗一次 取低3位

    public static int NoiseReg() => (int)(Next() & 31UL);//没人读的操作数字段 使字面分布像真实寄存器号

    public static int NoiseBits(int iBits) => (int)(Next() & ((1UL << iBits) - 1UL));//其他死字段 例如ppc访存指令的16位位移

    public static int Dup(int w) => Noise() | (Code[w & 3] << 3);//第三操作数字段 = 噪声 | 宽度码<<3字段位置ppc rB rv64 rs2 mips rt loongarch rk alpha rb

    public static int WidthOf(int uField) => Inv[(uField >> 3) & 3];

    public static byte WidthCode(int uBits) => uBits switch { 8 => Inv[0], 16 => Inv[1], 32 => Inv[2], 64 => Inv[3], _ => Inv[uBits & 3] };//位[4:3]存宽度码

    public static string Dump() =>
        $"[carrier-fmt] ver={Ver} code={Code[0]},{Code[1]},{Code[2]},{Code[3]} (field[4:3]=width code, field[2:0]=noise)";
}
