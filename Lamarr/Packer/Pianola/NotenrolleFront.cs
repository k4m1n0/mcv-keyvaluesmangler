namespace Lamarr.Packer;

using System.Collections.Generic;

public static class NotenrolleFront
{
    public abstract class Ex { }
    public sealed class Lit : Ex { public ulong uV; public int iWB; public Lit(ulong v, int w) { uV = v; iWB = w; } }
    public sealed class In : Ex { public int iR; public int iWB; public In(int r, int w) { iR = r; iWB = w; } }
    public sealed class Bin : Ex { public string sOp; public int iWB; public Ex a; public Ex b; public Bin(string op, int w, Ex x, Ex y) { sOp = op; iWB = w; a = x; b = y; } }
    public sealed class Narrow : Ex { public Ex x; public ulong uMask; public Narrow(Ex e, ulong m) { x = e; uMask = m; } }
    public sealed class Rev : Ex { public Ex x; public int iWB; public Rev(Ex e, int w) { x = e; iWB = w; } }//字节序反转单目 宽度由iWB定
    public sealed class Mem : Ex { public int iWB; public int iBase; public Ex? exIdx; public Mem(int w, int ib, Ex? e) { iWB = w; iBase = ib; exIdx = e; } }//M[iBase+idx]取值 idx为空时取M[iBase]

    public static int WC(int iBits) => iBits switch//公开API用位宽 内部Ex与Asm用宽度码0..3
    {
        8 => 0, 16 => 1, 32 => 2, 64 => 3,
        _ => throw new System.InvalidOperationException($"bits {iBits}")
    };

    public static Lit C(ulong uV, int iBits = 32) => new(uV, WC(iBits));
    public static Ex C64(ulong uV) => uV <= 0xFFFFFFFFUL//MOVIMM限32位以内立即数 64位常量拆hi/lo两段拼 |lo MOVIMM w32是零扩展所以shl 32之后高位就是hi
        ? C(uV, 32)
        : Op("or", Op("shl", C(uV >> 32, 32), C(32, 32), 64), C(uV & 0xFFFFFFFFUL, 32), 64);
    public static In V(int iR, int iBits = 32) => new(iR, WC(iBits));
    public static Bin Op(string sOp, Ex x, Ex y, int iBits = 32) => new(sOp, WC(iBits), x, y);
    public static Mem M(int iBits, int iBase, Ex? exIdx = null) => new(WC(iBits), iBase, exIdx);
    public static Narrow N(Ex x, ulong uMask) => new(x, uMask);
    public static Rev B(Ex x, int iBits = 64) => new(x, WC(iBits));//字节序反转只发w32/w64才有意义

    public sealed class Core
    {
        readonly NotenrolleComp.Asm a = new();
        readonly Dictionary<int, int> mpW = new();
        readonly List<int> rgFree = new();
        int iTmp = 8;
        int iLab;

        public int Var(int iReg, int iBits) { mpW[iReg] = WC(iBits); return iReg; }//登记变量位宽 变量由内核定义 固定占寄存器

        public void Set(int iDst, Ex e)
        {
            int wD = W(iDst);
            Emit(e, iDst);
            if (WB(e) > wD)//目标窄于源时才截断 源同宽或更窄已是零扩展值 掩码按目标宽度发
            {
                int rM = Tmp();
                a.Mov(rM, MaskOf(wD), 2);//掩码在32位以内 MOVIMM只支持32位以内立即数
                a.Alu("and", wD, iDst, rM);
                Free(rM);
            }
        }

        public void OpSet(int iDst, string sOp, Ex e)//iDst = iDst op e ALU宽度取目标宽度
        {
            int rB = Tmp();
            Emit(e, rB);
            a.Alu(sOp, W(iDst), iDst, rB);
            Free(rB);
        }

        public void Ld(int iBits, int iBase, Ex? exIdx, int iDst)//dst = M[iBase + idx]
        {
            int rA = Addr(iBase, exIdx);
            a.Ld(WC(iBits), iDst, rA);
            Free(rA);
        }

        public void St(int iBits, int iBase, Ex? exIdx, Ex eVal)//M[iBase + idx] = val
        {
            int wS = WC(iBits);
            int rA = Addr(iBase, exIdx);
            int rV = Tmp();
            Emit(eVal, rV, (wS <= 1 && LowDet(eVal)) ? wS : -1);//窄存只留低位 低位可信的链可按窄宽度算
            a.St(wS, rA, rV);
            Free(rV);
            Free(rA);
        }

        public void If(string sCmp, Ex x, Ex y, System.Action fnThen, System.Action? fnElse = null)//条件sCmp eq是等ne是不等cs是无符号小于cc是无符号大于等于
        {
            string sElse = "L" + iLab++;
            Cond(x, y);
            if (fnElse != null)
            {
                string sEnd = "L" + iLab++;
                a.Jcc(Not(sCmp), sElse);
                fnThen();
                a.Jmp(sEnd);
                a.Label(sElse);
                fnElse();
                a.Label(sEnd);
            }
            else
            {
                a.Jcc(Not(sCmp), sElse);
                fnThen();
                a.Label(sElse);
            }
        }

        public void While(string sCmp, Ex x, Ex y, System.Action fnBody)
        {
            string sTop = "L" + iLab++, sEnd = "L" + iLab++;
            a.Label(sTop);
            Cond(x, y);
            a.Jcc(Not(sCmp), sEnd);
            fnBody();
            a.Jmp(sTop);
            a.Label(sEnd);
        }

        public byte[] Finish() => a.Finish();
        public int Emit(Ex e, int rd, int wF = -1)//求值到rd返回rd wF >= 0表示只需要低wF位 低位可信的链按wF宽度算
        {
            switch (e)
            {
                case Lit l:
                    if (l.iWB == 3 && l.uV > 0xFFFFFFFFUL && wF < 0)
                    {
                        int rLo = Tmp(), rSh = Tmp();//w64立即数拆成MOVIMM + MOVIMM + shl 32 + or 载体层立即数限32位以内
                        a.Mov(rd, l.uV >> 32, 2);
                        a.Mov(rLo, l.uV & 0xFFFFFFFFUL, 2);
                        a.Mov(rSh, 32, 2);
                        a.Alu("shl", 3, rd, rSh);
                        a.Alu("or", 3, rd, rLo);
                        Free(rSh); Free(rLo);
                        return rd;
                    }
                    a.Mov(rd, l.uV, (wF >= 0 && wF < l.iWB) ? wF : l.iWB);
                    return rd;
                case In v:
                    if (v.iR != rd) a.MovReg(rd, v.iR);
                    return rd;
                case Mem m:
                {
                    int rA = Addr(m.iBase, m.exIdx);
                    a.Ld(m.iWB, rd, rA);
                    Free(rA);
                    return rd;
                }
                case Narrow n:
                {
                    int wN = MaskW(n.uMask);//窄化吸收 掩码能用宽度表达且整条链低位可信时按窄宽度算 掩码省掉
                    if (wN >= 0 && LowDet(n.x)) { Emit(n.x, rd, wN); return rd; }
                    if (wN >= 0 && wF >= 0 && wF <= wN) { Emit(n.x, rd, wF); return rd; }//外层算术不比这层掩码宽 低位已无残留 掩码冗余
                    Emit(n.x, rd);
                    int rM = Tmp();
                    a.Mov(rM, n.uMask, 2);//掩码在32位以内
                    a.Alu("and", wN >= 0 ? wN : 3, rd, rM);//按宽度发掩码and只要掩码在低位窄宽度的and与原宽度等价&M == x&M)
                    Free(rM);
                    return rd;
                }
                case Rev r:
                {
                    Emit(r.x, rd);//字节序反转要看到全部字节 操作数必须按自身宽度算 不吸收外层的wF
                    a.Bswap(r.iWB, rd);
                    return rd;
                }
                case Bin b:
                {
                    int wE = (wF >= 0 && LowDetOp.Contains(b.sOp)) ? wF : -1;//窄化吸收只需低wF位 且该族的结果低位只由操作数低位决定 整条链按wF算
                    Emit(b.a, rd, wE);//ALU是破坏性双操作数 左值先入rd 右值入临时寄存器
                    int rB = Tmp();
                    Emit(b.b, rB, wE);
                    a.Alu(b.sOp, wE >= 0 ? wE : b.iWB, rd, rB);
                    Free(rB);
                    return rd;
                }
            }
            throw new System.InvalidOperationException("expr");
        }

        void Cond(Ex x, Ex y)//比较x与y 引擎CMP置ZF和CF 条件与跳转由调用方按 sCmp 自行发出
        {
            int rX = Tmp(), rY = Tmp();
            Emit(x, rX);
            Emit(y, rY);
            a.Cmp(rX, rY);
            Free(rY);
            Free(rX);
        }

        int Addr(int iBase, Ex? exIdx)//地址寄存器 = iBase + idx 64位加立即数受MOVIMM的32位限制
        {
            int r = Tmp();
            if (exIdx == null)
            {
                a.Mov(r, (ulong)iBase, 2);
                return r;
            }
            Emit(exIdx, r);
            if (iBase != 0)
            {
                int rB = Tmp();
                a.Mov(rB, (ulong)iBase, 2);
                a.Alu("add", 3, r, rB);
                Free(rB);
            }
            return r;
        }

        int W(int iReg) => mpW.TryGetValue(iReg, out int w) ? w : 2;

        static int WB(Ex e) => e switch//表达式的保守位宽判断 目标变量要不要补截断
        {
            Lit l => l.iWB,
            In v => v.iWB,
            Mem m => m.iWB,
            Rev r => r.iWB,
            Narrow n => WB(n.x),
            Bin b => System.Math.Max(WB(b.a), WB(b.b)),
            _ => 3
        };

        static ulong MaskOf(int w) => w switch//64位没有掩码 Set 只在目标窄于源时调用 传进来的只可能是 w8/w16/w32
        {
            0 => 0xFFUL,
            1 => 0xFFFFUL,
            2 => 0xFFFFFFFFUL,
            _ => throw new System.InvalidOperationException("MaskOf(64): the narrowing path must not ask for a 64 bit mask"),
        };
        static int MaskW(ulong uMask) => uMask == 0xFFUL ? 0 : uMask == 0xFFFFUL ? 1 : -1;//掩码能不能表达成宽度0xFF=w8 0xFFFF=w16其余不行

        static readonly HashSet<string> LowDetOp = new() { "add", "sub", "mul", "and", "or", "xor" };//低位可信族 结果只由操作数低位决定 整条链按窄宽度算 移位/旋转/除法/取模不在集合内
        static bool LowDet(Ex e) => e switch
        {
            Lit => true,
            In => true,
            Mem => true,//访存按各自宽度读低位一定对
            Narrow n => LowDet(n.x),
            Rev => false,//字节序反转把高位搬到低位 必须在原宽度上做
            Bin b => LowDetOp.Contains(b.sOp) && LowDet(b.a) && LowDet(b.b),
            _ => false
        };

        static int Not(string sCmp) => sCmp switch//引擎Jcc 0=总是1=eq 2=ne 3=cs 4=cc
        {
            "eq" => 2, "ne" => 1, "cs" => 4, "cc" => 3,
            _ => throw new System.InvalidOperationException($"cmp {sCmp}")
        };

        int Tmp()
        {
            if (rgFree.Count > 0)
            {
                int r = rgFree[^1];
                rgFree.RemoveAt(rgFree.Count - 1);
                return r;
            }
            if (iTmp > 15) throw new System.InvalidOperationException("tmp overflow");
            return iTmp++;
        }

        void Free(int r) => rgFree.Add(r);
    }
}
