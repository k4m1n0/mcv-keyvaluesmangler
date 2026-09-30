namespace Lamarr.Packer;

using System.Text;
using System.Collections.Generic;

public static class NotenrolleComp
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

        public void Bswap(int iWB, int rd)//单目字节序反转0x18+w只带一个寄存器w8是恒等w16=rol ax,8 w32/w64用x86 bswap
        {
            w.Add((byte)(0x18 + iWB)); w.Add((byte)(rd & 15));
        }

        public void AndMask(int rd, ulong uMask, int rTmp)//按宽度截断rd &= uMask掩码经临时寄存器rTmp载入
        {
            EmitMovImm(w, rTmp, uMask, 3);
            w.Add(0x73); w.Add((byte)(((rTmp & 15) << 4) | (rd & 15)));
        }

        public void Ld(int iWB, int rDst, int rAddr)//LD/ST操作数字节 高4位是地址寄存器 低4位是目标或数据寄存器
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

        public byte[] Finish()//末尾补END并回填跳转偏移目标 = 操作码位置 + off16
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

    public static byte[] CompileJitKeyRoll()//jit密钥派生内核 常量在v0和v1 调用方置v2=0 v3=len M[0..len-1]=材料 返回h在v0
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

    public static byte[] BuildRollForSeed(ulong uSeed, uint uRollKey)//把jit密钥派生内核编成roll字节码 按种子选载体ISA编码 再叠at-rest加密
    {
        return BuildRoll(uSeed, uRollKey);
    }

    public const int KernJitKey = 0, KernMask = 1, KernSeedKey = 2, KernK1 = 3, KernDisturb = 4, KernMx = 5, KernDec = 6;//内核序号从0开始 顺序与roll容器一致

    static readonly Func<byte[]>[] KernTable = {//内核顺序即容器内顺序 加内核只改这里
        CompileJitKeyRoll, NotenrolleKernels.MaskKeyN, NotenrolleKernels.SeedKeyN,
        NotenrolleKernels.K1N, NotenrolleKernels.GenDisturbN, NotenrolleKernels.MxN,
        NotenrolleKernels.DecN };
    public static int KernRollCount => KernTable.Length;

    public static byte[] CompileKernRaw(int iKern) => KernTable[iKern]();//内核的原始字节码 不含CFG状态密钥插桩

    public static string KernOpStats()//统计内核字节码能力格出现次数 w8可达要求引擎执行且编译期发过w8 ALU
    {
        int iW8 = 0, iW16 = 0, iNot = 0, iBs = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < KernRollCount; i++)
        {
            byte[] rg = CompileKernRaw(i);
            int i8 = 0, i16 = 0, iN = 0, iB = 0;
            int p = 0;
            for (; p < rg.Length;)
            {
                int iLen = NotenrolleCfg.ILen(rg, p);
                if (iLen <= 0) break;
                byte op = rg[p];
                if (op >= 0x20 && op <= 0xEF) { if ((op & 15) == 0) i8++; else if ((op & 15) == 1) i16++; }
                else if (op >= 0xF0 && op <= 0xF3) iN++;
                else if (op >= 0x18 && op <= 0x1B) iB++;
                p += iLen;
            }
            if (p != rg.Length) sb.AppendLine($"[kern-ops] kern {i} parse stopped at {p}/{rg.Length}");
            sb.AppendLine($"[kern-ops] kern{i} len={rg.Length} aluW8={i8} aluW16={i16} not={iN} bswap={iB}");
            iW8 += i8; iW16 += i16; iNot += iN; iBs += iB;
        }
        sb.AppendLine($"[kern-ops] total aluW8={iW8} aluW16={iW16} not={iNot} bswap={iBs}");
        sb.AppendLine($"[kern-ops] reachable: w8-alu={iW8 > 0} (DecN/kernel6, ships with the artifact and runs in the engine)");
        sb.AppendLine($"[kern-ops] not-yet-emitted: w16-alu={iW16 > 0} not={iNot > 0} bswap={iBs > 0}" +
                      " (emitter wired for Asm.Bswap plus the Rev node of Front and the 0xFFFF of narrowing absorption;" +
                      " BSWAP was once emitted in MaskKeyN but the runtime mask value is taken by jithook as an integrity" +
                      " input, so it raised InvalidProgramException and was reverted)");
        sb.AppendLine($"[kern-ops] RESULT: {(iW8 > 0 ? "PASS" : "FAIL")} (requires the w8 ALU to be emitted at least once)");
        return sb.ToString();
    }

    public static byte[] CompileKern(int iKern) => CompileKern(iKern, 0);//按序编内部字节码 不含载体编码与at-rest加密 结构不认识时原样返回

    public static byte[] CompileKern(int iKern, ulong uSeed)
    {
        byte[] rg = KernTable[iKern]();
        return NotenrolleCfg.Apply(rg, KernLayout.Key, new System.Random(KernSeed(uSeed, iKern)), out _);
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

    //多内核容器 长度前缀明文 只在容器数据区内算各内核入口 不参与静态加密 每内核各自加密且流从头开始
    public static byte[] BuildRoll(ulong uSeed, uint uRollKey)
    {
        string sIsa = IsaPool.CarrierIsa(uSeed, 0);
        OpTable.Begin(uSeed, sIsa);//设定本产物的置换与载体宽度码
        CarrierFmt.Begin(uSeed);
        bool bBe = sIsa != "rv64" && sIsa != "loongarch";
        var w = new List<byte>();
        PutU32(w, (uint)KernRollCount);
        for (int i = 0; i < KernRollCount; i++)
        {
            byte[] rgEnc = EncodeForIsa(sIsa, CompileKern(i, uSeed));
            if (rgEnc.Length == 0 || (rgEnc.Length & 3) != 0)
                throw new InvalidOperationException($"roll blob {i} length {rgEnc.Length}");
            PutU32(w, (uint)rgEnc.Length);
            PianolaEngine.AtRestCrypt(rgEnc, uRollKey, bBe);
            w.AddRange(rgEnc);
        }
        return w.ToArray();
    }

    static void PutU32(List<byte> w, uint uV)//长度前缀小端u32
    {
        for (int i = 0; i < 4; i++) w.Add((byte)(uV >> (8 * i)));
    }

    public static void EmitMovImm(List<byte> w, int rd, ulong uV, int iWB)//MOVIMM [0x08][rd][w][imm LE bytes] w字节0=1B 1=2B 2=4B 3=8B
    {
        int n = new[] { 1, 2, 4, 8 }[iWB];
        w.Add(0x08); w.Add((byte)(rd & 15)); w.Add((byte)iWB);
        for (int i = 0; i < n; i++) w.Add((byte)(uV >> (8 * i)));
    }
}
