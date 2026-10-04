namespace Lamarr.Packer;

public static class KernLayout//尾部16字节是引擎保留区 存roll密钥与.text地址与.text长度 内核数据只能用前面的空间
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

public static partial class NotenrolleKernels
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
        c.Set(0, NotenrolleFront.V(rG));
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
        a.MovReg(0, 0);
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

    //摘要内核 单元7 材料在M[0..v3) 头4字节是进入的32位FNV-1a状态 逐字节续算 返回新状态在v0
    public static byte[] FnvUnitN()
    {
        var c = new NotenrolleFront.Core();
        int rH = c.Var(0, 32), rI = c.Var(1, 32), rY = c.Var(4, 32);
        c.Ld(32, 0, null, rH);//进入状态
        c.Set(rI, NotenrolleFront.C(4));//数据从第4字节起
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.V(3), () =>
        {
            c.Set(rY, NotenrolleFront.M(8, 0, NotenrolleFront.V(rI)));
            c.OpSet(rH, "xor", NotenrolleFront.V(rY));
            c.OpSet(rH, "mul", NotenrolleFront.C(KernLayout.K_Fnv2));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        c.Set(0, NotenrolleFront.V(rH));//返回状态在v0
        return c.Finish();
    }

    //64位摘要内核 单元8 材料在M[0..v3) 头8字节是进入的FNV-1a64状态 逐字节续算 结果写M[0x20..0x28)
    public static byte[] Fnv64UnitN()
    {
        var c = new NotenrolleFront.Core();
        int rH = c.Var(0, 64), rI = c.Var(1, 32), rY = c.Var(4, 32);
        c.Ld(64, 0, null, rH);//进入状态(8字节)
        c.Set(rI, NotenrolleFront.C(8));//数据从第8字节起
        c.While("cs", NotenrolleFront.V(rI), NotenrolleFront.V(3), () =>
        {
            c.Set(rY, NotenrolleFront.M(8, 0, NotenrolleFront.V(rI)));
            c.OpSet(rH, "xor", NotenrolleFront.V(rY));
            c.OpSet(rH, "mul", NotenrolleFront.C64(0x100000001B3UL));
            c.OpSet(rI, "add", NotenrolleFront.C(1));
        });
        c.St(64, KernLayout.Key, null, NotenrolleFront.V(rH));
        return c.Finish();
    }

    static NotenrolleFront.Ex C(int v) => NotenrolleFront.C((ulong)(uint)v);//工厂只吃ulong 这里补齐int与uint的写法
    static NotenrolleFront.Ex C(uint v) => NotenrolleFront.C(v);
    static NotenrolleFront.Ex V(int r) => NotenrolleFront.V(r);
    static NotenrolleFront.Ex M(int iBits, int iBase) => NotenrolleFront.M(iBits, iBase);
    static NotenrolleFront.Ex M(int iBits, int iBase, NotenrolleFront.Ex exIdx) => NotenrolleFront.M(iBits, iBase, exIdx);
    static NotenrolleFront.Ex Op(string sOp, NotenrolleFront.Ex a, NotenrolleFront.Ex b) => NotenrolleFront.Op(sOp, a, b);

    //ChaCha12单元是roll的流单元 槽9 全w32且状态常驻材料 因为引擎只有v0到v11十二个寄存器放不下16个字
    //每64B一块ctr每块自增 尾块按长度截断 逐字节异或天然支持任意长度
    public static byte[] ChaCha12UnitN()
    {
        var c = new NotenrolleFront.Core();
        int rP = c.Var(0, 32);//数据字节指针 相对数据区起点 内层逐字节前进 外层按块前进
        int rQ = c.Var(1, 32);//四分之一轮序号 0..47
        int rT = c.Var(2, 32);
        int rA = c.Var(4, 32), rB = c.Var(5, 32), rC = c.Var(6, 32), rD = c.Var(7, 32);

        c.St(32, ChLayout.In, null, C(ChLayout.C0));//前导 把16个初始字铺进私有In 常量4加材料key8加材料ctr加材料nonce3
        c.St(32, ChLayout.In + 4, null, C(ChLayout.C1));
        c.St(32, ChLayout.In + 8, null, C(ChLayout.C2));
        c.St(32, ChLayout.In + 12, null, C(ChLayout.C3));
        for (int j = 0; j < 8; j++) c.St(32, ChLayout.In + 16 + 4 * j, null, M(32, ChLayout.Key + 4 * j));
        c.St(32, ChLayout.In + 48, null, M(32, ChLayout.Slot + 12));//ctr
        for (int j = 0; j < 3; j++) c.St(32, ChLayout.In + 52 + 4 * j, null, M(32, ChLayout.Slot + 4 * j));//nonce

        c.St(32, ChLayout.Len, null, C(0));//数据长度等于材料长度v3减去数据区偏移v3不足一个头时按0 链推进用空材料执行本单元时走这条
        c.If("cc", V(3), C(ChLayout.Data), () => c.St(32, ChLayout.Len, null, Op("sub", V(3), C(ChLayout.Data))));
        c.Set(rP, C(0));
        c.While("cs", V(rP), M(32, ChLayout.Len), () =>
        {
            c.St(32, ChLayout.Cb, null, C(64));//本块字节数cb取64与剩余长度的较小值
            c.If("cs", Op("sub", M(32, ChLayout.Len), V(rP)), C(64),
                () => c.St(32, ChLayout.Cb, null, Op("sub", M(32, ChLayout.Len), V(rP))));
            c.St(32, ChLayout.BlkBase, null, V(rP));

            c.Set(rT, C(0));//初始状态取In的16个字 逐块重铺ctr在In[12]里自增
            c.While("cs", V(rT), C(16), () =>
            {
                c.St(32, ChLayout.State, Op("shl", V(rT), C(2)), M(32, ChLayout.In, Op("shl", V(rT), C(2))));
                c.OpSet(rT, "add", C(1));
            });
            c.Set(rQ, C(0));//12轮等于48个四分之一轮 状态下标从IdxTbl查表 展开会让字节码超出VM代码窗0x1000
            c.While("cs", V(rQ), C(48), () => Qr(c, rA, rB, rC, rD, rQ));
            c.Set(rT, C(0));//输出字等于状态字加初始字 再拆字节进密钥流缓冲In[12]已是本块计数器
            c.While("cs", V(rT), C(16), () =>
            {
                c.Set(rA, M(32, ChLayout.State, Op("shl", V(rT), C(2))));
                c.OpSet(rA, "add", M(32, ChLayout.In, Op("shl", V(rT), C(2))));
                for (int j = 0; j < 4; j++)
                    c.St(8, ChLayout.Ks, Op("add", Op("shl", V(rT), C(2)), C(j)), j == 0 ? V(rA) : Op("shr", V(rA), C(8 * j)));
                c.OpSet(rT, "add", C(1));
            });
            c.While("cs", Op("sub", V(rP), M(32, ChLayout.BlkBase)), M(32, ChLayout.Cb), () =>//逐字节异或rP从块基址走到块尾 顺带完成外层前进
            {
                c.Ld(8, ChLayout.Ks, Op("sub", V(rP), M(32, ChLayout.BlkBase)), rT);
                c.Ld(8, ChLayout.Data, V(rP), rA);
                c.OpSet(rT, "xor", V(rA));
                c.St(8, ChLayout.Data, V(rP), V(rT));
                c.OpSet(rP, "add", C(1));
            });
            c.Set(rT, M(32, ChLayout.In, C(48)));//In[12]每块自增一次
            c.OpSet(rT, "add", C(1));
            c.St(32, ChLayout.In, C(48), V(rT));
        });

        c.Set(rT, M(32, ChLayout.In, C(48)));
        c.St(32, ChLayout.Slot, C(0), V(rT));//协议槽首4字节放新计数器 与驱动读M[0x20]的约定对齐
        c.St(32, ChLayout.Slot, C(12), V(rT));//原位回写 下一次调用可从槽里续读
        c.Set(0, V(rT));//返回值=结束计数器
        return c.Finish();
    }

    static void Qr(NotenrolleFront.Core c, int rA, int rB, int rC, int rD, int rQ)//一个四分之一轮 4个状态字落在rA到rD 第q个QR的4个下标从IdxTbl逐字节取
    {
        NotenrolleFront.Ex Off(int j) => Op("shl", M(32, ChLayout.Tbl, Op("add", Op("shl", Op("and", V(rQ), C(7)), C(4)), C(4 * j))), C(2));//表项是状态下标 乘4等于State里的字节偏移
        c.Ld(32, ChLayout.State, Off(0), rA);
        c.Ld(32, ChLayout.State, Off(1), rB);
        c.Ld(32, ChLayout.State, Off(2), rC);
        c.Ld(32, ChLayout.State, Off(3), rD);
        c.OpSet(rA, "add", V(rB));
        c.OpSet(rD, "xor", V(rA));
        c.OpSet(rD, "rol", C(16));
        c.OpSet(rC, "add", V(rD));
        c.OpSet(rB, "xor", V(rC));
        c.OpSet(rB, "rol", C(12));
        c.OpSet(rA, "add", V(rB));
        c.OpSet(rD, "xor", V(rA));
        c.OpSet(rD, "rol", C(8));
        c.OpSet(rC, "add", V(rD));
        c.OpSet(rB, "xor", V(rC));
        c.OpSet(rB, "rol", C(7));
        c.St(32, ChLayout.State, Off(0), V(rA));
        c.St(32, ChLayout.State, Off(1), V(rB));
        c.St(32, ChLayout.State, Off(2), V(rC));
        c.St(32, ChLayout.State, Off(3), V(rD));
        c.OpSet(rQ, "add", C(1));
    }
}

//ChaCha12单元的材料布局 驱动写key与nonce与ctr 数据落在Q.VmMatData 0x900起是内核私有工作区逐调用零化
public static class ChLayout
{
    public const int Key = 0x00;
    public const int Slot = 0x20;
    public const int Data = 0x30;//必须等于A0.Q.VmMatData
    public const int DataMax = A0.QStream.dm;//驱动分块上限 0x30加1984等于0x7F0 小于私有区的0x900

    public const int In = 0x900;//16个w32初始字 常量4加key8加ctr加nonce3 ctr在In[12]
    public const int State = 0x940;//16个w32工作状态
    public const int Ks = 0x980;//64B 密钥流字节缓冲
    public const int Len = 0x9C0;//w32 数据长度
    public const int Cb = 0x9C4;//w32 本块字节数
    public const int BlkBase = 0x9C8;//w32 本块基址
    public const int Tbl = A0.QStream.Tbl;//8乘4的w32四分之一轮状态下标表 调用方按QrTbl布好

    public static byte[] QrTbl() => A0.QStream.TblBytes();//QR下标表的单一来源在A0.QStream

    public const uint C0 = 0x61707865;//"expa"
    public const uint C1 = 0x3320646E;//"nd 3"
    public const uint C2 = 0x79622D32;//"2-by"
    public const uint C3 = 0x6B206574;//"te k"
}
