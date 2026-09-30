namespace Lamarr.Packer;

public static class KernLayout//M尾部16字节是引擎保留区roll密钥 .text地址 .text长度内核数据只能用前面
{
    public const int Seed = 0x00;//材料占32B
    public const int SeedLen = 32;
    public const int Key = 0x20;//输出与输入各16B
    public const int KeyLen = 16;
    public const int H = 0x40;//扰动源占64B
    public const int HLen = 64;

    public const uint K_Fnv = 0x811C9DC5;//uLK0A^uLK0B
    public const uint K_Mul = 0x000001B3;//uLK1A^uLK1B
    public const uint K_Alt = 0x1B0CA2B5;//uQ3A^uQ3B
    public const uint K_Gold = 0x9E3779B9;//uR2A^uR2B
    public const uint K_Fnv2 = 0x01000193;//uR1A^uR1B
    public const uint K_G0 = 0x5A5A5A5A;
    public const uint K_D0 = 0x6E5A1F2B;
    public const uint K_D1 = 0x4D7C9E35;
    public const uint K_D2 = 0x85EBCA6B;
    public const uint K_D3 = 0xC2B2AE35;
    public const uint K_SeedG = 0x9E3779B9;

    public const ulong K_MaskH0 = 0x6A09E667F3BCC909UL;//签名表掩码用与jit密钥域分开
    public const ulong K_MaskMul = 0xC2B2AE3D27D4EB4FUL;
    public const ulong K_MaskXor = 0x9E3779B97F4A7C15UL;
}

public static class NotenrolleKernels
{
    public static byte[] SeedKeyN()//16轮uint环 逐字节读Seed写Key
    {
        var c = new NotenrolleFront.Core();
        int rA = c.Var(0, 32), rB = c.Var(1, 32), rG = c.Var(2, 32), rI = c.Var(3, 32), rT = c.Var(4, 32), rY = c.Var(5, 32);
        c.Set(rA, NotenrolleFront.C(KernLayout.K_Fnv));
        c.Set(rB, NotenrolleFront.C(KernLayout.K_Alt));
        c.Set(rG, NotenrolleFront.C(KernLayout.K_G0));
        c.Set(rI, NotenrolleFront.C(0));
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.C(16), () =>
        {
            c.OpSet(rA, "xor", NotenrolleFront.M(8, KernLayout.Seed, NotenrolleFront.V(rI)));
            c.OpSet(rA, "mul", NotenrolleFront.C(KernLayout.K_Fnv2));
            c.OpSet(rB, "xor", NotenrolleFront.M(8, KernLayout.Seed + 16, NotenrolleFront.V(rI)));
            c.OpSet(rB, "mul", NotenrolleFront.C(KernLayout.K_Gold));
            c.Set(rT, NotenrolleFront.Op("add",//uT = (vA ^ (vB<<1)) + (vB ^ (vA>>3))
                NotenrolleFront.Op("xor", NotenrolleFront.V(rA), NotenrolleFront.Op("shl", NotenrolleFront.V(rB), NotenrolleFront.C(1))),
                NotenrolleFront.Op("xor", NotenrolleFront.V(rB), NotenrolleFront.Op("shr", NotenrolleFront.V(rA), NotenrolleFront.C(3)))));
            c.Set(rY, NotenrolleFront.Op("xor",//by = (T>>16 ^ T>>24) ^ seed[i]
                NotenrolleFront.Op("xor",
                    NotenrolleFront.Op("shr", NotenrolleFront.V(rT), NotenrolleFront.C(16)),
                    NotenrolleFront.Op("shr", NotenrolleFront.V(rT), NotenrolleFront.C(24))),
                NotenrolleFront.M(8, KernLayout.Seed, NotenrolleFront.V(rI))));
            c.St(8, KernLayout.Key, NotenrolleFront.V(rI), NotenrolleFront.V(rY));
            c.OpSet(rG, "mul", NotenrolleFront.C(KernLayout.K_SeedG));
            c.OpSet(rG, "xor", NotenrolleFront.V(rY));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        c.Set(0, NotenrolleFront.V(rG));//返回uG
        return c.Finish();
    }

    public static byte[] K1N()//材料在Seed 长度在v3 返回uH在v0
    {
        var c = new NotenrolleFront.Core();
        int rH = c.Var(0, 32), rG = c.Var(1, 32), rI = c.Var(2, 32), rY = c.Var(4, 32);//v3是长度上界不能占用
        c.Set(rH, NotenrolleFront.C(KernLayout.K_Fnv));
        c.Set(rG, NotenrolleFront.C(KernLayout.K_Gold));
        c.Set(rI, NotenrolleFront.C(0));
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.V(3), () =>
        {
            c.Set(rY, NotenrolleFront.M(8, KernLayout.Seed, NotenrolleFront.V(rI)));
            c.OpSet(rH, "xor", NotenrolleFront.V(rY));
            c.OpSet(rH, "mul", NotenrolleFront.C(KernLayout.K_Mul));
            c.OpSet(rG, "mul", NotenrolleFront.C(KernLayout.K_Fnv2));
            c.OpSet(rG, "add", NotenrolleFront.V(rY));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        return c.Finish();
    }

    public static byte[] GenDisturbN()//rgH在H 走64轮奇偶分支 再16轮写到Key
    {
        var c = new NotenrolleFront.Core();
        int rA = c.Var(0, 32), rB = c.Var(1, 32), rS = c.Var(2, 32), rK = c.Var(3, 32), rI = c.Var(4, 32), rY = c.Var(5, 32);
        c.Set(rA, NotenrolleFront.C(KernLayout.K_D0));
        c.Set(rB, NotenrolleFront.C(KernLayout.K_D1));
        c.Set(rK, NotenrolleFront.C(0));
        c.While("cs", NotenrolleFront.V(rK), NotenrolleFront.C(64), () =>
        {
            c.Set(rY, NotenrolleFront.Op("and",//索引 = (k*19+11) & 63
                NotenrolleFront.Op("add",
                    NotenrolleFront.Op("mul", NotenrolleFront.V(rK), NotenrolleFront.C(19)),
                    NotenrolleFront.C(11)),
                NotenrolleFront.C(63)));
            c.Ld(8, KernLayout.H, NotenrolleFront.V(rY), rY);
            c.If("ne", NotenrolleFront.Op("and", NotenrolleFront.V(rK), NotenrolleFront.C(1)), NotenrolleFront.C(0),
                () =>
                {
                    c.OpSet(rB, "xor", NotenrolleFront.V(rY));
                    c.OpSet(rB, "mul", NotenrolleFront.C(KernLayout.K_D3));
                },
                () =>
                {
                    c.OpSet(rA, "xor", NotenrolleFront.V(rY));
                    c.OpSet(rA, "mul", NotenrolleFront.C(KernLayout.K_D2));
                });
            c.OpSet(rK, "add", NotenrolleFront.C(1));
        });
        c.Set(rS, NotenrolleFront.Op("xor", NotenrolleFront.V(rA), NotenrolleFront.V(rB)));
        c.Set(rI, NotenrolleFront.C(0));
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.C(16), () =>
        {
            c.OpSet(rS, "mul", NotenrolleFront.C(KernLayout.K_Fnv2));
            c.OpSet(rS, "add", NotenrolleFront.C(KernLayout.K_Gold));
            c.St(8, KernLayout.Key, NotenrolleFront.V(rI), NotenrolleFront.Op("shr", NotenrolleFront.V(rS), NotenrolleFront.C(24)));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        return c.Finish();
    }

    public static byte[] DecN()//M[v6..v6+v3)就地XOR成明文 调用方置v1=keyMix v2=流起点 v3=字节数 v4/v5=siteKey v6=M偏移 返回v0=0
    {
        var a = new NotenrolleComp.Asm();
        a.MovReg(7, 0);//i = 0 v0已被调用方清零寄存器搬运无需立即数
        a.Label("dloop");
        a.MovReg(8, 2);//r8 = j = v2 + i
        a.Alu("add", 2, 8, 7);
        a.MovReg(12, 8);//r12 = bit = j & 1
        a.Mov(9, 3, 2);
        a.Alu("shr", 2, 12, 9);
        a.Mov(9, 1, 2);
        a.Alu("and", 0, 12, 9);//bit只看最低位w8 and与w32等价
        a.MovReg(11, 8);//r11 = k = *8
        a.Mov(9, 7, 2);
        a.Alu("and", 0, 11, 9);//k = * 8低3位w8
        a.Mov(9, 8, 2);
        a.Alu("mul", 2, 11, 9);
        a.MovReg(10, 4);//r10 = b4 = & 0xFF
        a.Alu("shr", 3, 10, 11);
        a.Mov(9, 0xFF, 2);
        a.Alu("and", 0, 10, 9);//掩码在低位w8 and与w64 and等价
        a.MovReg(9, 5);//r9 = b5 = & 0xFF
        a.Alu("shr", 3, 9, 11);
        a.Mov(11, 0xFF, 2);
        a.Alu("and", 0, 9, 11);//v0 = v9 & 0xFF
        a.Mov(11, 0xFF, 2);//r12 = sel = bit * 0xFF
        a.Alu("mul", 0, 12, 11);//bit*0xFF <= 0xFF w8
        a.MovReg(11, 10);//r10 = ks = b4 ^ & sel) 逐字节选半字只用xor/and/mul
        a.Alu("xor", 0, 11, 9);//b4与b5与sel都是字节 这一族全按w8算
        a.Alu("and", 0, 11, 12);
        a.Alu("xor", 0, 10, 11);
        a.MovReg(11, 8);//r9 = km = *8)) & 0xFF
        a.Mov(9, 3, 2);
        a.Alu("and", 2, 11, 9);
        a.Mov(9, 8, 2);
        a.Alu("mul", 2, 11, 9);
        a.MovReg(9, 1);
        a.Alu("shr", 2, 9, 11);
        a.Mov(11, 0xFF, 2);
        a.Alu("and", 0, 9, 11);//掩码在低位w8 and与w32 and等价
        a.Alu("xor", 0, 10, 9);//流字节 = ks ^ km两者都是字节w8
        a.MovReg(11, 6);//M[v6+i] ^= r10
        a.Alu("add", 2, 11, 7);
        a.Ld(0, 9, 11);               //r9 = M[addr]
        a.Alu("xor", 0, 9, 10);//字节^字节w8
        a.St(0, 11, 9);
        a.Mov(9, 1, 2);//i++ while
        a.Alu("add", 2, 7, 9);
        a.Cmp(7, 3);
        a.Jcc(3, "dloop");            //cs = 无符号小于i < n继续
        a.MovReg(0, 0);               //返回值0
        return a.Finish();
    }

    public static byte[] MxN()//Key[0..3]^Key[4..7]^K_Fnv 为0时取K_Fnv 返回v0
    {
        var c = new NotenrolleFront.Core();
        int rA = c.Var(0, 32), rB = c.Var(1, 32);
        c.Ld(32, KernLayout.Key, null, rA);
        c.Ld(32, KernLayout.Key + 4, null, rB);
        c.OpSet(rA, "xor", NotenrolleFront.V(rB));
        c.OpSet(rA, "xor", NotenrolleFront.C(KernLayout.K_Fnv));
        c.If("ne", NotenrolleFront.V(rA), NotenrolleFront.C(0), () => { },
            () => c.Set(rA, NotenrolleFront.C(KernLayout.K_Fnv)));
        return c.Finish();
    }

    public static byte[] MaskKeyN()//材料@Seed 长度v3 返回maskLo在v0 64位常量用C64拆hi/lo MOVIMM限32位内立即数
    {
        var c = new NotenrolleFront.Core();
        int rH = c.Var(0, 64), rI = c.Var(1, 32), rY = c.Var(2, 32), rK = c.Var(4, 64), rS = c.Var(5, 64), rT = c.Var(6, 64);
        c.Set(rH, NotenrolleFront.C64(KernLayout.K_MaskH0));
        c.Set(rK, NotenrolleFront.C64(KernLayout.K_MaskMul));
        c.Set(rI, NotenrolleFront.C(0));
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.V(3), () =>
        {
            c.Set(rY, NotenrolleFront.M(8, KernLayout.Seed, NotenrolleFront.V(rI)));//8位读入零扩展
            c.OpSet(rH, "xor", NotenrolleFront.V(rY));
            c.OpSet(rH, "mul", NotenrolleFront.V(rK));
            c.Set(rS, NotenrolleFront.Op("shr", NotenrolleFront.V(rH), NotenrolleFront.C(29), 64));
            c.OpSet(rH, "xor", NotenrolleFront.V(rS));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        c.Set(rS, NotenrolleFront.V(rH));
        c.Set(rT, NotenrolleFront.Op("shr", NotenrolleFront.V(rH), NotenrolleFront.C(16), 64));
        c.OpSet(rS, "xor", NotenrolleFront.V(rT));
        c.Set(rT, NotenrolleFront.Op("shl", NotenrolleFront.V(rH), NotenrolleFront.C(13), 64));
        c.OpSet(rS, "xor", NotenrolleFront.V(rT));
        c.Set(rT, NotenrolleFront.C64(KernLayout.K_MaskXor));
        c.OpSet(rS, "xor", NotenrolleFront.V(rT));
        c.Set(0, NotenrolleFront.V(rS));//v0 = low32 Set对u32目标自动补截断
        return c.Finish();
    }

}
