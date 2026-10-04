namespace Lamarr.Packer;

using System.Numerics;

//RFC 8032 Ed25519纯托管实现 域运算用BigInteger以保证正确性优先
//签名只在打包期使用 产物侧只要Verify 无RNG 无状态 无操作系统依赖
internal static partial class Ed25519
{
    internal const int iPub = 32;
    internal const int iSig = 64;

    static readonly BigInteger P = (BigInteger.One << 255) - 19;
    static readonly BigInteger L = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    static readonly BigInteger D = Mod(-121665 * Inv(121666));
    static readonly BigInteger ISq = BigInteger.ModPow(2, (P - 1) / 4, P);
    static readonly BigInteger Gx, Gy;

    static Ed25519()
    {
        BigInteger y = Mod(4 * Inv(5));//按曲线定义现算基点并取偶根 手抄坐标容易出错
        BigInteger x = SqrtRatio(Mod(y * y - 1), Mod(D * y * y + 1), out bool bOk);
        if (!bOk) throw new InvalidOperationException("ed25519: base point sqrt failed");
        if (!(x & BigInteger.One).IsZero) x = P - x;
        Gx = x; Gy = y;
    }

    static BigInteger Mod(BigInteger x) { x %= P; return x.Sign < 0 ? x + P : x; }
    static BigInteger ModL(BigInteger x) { x %= L; return x.Sign < 0 ? x + L : x; }
    static BigInteger Inv(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);
    static BigInteger LE(byte[] rg) => new(rg, isUnsigned: true, isBigEndian: false);

    static byte[] Le32(BigInteger x)
    {
        byte[] rg = x.ToByteArray(isUnsigned: true, isBigEndian: false);
        byte[] rgOut = new byte[32];
        Array.Copy(rg, rgOut, Math.Min(32, rg.Length));
        return rgOut;
    }

    static BigInteger SqrtRatio(BigInteger u, BigInteger v, out bool bOk)//解码用开方x²=u/v 失败即非法点
    {
        u = Mod(u); v = Mod(v);
        BigInteger v3 = Mod(v * v * v);
        BigInteger v7 = Mod(v3 * v3 * v);
        BigInteger x = Mod(u * v3 * BigInteger.ModPow(Mod(u * v7), (P - 5) / 8, P));
        BigInteger vx2 = Mod(v * x * x);
        if (vx2 == u) { bOk = true; return x; }
        if (vx2 == Mod(-u)) { bOk = true; return Mod(x * ISq); }
        bOk = false; return BigInteger.Zero;
    }

    static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Add(
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p,
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) q)//扩展坐标点加(X:Y:Z:T) T=XY/Z
    {
        BigInteger a = Mod((p.Y - p.X) * (q.Y - q.X));
        BigInteger b = Mod((p.Y + p.X) * (q.Y + q.X));
        BigInteger c = Mod(2 * p.T * q.T * D);
        BigInteger d = Mod(2 * p.Z * q.Z);
        BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
        return (Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    static readonly (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Id =
        (BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero);

    static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) Mul(
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p, BigInteger e)
    {
        var r = Id;
        for (int i = (int)e.GetBitLength() - 1; i >= 0; i--)
        {
            r = Add(r, r);
            if (!((e >> i) & BigInteger.One).IsZero) r = Add(r, p);
        }
        return r;
    }

    static byte[] Encode((BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p)
    {
        BigInteger zI = Inv(p.Z);
        BigInteger x = Mod(p.X * zI), y = Mod(p.Y * zI);
        byte[] rg = Le32(y);
        if (!(x & BigInteger.One).IsZero) rg[31] |= 0x80;
        return rg;
    }

    static bool TryDecode(byte[] rg, out (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p)
    {
        p = Id;
        if (rg.Length != 32) return false;
        byte[] rgY = (byte[])rg.Clone();
        int iSign = (rgY[31] >> 7) & 1;
        rgY[31] &= 0x7F;
        BigInteger y = LE(rgY);
        if (y >= P) return false;//非规范编码一律拒
        BigInteger x = SqrtRatio(Mod(y * y - 1), Mod(D * y * y + 1), out bool bOk);
        if (!bOk) return false;
        if (x.IsZero && iSign == 1) return false;
        if (((int)(x & BigInteger.One)) != iSign) x = P - x;
        p = (x, y, BigInteger.One, Mod(x * y));
        return true;
    }

    static bool SamePoint((BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) p,
        (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) q)
        => Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

    static byte[] ClampScalar(byte[] rgH)
    {
        byte[] rg = new byte[32];
        Array.Copy(rgH, rg, 32);
        rg[0] &= 248; rg[31] &= 127; rg[31] |= 64;
        return rg;
    }

    static (BigInteger X, BigInteger Y, BigInteger Z, BigInteger T) G => (Gx, Gy, BigInteger.One, Mod(Gx * Gy));

    internal static byte[] PublicKey(byte[] rgSeed)
    {
        byte[] rgH = Sha512.Hash(rgSeed);
        return Encode(Mul(G, LE(ClampScalar(rgH))));
    }

    internal static byte[] Sign(byte[] rgSeed, byte[] rgMsg)//签名确定性来源于r=SHA512(prefix‖M) 同seed同msg必得同一签 重钉才能稳定
    {
        byte[] rgH = Sha512.Hash(rgSeed);
        BigInteger s = LE(ClampScalar(rgH));
        byte[] rgPre = new byte[32];
        Array.Copy(rgH, 32, rgPre, 0, 32);
        byte[] rgA = Encode(Mul(G, s));
        BigInteger r = ModL(LE(Sha512.Hash(rgPre, rgMsg, null)));
        byte[] rgR = Encode(Mul(G, r));
        BigInteger k = ModL(LE(Sha512.Hash(rgR, rgA, rgMsg)));
        BigInteger S = ModL(r + k * s);
        byte[] rgSig = new byte[64];
        Array.Copy(rgR, 0, rgSig, 0, 32);
        Array.Copy(Le32(S), 0, rgSig, 32, 32);
        return rgSig;
    }

    internal static bool Verify(byte[] rgPub, byte[] rgMsg, byte[] rgSig)//严格校验 要求S<L并拒绝非规范编码 方程不带协因子
    {
        if (rgPub.Length != 32 || rgSig.Length != 64) return false;
        if (LE(rgSig[32..64]) >= L) return false;//否则S+L也验得过 签名可变形
        if (!TryDecode(rgPub, out var ptA)) return false;
        if (!TryDecode(rgSig[0..32], out var ptR)) return false;
        BigInteger k = ModL(LE(Sha512.Hash(rgSig[0..32], rgPub, rgMsg)));
        return SamePoint(Mul(G, LE(rgSig[32..64])), Add(ptR, Mul(ptA, k)));
    }

}
