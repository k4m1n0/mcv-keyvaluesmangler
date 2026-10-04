namespace Lamarr.Packer;

using System.Numerics;

//纯托管SHA-512 不使用进程外密码学组件 以免多一个可挂钩的面
internal static class Sha512
{
    static readonly ulong[] HK = DeriveH();
    static readonly ulong[] KK = DeriveK();

    internal static byte[] Hash(byte[] rg) => Hash(rg, null, null);

    //指纹工具 不引PeWriterAntheil侧类型 这样关闭antheil属性的变体也能用
    internal static string Fp(byte[] rg, int cbHex = 32)
    {
        if (cbHex < 0 || cbHex > 128) throw new ArgumentOutOfRangeException(nameof(cbHex), "hex length must be within [0,128]");
        return Convert.ToHexString(Hash(rg)).Substring(0, cbHex);
    }

    internal static byte[] Hash(byte[] rgA, byte[]? rgB, byte[]? rgC)
    {
        ulong[] h = (ulong[])HK.Clone();
        long cb = (long)rgA.Length + (rgB?.Length ?? 0) + (rgC?.Length ?? 0);
        ulong cbBits = (ulong)cb * 8uL;
        long cbPad = ((cb + 16) / 128 + 1) * 128;//至少一个0x80与16B长度域
        byte[] rgM = new byte[cbPad];
        Buffer.BlockCopy(rgA, 0, rgM, 0, rgA.Length);
        int iAt = rgA.Length;
        if (rgB != null) { Buffer.BlockCopy(rgB, 0, rgM, iAt, rgB.Length); iAt += rgB.Length; }
        if (rgC != null) { Buffer.BlockCopy(rgC, 0, rgM, iAt, rgC.Length); }
        rgM[cb] = 0x80;
        for (int i = 0; i < 8; i++) rgM[cbPad - 1 - i] = (byte)(cbBits >> (8 * i));

        ulong[] w = new ulong[80];
        for (long iBlk = 0; iBlk < cbPad; iBlk += 128)
        {
            for (int i = 0; i < 16; i++)
            {
                int o = (int)iBlk + i * 8;
                ulong u = 0;
                for (int k = 0; k < 8; k++) u = (u << 8) | rgM[o + k];//大端 算法规定
                w[i] = u;
            }
            for (int i = 16; i < 80; i++)
            {
                ulong uA = w[i - 15], uB = w[i - 2];
                ulong s0 = ((uA >> 1) | (uA << 63)) ^ ((uA >> 8) | (uA << 56)) ^ (uA >> 7);
                ulong s1 = ((uB >> 19) | (uB << 45)) ^ ((uB >> 61) | (uB << 3)) ^ (uB >> 6);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (int i = 0; i < 80; i++)
            {
                ulong S1 = ((e >> 14) | (e << 50)) ^ ((e >> 18) | (e << 46)) ^ ((e >> 41) | (e << 23));
                ulong ch = (e & f) ^ (~e & g);
                ulong t1 = hh + S1 + ch + KK[i] + w[i];
                ulong S0 = ((a >> 28) | (a << 36)) ^ ((a >> 34) | (a << 30)) ^ ((a >> 39) | (a << 25));
                ulong mj = (a & b) ^ (a & c) ^ (b & c);
                ulong t2 = S0 + mj;
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }
        byte[] rgOut = new byte[64];
        for (int i = 0; i < 8; i++)
            for (int k = 0; k < 8; k++)
                rgOut[i * 8 + k] = (byte)(h[i] >> (56 - 8 * k));
        return rgOut;
    }

    static int[] FirstPrimes(int n)
    {
        int[] rg = new int[n];
        int c = 0;
        for (int v = 2; c < n; v++)
        {
            bool bPrime = true;
            for (int d = 2; (long)d * d <= v; d++) if (v % d == 0) { bPrime = false; break; }
            if (bPrime) rg[c++] = v;
        }
        return rg;
    }

    static BigInteger ISqrt(BigInteger n)
    {
        if (n < 2) return n;
        BigInteger x = BigInteger.One << ((int)((n.GetBitLength() + 1) / 2));
        for (int i = 0; i < 400; i++)
        {
            BigInteger y = (x + n / x) >> 1;
            if (y >= x) break;
            x = y;
        }
        while (x * x > n) x -= 1;//Newton迭代可能停在根上方1
        return x;
    }

    static BigInteger ICbrt(BigInteger n)
    {
        if (n < 2) return n;
        BigInteger x = BigInteger.One << ((int)((n.GetBitLength() + 2) / 3));
        for (int i = 0; i < 400; i++)
        {
            BigInteger y = (2 * x + n / (x * x)) / 3;
            if (y >= x) break;
            x = y;
        }
        while (x * x * x > n) x -= 1;
        while ((x + 1) * (x + 1) * (x + 1) <= n) x += 1;
        return x;
    }

    //floor(frac(root(p))*2^64)等于整数根按2^64缩放后的末64位
    static ulong Frac64(BigInteger r, int cExpand)
        => (ulong)((cExpand == 2 ? ISqrt(r << 128) : ICbrt(r << 192)) & ((BigInteger.One << 64) - 1));

    static ulong[] DeriveH()
    {
        int[] rgP = FirstPrimes(8);
        ulong[] rg = new ulong[8];
        for (int i = 0; i < 8; i++) rg[i] = Frac64(rgP[i], 2);
        return rg;
    }

    static ulong[] DeriveK()
    {
        int[] rgP = FirstPrimes(80);
        ulong[] rg = new ulong[80];
        for (int i = 0; i < 80; i++) rg[i] = Frac64(rgP[i], 3);
        return rg;
    }

    //自查 常量与标准前缀一致
    internal static ulong DbgK(int i) => KK[i];
    internal static ulong DbgH(int i) => HK[i];
    internal static bool ConstsOk()
        => HK[0] == 0x6A09E667F3BCC908UL && HK[7] == 0x5BE0CD19137E2179UL
        && KK[0] == 0x428A2F98D728AE22UL && KK[79] == 0x6C44198C4A475817UL;
}
