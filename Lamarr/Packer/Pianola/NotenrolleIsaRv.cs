namespace Lamarr.Packer;

using System.Collections.Generic;

//RV64指令字层内部字节码与指令字流 字节序le 位域见下方字位常量 宽度码在第三操作数字段 R型rs2 I型imm12低5位 载荷型偏移放auipc的imm20 寄存器字段放低5位真寄存器号 字段都承载信息不留定值或互为副本
public static class NotenrolleIsaRv
{
    static void PutW(List<byte> w, uint v)
    { w.Add((byte)v); w.Add((byte)(v >> 8)); w.Add((byte)(v >> 16)); w.Add((byte)(v >> 24)); }
    static uint ReadW(byte[] b, int i) => (uint)b[i] | ((uint)b[i + 1] << 8) | ((uint)b[i + 2] << 16) | ((uint)b[i + 3] << 24);

    static uint R(int f7, int f3, int rd, int rs1, int rs2)//R型word=f7<<25|rs2<<20|rs1<<15|f3<<12|rd<<7|0x33
        => ((uint)f7 << 25) | ((uint)(rs2 & 31) << 20) | ((uint)(rs1 & 31) << 15) | ((uint)f3 << 12) | ((uint)(rd & 31) << 7) | 0x33u;
    static uint I(int f3, int rd, int rs1, int iImm)//I型word=imm12<<20|rs1<<15|f3<<12|rd<<7|0x13
        => ((uint)(iImm & 0xFFF) << 20) | ((uint)(rs1 & 31) << 15) | ((uint)f3 << 12) | ((uint)(rd & 31) << 7) | 0x13u;
    static uint LUI(int rd, uint uHi20) => ((uHi20 & 0xFFFFF) << 12) | ((uint)(rd & 31) << 7) | 0x37u;
    static uint AUIPC(int rd, uint uImm20) => ((uImm20 & 0xFFFFF) << 12) | ((uint)(rd & 31) << 7) | 0x17u;
    static uint Ld(int f3, int rd, int rs1, int iDisp)//LD操作码0x03 f3 0=lb 1=lh 2=lw 3=ld ST操作码0x23位移字段引擎不读填噪声S型位移拆在[31:25]与[11:7]
        => ((uint)(iDisp & 0xFFF) << 20) | ((uint)(rs1 & 31) << 15) | ((uint)f3 << 12) | ((uint)(rd & 31) << 7) | 0x03u;
    static uint St(int f3, int rs1, int rs2, int iDisp)
        => ((uint)((iDisp >> 5) & 0x7F) << 25) | ((uint)(rs2 & 31) << 20) | ((uint)(rs1 & 31) << 15)
           | ((uint)f3 << 12) | ((uint)(iDisp & 0x1F) << 7) | 0x23u;

    const uint W_END = 0x00008067u;//ret = jalr x0,0
    const uint W_RET = 0x000080E7u;//jalr x1,0

    internal static readonly (int, int)[] rgFamMap = {
        (0x00,0),(0x20,0),(0x01,0),(0x01,4),(0x01,5),(0x01,6),
        (0x00,7),(0x00,6),(0x00,4),(0x00,1),(0x00,5),(0x20,5),(0x01,7) };
    static (int, int) FamOf(int iFam) => rgFamMap[(iFam - 0x20) / 0x10];

    const int F7_REV = 0x34, F3_REV = 5;//字节序指令字合成grevi形态 f7=34h f3=5 字面落在移位类且与族表不撞

    static void PutCarrierPair(List<byte> w, byte byOpC, int iPair, int iWidth)//同形状类载体按操作码置换[op]选操作数字节 访存类按本ISA访存字段放寄存器对 ALU族按rd/rs 单目只放一个4位寄存器 宽度码放第三操作数字段
    {
        if (byOpC >= 0x10 && byOpC <= 0x13) { PutW(w, Ld(byOpC - 0x10, iPair & 15, iPair >> 4, CarrierFmt.NoiseBits(12))); return; }
        if (byOpC >= 0x14 && byOpC <= 0x17) { PutW(w, St(byOpC - 0x14, iPair >> 4, iPair & 15, CarrierFmt.NoiseBits(12))); return; }
        if (byOpC >= 0x18 && byOpC <= 0x1B)
        {
            PutW(w, R(F7_REV, F3_REV, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;
        }
        if (byOpC >= 0xF0 && byOpC <= 0xF3)//NOT四宽度
        {
            int iImm = CarrierFmt.Dup(iWidth) | (CarrierFmt.NoiseBits(7) << 5);//字面是xori rd, rs1, imm12 rs1像真读一个寄存器宽度码藏在imm12的低5位
            PutW(w, I(4, iPair & 15, CarrierFmt.NoiseReg(), iImm)); return;
        }
        if (byOpC >= 0x20 && byOpC <= 0xEF)
        {
            var ff = FamOf(byOpC - (byOpC & 3));
            PutW(w, R(ff.Item1, ff.Item2, iPair & 15, iPair >> 4, CarrierFmt.Dup(iWidth))); return;
        }
        throw new InvalidOperationException($"RV pair carrier unsupported op {byOpC:X2}");
    }

    public static byte[] EncodeLinear(byte[] b)
    {
        var w = new List<byte>(); int i = 0;
        while (i < b.Length)
        {
            byte op = b[i];
            byte byOpC = OpTable.Map(op);//操作数解析用规范操作码 选载体用操作码置换[op]
            if (op == 0x00 || op == 0x01) { PutW(w, byOpC == 0x00 ? W_END : W_RET); i++; continue; }//END/RET ret/jalr
            if (op == 0x08)//MOVIMM [rd][iWB][imm] w<=32
            {
                int rd = b[i + 1], iWB = b[i + 2];
                if (iWB < 0 || iWB > 2) throw new InvalidOperationException($"RV MOVIMM iWB unsupported {iWB}");
                int n = new[] { 1, 2, 4 }[iWB];
                uint uV = 0; for (int k = 0; k < n; k++) uV |= (uint)b[i + 3 + k] << (8 * k);
                i += 3 + n;
                int iHi = (int)((uV + 0x800u) >> 12);//补偿使signext12还原
                int iLo = (int)(uV - ((uint)iHi << 12));
                PutW(w, LUI(rd, (uint)(iHi & 0xFFFFF)));
                PutW(w, I(0, rd, CarrierFmt.Dup(iWB), iLo));//addi rd,rs1=宽度码+噪声,lo12
                continue;
            }
            //寄存器搬移与比较在置换组内互换 死字段填噪声 免得恒定值成为指纹
            if (op == 0x09) { int p = b[i + 1]; i += 2; if (byOpC == 0x09) PutW(w, I(0, p & 15, p >> 4, CarrierFmt.NoiseBits(12))); else PutW(w, R(0, 2, p & 15, p >> 4, CarrierFmt.NoiseReg())); continue; }
            if (op == 0x1C) { int p = b[i + 1]; i += 2; if (byOpC == 0x1C) PutW(w, R(0, 2, p & 15, p >> 4, CarrierFmt.NoiseReg())); else PutW(w, I(0, p & 15, p >> 4, CarrierFmt.NoiseBits(12))); continue; }
            if ((op >= 0x10 && op <= 0x1B) || (op >= 0x20 && op <= 0xEF) || (op >= 0xF0 && op <= 0xF3))//同形状类
            {
                int iWidth = op & 3; int p = b[i + 1]; i += 2;//8/16位也是原生能力 不按宽度拒绝
                PutCarrierPair(w, byOpC, p, iWidth);
                continue;
            }
            if (op == 0x1D || op == 0x1F)//JMP与CALL同形 操作码置换组内1D与1F互换
            {
                uint uOff = (uint)(short)(b[i + 1] | (b[i + 2] << 8)); i += 3;
                PutW(w, AUIPC(byOpC == 0x1D ? 0 : 2, uOff & 0xFFFF));
                continue;
            }
            if (op == 0x1E)//Jcc自成一类
            {
                uint uCond = b[i + 1]; uint uOff = (uint)(short)(b[i + 2] | (b[i + 3] << 8)); i += 4;
                PutW(w, AUIPC(1, ((uCond & 7u) << 16) | (uOff & 0xFFFF)));
                continue;
            }
            throw new InvalidOperationException($"RV opcode not covered {op:X2}");
        }
        return w.ToArray();
    }

    public static byte[]? DecodeLinear(byte[] rgIsa)
    {
        var o = new List<byte>();
        int i = 0;
        while (i < rgIsa.Length)
        {
            if (i + 4 > rgIsa.Length) return null;
            uint w = ReadW(rgIsa, i); i += 4;
            if (w == W_END) { o.Add(0x00); continue; }
            if (w == W_RET) { o.Add(0x01); continue; }
            uint uOp = w & 0x7Fu;
            if (uOp == 0x37u)//LUI+ADDI MOVIMM
            {
                if (i + 4 > rgIsa.Length) return null;
                uint w2 = ReadW(rgIsa, i);
                if ((w2 & 0x7Fu) != 0x13u || ((w2 >> 12) & 7u) != 0u) return null;
                i += 4;
                int rd = (int)((w >> 7) & 31);
                int iWB = CarrierFmt.WidthOf((int)((w2 >> 15) & 31));//addi.rs1承载宽度码与噪声
                if (rd > 15 || iWB > 2) return null;
                int iHi = (int)(w >> 12);
                int iLo = ((int)(w2 >> 20) << 20) >> 20;
                uint uV = (uint)((iHi << 12) + iLo);
                int n = new[] { 1, 2, 4 }[iWB];
                o.Add(0x08); o.Add((byte)rd); o.Add((byte)iWB);
                for (int k = 0; k < n; k++) o.Add((byte)(uV >> (8 * k)));
                continue;
            }
            if (uOp == 0x17u)//AUIPC JMP/Jcc/CALL
            {
                int iTag = (int)((w >> 7) & 31);
                uint uPay = (w >> 12) & 0xFFFFFu;
                short sOff = (short)(uPay & 0xFFFF);
                if (iTag == 0) { o.Add(0x1D); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                if (iTag == 2) { o.Add(0x1F); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                if (iTag == 1)
                {
                    o.Add(0x1E); o.Add((byte)((uPay >> 16) & 0xFF));
                    o.Add((byte)sOff); o.Add((byte)(sOff >> 8));
                    continue;
                }
                return null;
            }
            if (uOp == 0x13u)//I型f3=0走MOVREG f3=4走NOT
            {
                int f3 = (int)((w >> 12) & 7);
                if (f3 == 0)
                {
                    o.Add(0x09); o.Add((byte)((((w >> 15) & 15) << 4) | ((w >> 7) & 15)));
                    continue;
                }
                if (f3 == 4)
                {
                    int gD = (int)((w >> 7) & 31);//寄存器字段不含宽度位 宽度码在imm12的低5位
                    int iWB = CarrierFmt.WidthOf((int)((w >> 20) & 31));
                    if (gD > 15) return null;
                    o.Add((byte)(0xF0 + iWB)); o.Add((byte)gD);
                    continue;
                }
                return null;
            }
            if (uOp == 0x03u || uOp == 0x23u)//LD/ST按funct3区分宽度
            {
                int f3 = (int)((w >> 12) & 3);
                int iAddr = (int)((w >> 15) & 15);
                if (uOp == 0x03u)
                {
                    o.Add((byte)(0x10 + f3));
                    o.Add((byte)((iAddr << 4) | (int)((w >> 7) & 15)));
                }
                else
                {
                    o.Add((byte)(0x14 + f3));
                    o.Add((byte)((iAddr << 4) | (int)((w >> 20) & 15)));
                }
                continue;
            }
            if (uOp == 0x33u)//R型
            {
                int f7 = (int)((w >> 25) & 0x7F), f3 = (int)((w >> 12) & 7);
                int iWB = CarrierFmt.WidthOf((int)((w >> 20) & 31));//rs2承载宽度码与噪声
                if (f7 == 0 && f3 == 2)
                {
                    o.Add(0x1C); o.Add((byte)((((w >> 15) & 15) << 4) | ((w >> 7) & 15)));
                    continue;
                }
                if (f7 == F7_REV && f3 == F3_REV)//字节序指令字BSWAP
                {
                    int iRdB = (int)((w >> 7) & 31);
                    if (iRdB > 15) return null;
                    o.Add((byte)(0x18 + iWB)); o.Add((byte)iRdB);
                    continue;
                }
                int iIdx = -1;
                for (int k = 0; k < rgFamMap.Length; k++)
                    if (rgFamMap[k].Item1 == f7 && rgFamMap[k].Item2 == f3) { iIdx = k; break; }
                if (iIdx < 0) return null;
                int gD = (int)((w >> 7) & 31), gS = (int)((w >> 15) & 31);
                if (gD > 15 || gS > 15) return null;//寄存器字段是真寄存器号 不该出现第5位
                o.Add((byte)(0x20 + iIdx * 0x10 + iWB));
                o.Add((byte)((gS << 4) | gD));
                continue;
            }
            return null;
        }
        return o.ToArray();
    }
}
