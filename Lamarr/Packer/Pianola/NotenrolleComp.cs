namespace Lamarr.Packer;

using System.Text;
using System.Collections.Generic;

public static partial class NotenrolleComp
{
    public enum EW { W8 = 0, W16 = 1, W32 = 2, W64 = 3 }//宽度码0=w8 1=w16 2=w32 3=w64
    public sealed class Asm
    {
        readonly List<byte> w = new();
        readonly Dictionary<string, int> mpLab = new();
        readonly List<(int iPos, string sLab, bool bJcc)> rgFix = new();

        public void Label(string sLab) => mpLab[sLab] = w.Count;

        public void Mov(int rd, ulong uV, int iWB) => EmitMovImm(w, rd, uV, iWB);

        public void MovReg(int rd, int rs)
        {
            w.Add(0x09); w.Add((byte)(((rs & 15) << 4) | (rd & 15)));
        }

        public void Alu(string sOp, int iWB, int rd, int rs)//双寄存器ALU 结果落rd 右操作数在rs
        {
            w.Add((byte)(FamBase(sOp) + iWB));
            w.Add((byte)(((rs & 15) << 4) | (rd & 15)));
        }

        public void Not(int iWB, int rd)
        {
            w.Add((byte)(0xF0 + iWB)); w.Add((byte)(rd & 15));
        }

        public void Bswap(int iWB, int rd)//单目字节序反转 操作码0x18+w 只带一个寄存器w8恒等w16用rol ax,8 w32与w64用bswap
        {
            w.Add((byte)(0x18 + iWB)); w.Add((byte)(rd & 15));
        }

        public void AndMask(int rd, ulong uMask, int rTmp)//按宽度截断rd与uMask 掩码经临时寄存器rTmp载入
        {
            EmitMovImm(w, rTmp, uMask, 3);
            w.Add(0x73); w.Add((byte)(((rTmp & 15) << 4) | (rd & 15)));
        }

        public void Ld(int iWB, int rDst, int rAddr)//LD与ST操作数字节 高4位是地址寄存器 低4位是目标或数据寄存器
        {
            w.Add((byte)(0x10 + iWB)); w.Add((byte)(((rAddr & 15) << 4) | (rDst & 15)));
        }

        public void St(int iWB, int rAddr, int rData)
        {
            w.Add((byte)(0x14 + iWB)); w.Add((byte)(((rAddr & 15) << 4) | (rData & 15)));
        }

        public void Cmp(int rd, int rs)
        {
            w.Add(0x1C); w.Add((byte)(((rs & 15) << 4) | (rd & 15)));
        }

        public void Jmp(string sLab)
        {
            w.Add(0x1D); rgFix.Add((w.Count, sLab, false)); w.Add(0); w.Add(0);
        }

        public void Jcc(int iCond, string sLab)
        {
            w.Add(0x1E); w.Add((byte)iCond); rgFix.Add((w.Count, sLab, true)); w.Add(0); w.Add(0);
        }

        public byte[] Finish()//末尾补END并回填跳转偏移 目标是操作码位置加off16
        {
            w.Add(0x00);
            foreach (var (iPos, sLab, bJcc) in rgFix)
            {
                if (!mpLab.TryGetValue(sLab, out int iTgt))
                    throw new InvalidOperationException($"unknown label {sLab}");
                int iOp = bJcc ? iPos - 2 : iPos - 1;
                int iOff = iTgt - iOp;
                if (iOff < -32768 || iOff > 32767)
                    throw new InvalidOperationException($"jump out of range: {sLab} {iOff}");
                w[iPos] = (byte)iOff; w[iPos + 1] = (byte)(iOff >> 8);
            }
            return w.ToArray();
        }
    }

    public static byte[] CompileJitKeyRoll()//jit密钥派生内核 常量在v0与v1 调用方置v2=0 v3=长度 材料放M 返回h在v0
    {
        var a = new Asm();
        a.Mov(0, 0x2F1E4B7DUL, 2);//v0=h0w32
        a.Mov(1, 0x9E3B7A11UL, 2);//v1=kw32
        a.Label("loop");
        a.Ld(0, 4, 2);//LD8v4=M[v2]
        a.Alu("xor", 2, 0, 4);//XOR32v0^=v4
        a.Alu("mul", 2, 0, 1);//MUL32v0*=v1
        a.Mov(5, 1UL, 2);//MOVimm v5 = 1 w32
        a.Alu("add", 2, 2, 5);//ADD32 v2 += v5
        a.Cmp(2, 3);
        a.Jcc(2, "loop");
        return a.Finish();
    }

    static int FamBase(string nm) => nm switch
    {
        "add" => 0x20, "sub" => 0x30, "mul" => 0x40, "divu" => 0x50, "modu" => 0x60,
        "and" => 0x70, "or" => 0x80, "xor" => 0x90, "shl" => 0xA0, "shr" => 0xB0,
        "asr" => 0xC0, "rol" => 0xD0, "ror" => 0xE0, _ => throw new InvalidOperationException(nm)
    };

    public static byte[] BuildRollForSeed(ulong uSeed, uint uRollKey, UnitRunner fnRun, out uint uChainTail)//把jit密钥派生内核编成roll字节码 按种子选载体ISA编码 再叠at-rest加密
    {
        return BuildRoll(uSeed, uRollKey, fnRun, out uChainTail);
    }

    //链推进必须真的执行前一枚单元 解不出或执行失败就返回false让打包失败关闭
    public delegate bool UnitRunner(byte[] rgBlob, uint uKey, uint uChainIn, out uint uChainOut, out string sErr);

    public const int KernJitKey = 0, KernMask = 1, KernSeedKey = 2, KernK1 = 3, KernDisturb = 4, KernMx = 5, KernDec = 6, KernFnvUnit = 7, KernFnv64Unit = 8, KernChaCha12 = 9;//roll单元号 顺序即容器内顺序

    static readonly Func<byte[]>[] KernTable = {//内核顺序即容器内顺序 加内核只改这里
        CompileJitKeyRoll, NotenrolleKernels.MaskKeyN, NotenrolleKernels.SeedKeyN,
        NotenrolleKernels.K1N, NotenrolleKernels.GenDisturbN, NotenrolleKernels.MxN,
        NotenrolleKernels.DecN, NotenrolleKernels.FnvUnitN, NotenrolleKernels.Fnv64UnitN, NotenrolleKernels.ChaCha12UnitN };
    public static int KernRollCount => KernTable.Length;

    public static byte[] CompileKern(int iKern) => CompileKern(iKern, 0);//按序编内部字节码 不含载体编码与at-rest加密 不认识的指令原样返回

    public static byte[] CompileKern(int iKern, ulong uSeed)
    {
        byte[] rg = KernTable[iKern]();
        return NotenrolleCfg.Apply(rg, KernLayout.Key, new System.Random(KernSeed(uSeed, iKern)), out _);
    }

    public static byte[] CompileUnit(Func<byte[]> fnUnit, ulong uSeed, int iSlot)//通用编译入口 任意字节码源走同一管线 带CFG状态键插桩
    {
        byte[] rg = fnUnit();
        return NotenrolleCfg.Apply(rg, KernLayout.Key, new System.Random(KernSeed(uSeed, iSlot)), out _);
    }

    static int KernSeed(ulong uSeed, int iKern)//每个内核一条独立随机流
    {
        ulong u = uSeed ^ ((ulong)(0x70u + (uint)iKern) * 0x9E3779B97F4A7C15UL);
        u ^= u >> 33; u *= 0xFF51AFD7ED558CCDUL;
        u ^= u >> 33; u *= 0xC4CEB9FE1A85EC53UL;
        u ^= u >> 33;
        return (int)(u ^ (u >> 32));
    }

    public static byte[] EncodeForIsa(string sIsa, byte[] rgBc) => sIsa switch//按种子选载体ISA 整条roll同一个ISA 编码与解码两侧成对 解码只认本层编码器的产出
    {
        "ppc" => NotenrolleIsa.EncodeLinear(rgBc),
        "rv64" => NotenrolleIsaRv.EncodeLinear(rgBc),
        "mips64" => NotenrolleIsaMi.EncodeLinear(rgBc),
        "loongarch" => NotenrolleIsaLa.EncodeLinear(rgBc),
        "alpha" => NotenrolleIsaAl.EncodeLinear(rgBc),
        _ => throw new InvalidOperationException($"roll encode failed: {sIsa}")
    };

    //多内核容器 长度前缀明文 各内核入口只在容器数据区内算 不参与静态加密
    //链式 单元0用纯roll钥匙 单元i的at-rest键等于roll钥匙异或前序执行输出 解出任一枚都必须先按序真的执行前序单元
    public static byte[] BuildRoll(ulong uSeed, uint uRollKey, UnitRunner fnRun, out uint uChainTail)
    {
        uChainTail = 0;
        if (fnRun == null) throw new InvalidOperationException("roll chain requires an engine runner");
        if (KernRollCount != A0.Q.iRollUnits) throw new InvalidOperationException($"roll unit count {KernRollCount} != Q.iRollUnits {A0.Q.iRollUnits}");
        string sIsa = IsaPool.CarrierIsa(uSeed, 0);
        OpTable.Begin(uSeed, sIsa);//设定本产物的置换与载体宽度码
        CarrierFmt.Begin(uSeed);
        bool bBe = sIsa != "rv64" && sIsa != "loongarch";
        var w = new List<byte>();
        PutU32(w, (uint)KernRollCount);
        uint uChain = 0;
        for (int i = 0; i < KernRollCount; i++)
        {
            byte[] rgEnc = EncodeForIsa(sIsa, CompileKern(i, uSeed));
            if (rgEnc.Length == 0 || (rgEnc.Length & 3) != 0)
                throw new InvalidOperationException($"roll blob {i} length {rgEnc.Length}");
            PutU32(w, (uint)rgEnc.Length);
            PianolaEngine.AtRestCryptChain(rgEnc, uRollKey ^ uChain, bBe);
            if (!fnRun(rgEnc, uRollKey, uChain, out uint uNext, out string sErr))
                throw new InvalidOperationException($"roll chain unit {i} failed: {sErr}");
            uChain = uNext;
            w.AddRange(rgEnc);
        }
        uChainTail = uChain;//链终态 打包器随后把它与roll钥匙异或后写进roll尾部
        return w.ToArray();
    }

    static void PutU32(List<byte> w, uint uV)//长度前缀小端u32
    {
        for (int i = 0; i < 4; i++) w.Add((byte)(uV >> (8 * i)));
    }

    public static byte[] BuildPreKern(ulong uSeed, uint uKey, int iKern, UnitRunner fnRun) => BuildPreKern(uSeed, uKey, new[] { iKern }, fnRun);//单内核容器 等价于只装载一枚内核

    //前置容器 与整条roll同格式 独立at-rest钥匙 只由不依赖roll钥匙的域量派生
    //roll自身是单元宿主不能自举 所以掩码内核与ChaCha12流单元由前置容器装载 掩码内核解锁roll钥匙字段
    //链式 单元i的键等于prek钥匙异或前序执行输出
    public static byte[] BuildPreKern(ulong uSeed, uint uKey, int[] rgKern, UnitRunner fnRun)
    {
        if (fnRun == null) throw new InvalidOperationException("prekern chain requires an engine runner");
        string sIsa = IsaPool.CarrierIsa(uSeed, 0);
        OpTable.Begin(uSeed, sIsa);
        CarrierFmt.Begin(uSeed);
        bool bBe = sIsa != "rv64" && sIsa != "loongarch";
        var w = new List<byte>();
        PutU32(w, (uint)rgKern.Length);
        uint uChain = 0;//单元0用纯prek钥匙 单元i的at-rest键等于prek钥匙异或前序执行输出
        for (int i = 0; i < rgKern.Length; i++)
        {
            byte[] rgEnc = EncodeForIsa(sIsa, CompileKern(rgKern[i], uSeed));
            if (rgEnc.Length == 0 || (rgEnc.Length & 3) != 0)
                throw new InvalidOperationException($"prekern blob {i} length {rgEnc.Length}");
            PutU32(w, (uint)rgEnc.Length);
            PianolaEngine.AtRestCryptChain(rgEnc, uKey ^ uChain, bBe);
            if (!fnRun(rgEnc, uKey, uChain, out uint uNext, out string sErr))
                throw new InvalidOperationException($"prekern chain unit {i} failed: {sErr}");
            uChain = uNext;
            w.AddRange(rgEnc);
        }
        return w.ToArray();
    }

    public static void EmitMovImm(List<byte> w, int rd, ulong uV, int iWB)//MOVIMM [0x08][rd][w][imm LE bytes] w字节0=1B 1=2B 2=4B 3=8B
    {
        int n = new[] { 1, 2, 4, 8 }[iWB];
        w.Add(0x08); w.Add((byte)(rd & 15)); w.Add((byte)iWB);
        for (int i = 0; i < n; i++) w.Add((byte)(uV >> (8 * i)));
    }
}
