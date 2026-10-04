namespace Lamarr.Packer;

using System.Collections.Generic;

//指令字层负责内部字节码与指令字流互转 宽度码放在第三操作数字段 字节序为大端 操作数放通用寄存器字段低四位 宽度放该字段第4位 载荷型偏移放立即数
public static class NotenrolleIsa
{
    public static void PutW(List<byte> w, uint uInstr)
    {
        w.Add((byte)(uInstr >> 24)); w.Add((byte)(uInstr >> 16));
        w.Add((byte)(uInstr >> 8)); w.Add((byte)uInstr);
    }
    static uint ReadW(byte[] b, int i) => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    public const uint X_ADD = 266, X_SUBF = 40, X_MULLW = 235, X_DIVW = 491, X_MULHWU = 11,
        X_AND = 28, X_OR = 444, X_XOR = 316, X_NOR = 124, X_NAND = 476, X_SLW = 24, X_SRW = 536, X_SRAW = 792,
        X_ADDC = 10, X_EQV = 284, X_BSWAP = 202;//ADDC=MOVREG指令字 EQV=NOT指令字 BSWAP=字节序指令字 三者不在族表内 解码无歧义
    static uint XF(uint rD, uint xo, uint rA, uint rB)//X型word=31<<26|rD<<21|rA<<16|rB<<11|XO<<1|Rc
        => (31u << 26) | ((rD & 31) << 21) | ((rA & 31) << 16) | ((rB & 31) << 11) | ((xo & 0x3FF) << 1);

    static readonly (int, int)[] rgFamMap = {
        (0x20, (int)X_ADD),(0x30,(int)X_SUBF),(0x40,(int)X_MULLW),(0x50,(int)X_DIVW),
        (0x60,(int)X_MULHWU),(0x70,(int)X_AND),(0x80,(int)X_OR),(0x90,(int)X_XOR),
        (0xA0,(int)X_SLW),(0xB0,(int)X_SRW),(0xC0,(int)X_SRAW),(0xD0,(int)X_NOR),(0xE0,(int)X_NAND) };
    static int FamOf(uint xo)
    { foreach (var f in rgFamMap) if (f.Item2 == (int)xo) return f.Item1; return -1; }
    static uint XoOf(int iFam)
    { foreach (var f in rgFamMap) if (f.Item1 == iFam) return (uint)f.Item2; return 0; }
    internal static uint XoForFam(int iFam)//族基址到XO 表只保留这一份 引擎解码段用它生成比较指令
    { foreach (var f in rgFamMap) if (f.Item1 == iFam) return (uint)f.Item2; return 0; }

    const uint P_LIS = 15, P_ORI = 24, P_ORIS = 25, P_XORI = 26, P_XORIS = 27, P_ANDIS = 29;//载荷指令字primary
    static readonly uint[] P_LD = { 34, 40, 32, 58 };//lbz/lhz/lwz/ld
    static readonly uint[] P_ST = { 38, 44, 36, 62 };//stb/sth/stw/std
    const uint W_END = 0x4E800020u, W_RET = 0x4E800420u;//blr/bctr

    //同形状类载体按置换选操作数字节并按规范解析 寄存器字段只放真寄存器号 宽度码与噪声移第三操作数字段
    static void PutCarrierPair(List<byte> w, byte byOpC, int iPair, int iWidth)
    {
        if (byOpC >= 0x10 && byOpC <= 0x13)//LD lbz/lhz/lwz/ld宽度由primary opcode表达
        {
            uint uDisp = (uint)CarrierFmt.NoiseBits(16);//位移字段引擎不读 填噪声免得恒0成定值
            PutW(w, (P_LD[byOpC - 0x10] << 26) | ((uint)(iPair & 15) << 21) | ((uint)(iPair >> 4) << 16) | uDisp);
            return;
        }
        if (byOpC >= 0x14 && byOpC <= 0x17)//ST stb/sth/stw/std
        {
            uint uDisp = (uint)CarrierFmt.NoiseBits(16);
            PutW(w, (P_ST[byOpC - 0x14] << 26) | ((uint)(iPair & 15) << 21) | ((uint)(iPair >> 4) << 16) | uDisp);
            return;
        }
        if (byOpC >= 0x18 && byOpC <= 0x1B)//BSWAP单目寄存器放rD宽度码/噪声放rB, rA填噪声
        {
            int rd = iPair & 15;
            PutW(w, XF((uint)rd, X_BSWAP, (uint)CarrierFmt.NoiseReg(), (uint)CarrierFmt.Dup(iWidth)));
            return;
        }
        if (byOpC >= 0xF0 && byOpC <= 0xF3)//NOT eqv单目
        {
            int rd = iPair & 15;
            PutW(w, XF((uint)rd, X_EQV, (uint)CarrierFmt.NoiseReg(), (uint)CarrierFmt.Dup(iWidth)));
            return;
        }
        if (byOpC >= 0x20 && byOpC <= 0xEF)//ALU族
        {
            int rd = iPair & 15, rs = iPair >> 4;
            PutW(w, XF((uint)rd, XoOf(byOpC - (byOpC & 3)), (uint)rs, (uint)CarrierFmt.Dup(iWidth)));
            return;
        }
        throw new InvalidOperationException($"pair carrier unsupported op {byOpC:X2}");
    }

    public static byte[] EncodeLinear(byte[] b)
    {
        var w = new List<byte>(); int i = 0;
        while (i < b.Length)
        {
            byte op = b[i];
            byte byOpC = OpTable.Map(op);//操作数解析用规范操作码 选载体用操作码置换[op]
            if (op == 0x00 || op == 0x01)//END/RET blr/bctr
            {
                PutW(w, byOpC == 0x00 ? W_END : W_RET);
                i++;
                continue;
            }
            if (op == 0x08)//MOVIMM [rd][iWB][imm LE n]
            {
                int rd = b[i + 1], iWB = b[i + 2];
                if (iWB < 0 || iWB > 2) throw new InvalidOperationException($"MOVIMM iWB unsupported {iWB}");
                int n = new[] { 1, 2, 4 }[iWB];
                uint uV = 0; for (int k = 0; k < n; k++) uV |= (uint)b[i + 3 + k] << (8 * k);
                i += 3 + n;
                PutW(w, (P_LIS << 26) | ((uint)rd << 21) | ((uV >> 16) & 0xFFFF));//lis rD,hi16
                PutW(w, (P_ORI << 26) | ((uint)rd << 21) | ((uint)(CarrierFmt.Noise() | (CarrierFmt.Code[iWB] << 3)) << 16) | (uV & 0xFFFF));//ori rD,rA=宽度码+噪声,lo16
                continue;
            }
            if (op == 0x09 || op == 0x1C)//MOVREG addc / CMP xoris
            {
                int p = b[i + 1]; i += 2;
                if (byOpC == 0x09)
                    PutW(w, XF((uint)(p & 15), X_ADDC, (uint)(p >> 4), (uint)CarrierFmt.NoiseReg()));
                else
                    PutW(w, (P_XORIS << 26) | ((uint)(p & 15) << 21) | ((uint)(p >> 4) << 16) | (uint)CarrierFmt.NoiseBits(16));
                continue;
            }
            if ((op >= 0x10 && op <= 0x1B) || (op >= 0x20 && op <= 0xEF) || (op >= 0xF0 && op <= 0xF3))//同形状pair类
            {
                int iWidth = op & 3;
                int p = b[i + 1]; i += 2;
                PutCarrierPair(w, byOpC, p, iWidth);
                continue;
            }
            if (op == 0x1D || op == 0x1F)//JMPxori/CALLoris
            {
                uint uOff = (uint)(short)(b[i + 1] | (b[i + 2] << 8)); i += 3;
                PutW(w, byOpC == 0x1D ? ((P_XORI << 26) | (uOff & 0xFFFF)) : ((P_ORIS << 26) | (uOff & 0xFFFF)));
                continue;
            }
            if (op == 0x1E)//Jcc andis位移bit0落Rc位
            {
                uint uCond = b[i + 1]; uint uOff = (uint)(short)(b[i + 2] | (b[i + 3] << 8)); i += 4;
                PutW(w, (P_ANDIS << 26) | ((uCond & 15) << 21) | (uOff & 0xFFFF));
                continue;
            }
            throw new InvalidOperationException($"linear unsupported op {op:X2}");
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
            uint p = w >> 26;

            int wd = -1; bool bSt = false;//LD/ST按primary区分宽度
            for (int k = 0; k < 4; k++)
            {
                if (p == P_LD[k]) { wd = k; bSt = false; break; }
                if (p == P_ST[k]) { wd = k; bSt = true; break; }
            }
            if (wd >= 0)
            {
                o.Add((byte)((bSt ? 0x14 : 0x10) + wd));
                o.Add((byte)((((w >> 16) & 15) << 4) | ((w >> 21) & 15)));
                continue;
            }

            switch (p)
            {
                case 19://blr/bctr
                    if (w == W_END) { o.Add(0x00); continue; }
                    if (w == W_RET) { o.Add(0x01); continue; }
                    return null;
                case P_LIS://MOVIMM首字 须紧随ori
                {
                    if (i + 4 > rgIsa.Length) return null;
                    uint w2 = ReadW(rgIsa, i);
                    if ((w2 >> 26) != P_ORI) return null;
                    i += 4;
                    int rd = (int)((w >> 21) & 31);
                    int iWB = CarrierFmt.WidthOf((int)((w2 >> 16) & 31));//rA承载宽度码与噪声
                    if (rd > 15 || iWB > 2) return null;
                    uint uV = ((w & 0xFFFF) << 16) | (w2 & 0xFFFF);
                    int n = new[] { 1, 2, 4 }[iWB];
                    o.Add(0x08); o.Add((byte)rd); o.Add((byte)iWB);
                    for (int k = 0; k < n; k++) o.Add((byte)(uV >> (8 * k)));
                    continue;
                }
                case P_ORI:   return null;//仅作MOVIMM第二字
                case P_XORI:  { short sOff = (short)(w & 0xFFFF); o.Add(0x1D); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                case P_ORIS:  { short sOff = (short)(w & 0xFFFF); o.Add(0x1F); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                case P_ANDIS:
                {
                    int iCond = (int)((w >> 21) & 15); short sOff = (short)(w & 0xFFFF);
                    o.Add(0x1E); o.Add((byte)iCond); o.Add((byte)sOff); o.Add((byte)(sOff >> 8));
                    continue;
                }
                case P_XORIS:
                {
                    int iD = (int)((w >> 21) & 15), iS = (int)((w >> 16) & 15);
                    o.Add(0x1C); o.Add((byte)((iS << 4) | iD));
                    continue;
                }
                case 31://X型
                {
                    uint xo = (w >> 1) & 0x3FF;
                    int gD = (int)((w >> 21) & 15), gA = (int)((w >> 16) & 15);//寄存器字段不含宽度位宽度码在rB的[4:3] word位[14:13]
                    int iWB = CarrierFmt.WidthOf((int)((w >> 11) & 31));
                    if (xo == X_ADDC) { o.Add(0x09); o.Add((byte)((gA << 4) | gD)); continue; }
                    if (xo == X_EQV) { o.Add((byte)(0xF0 + iWB)); o.Add((byte)gD); continue; }
                    if (xo == X_BSWAP) { o.Add((byte)(0x18 + iWB)); o.Add((byte)gD); continue; }
                    int iFam = FamOf(xo);
                    if (iFam < 0) return null;
                    o.Add((byte)(iFam + iWB));
                    o.Add((byte)((gA << 4) | gD));
                    continue;
                }
                default: return null;
            }
        }
        return o.ToArray();
    }
}
