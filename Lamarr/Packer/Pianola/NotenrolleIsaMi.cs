namespace Lamarr.Packer;

using System.Collections.Generic;

//MIPS64指令字层内部字节码与指令字流 字节序be 位域位置见下方字位常量 宽度码在rt字段与ori的rs字段 载荷型偏移放分支imm16 单目宽度码放rs 寄存器放rt
public static class NotenrolleIsaMi
{
    static void PutW(List<byte> w, uint v)
    { w.Add((byte)(v >> 24)); w.Add((byte)(v >> 16)); w.Add((byte)(v >> 8)); w.Add((byte)v); }
    static uint ReadW(byte[] b, int i) => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    static uint R(int iFunct, int rd, int rs, int rt)//R型word=funct|rd<<11|rt<<16|rs<<21 opcode0
        => ((uint)iFunct & 0x3F) | ((uint)(rd & 31) << 11) | ((uint)(rt & 31) << 16) | ((uint)(rs & 31) << 21);
    static uint I(int op, int rs, int rt, int iImm)//I型word=op<<26|rs<<21|rt<<16|imm16
        => ((uint)op << 26) | ((uint)(rs & 31) << 21) | ((uint)(rt & 31) << 16) | ((uint)iImm & 0xFFFF);

    internal static readonly int[] rgFamMap = {//族表13项 funct互不相同 操作码0按内部族码0x20..0xE0排列
        0x20,0x22,0x24,0x25,0x26,0x27,0x2B,0x2D,0x04,0x06,0x07,0x14,0x16 };
    //不取乘除这类操作码 rd位是累加器选择子

    const uint W_END = 0x03E00008u;//jr $ra
    const uint W_RET = 0x03200008u;//jr $25
    const int F_MOVREG = 0x21;//addu
    const int F_CMP = 0x2A;//slt
    internal const int F_BSWAP = 0x36;//字节序指令字合成SPECIAL funct=36h
    static readonly int[] LdOp = { 0x20, 0x21, 0x23, 0x37 };//lblhlwld
    static readonly int[] StOp = { 0x28, 0x29, 0x2B, 0x3F };//sbshswsd
    const int OP_LUI = 0x0F, OP_ORI = 0x0D, OP_XORI = 0x0E;
    const int OP_BEQ = 0x04, OP_BNE = 0x05;

    static void PutCarrierPair(List<byte> w, byte byOpC, int iPair, int iWidth)//同形状类载体按操作码置换[op]选操作数字节 宽度码放第三操作数字段 ALU用rt 单目用rs 访存类宽度在操作码本身 imm16填噪声
    {
        if (byOpC >= 0x10 && byOpC <= 0x13) { PutW(w, I(LdOp[byOpC - 0x10], iPair >> 4, iPair & 15, CarrierFmt.NoiseBits(16))); return; }
        if (byOpC >= 0x14 && byOpC <= 0x17) { PutW(w, I(StOp[byOpC - 0x14], iPair >> 4, iPair & 15, CarrierFmt.NoiseBits(16))); return; }
        if (byOpC >= 0x18 && byOpC <= 0x1B)//BSWAP单目
        {
            PutW(w, R(F_BSWAP, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;
        }
        if (byOpC >= 0xF0 && byOpC <= 0xF3)//NOT xori单目
        {
            PutW(w, I(OP_XORI, CarrierFmt.Dup(iWidth), iPair & 15, CarrierFmt.NoiseBits(16))); return;//rs = 宽度码+噪声rt = 真寄存器imm16 = 噪声
        }
        if (byOpC >= 0x20 && byOpC <= 0xEF)
        {
            PutW(w, R(rgFamMap[(byOpC - (byOpC & 3) - 0x20) / 0x10], iPair & 15, iPair >> 4, CarrierFmt.Dup(iWidth))); return;
        }
        throw new InvalidOperationException($"MIPS pair carrier unsupported op {byOpC:X2}");
    }

    public static byte[] EncodeLinear(byte[] b)
    {
        var w = new List<byte>(); int i = 0;
        while (i < b.Length)
        {
            byte op = b[i];
            byte byOpC = OpTable.Map(op);//操作数解析用规范操作码 选载体用操作码置换[op]
            if (op == 0x00 || op == 0x01) { PutW(w, byOpC == 0x00 ? W_END : W_RET); i++; continue; }//END/RET jr $ra / jr $25
            if (op == 0x08)//MOVIMM lui rd,hi16 + ori rd,rs=iWB,lo16
            {
                int rd = b[i + 1], iWB = b[i + 2];
                if (iWB < 0 || iWB > 2) throw new InvalidOperationException($"MIPS MOVIMM iWB unsupported {iWB}");
                int n = new[] { 1, 2, 4 }[iWB];
                uint uV = 0; for (int k = 0; k < n; k++) uV |= (uint)b[i + 3 + k] << (8 * k);
                i += 3 + n;
                PutW(w, I(OP_LUI, 0, rd, (int)(uV >> 16)));
                PutW(w, I(OP_ORI, CarrierFmt.Dup(iWB), rd, (int)(uV & 0xFFFF)));//ori.rs承载宽度码与噪声
                continue;
            }
            if (op == 0x09) { int p = b[i + 1]; i += 2; if (byOpC == 0x09) PutW(w, R(F_MOVREG, p & 15, p >> 4, 0)); else PutW(w, R(F_CMP, p & 15, p >> 4, 0)); continue; }//MOVREG与CMP互换(组内09/1C)
            if (op == 0x1C) { int p = b[i + 1]; i += 2; if (byOpC == 0x1C) PutW(w, R(F_CMP, p & 15, p >> 4, 0)); else PutW(w, R(F_MOVREG, p & 15, p >> 4, 0)); continue; }//CMP与MOVREG互换
            if ((op >= 0x10 && op <= 0x1B) || (op >= 0x20 && op <= 0xEF) || (op >= 0xF0 && op <= 0xF3))//同形状类
            {
                int iWidth = op & 3; int p = b[i + 1]; i += 2;//8/16位也是原生能力 不按宽度拒绝
                PutCarrierPair(w, byOpC, p, iWidth);
                continue;
            }
            if (op == 0x1D || op == 0x1F)//JMP与CALL同形 操作码置换组内1D与1F互换
            {
                uint uOff = (uint)(short)(b[i + 1] | (b[i + 2] << 8)); i += 3;
                PutW(w, byOpC == 0x1D ? I(OP_BEQ, 0, 0, (int)(uOff & 0xFFFF)) : I(OP_BNE, 0, 0, (int)(uOff & 0xFFFF)));
                continue;
            }
            if (op == 0x1E)//Jcc自成一类
            {
                uint uCond = b[i + 1]; uint uOff = (uint)(short)(b[i + 2] | (b[i + 3] << 8)); i += 4;
                PutW(w, I(OP_BEQ, 0, (int)((uCond & 7u) + 1u), (int)(uOff & 0xFFFF)));
                continue;
            }
            throw new InvalidOperationException($"MIPS opcode not covered {op:X2}");
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
            uint uOpc = w >> 26;
            if (uOpc == (uint)OP_LUI)//MOVIMM lui + ori rs=iWB
            {
                if (i + 4 > rgIsa.Length) return null;
                uint w2 = ReadW(rgIsa, i);
                if ((w2 >> 26) != (uint)OP_ORI) return null;
                i += 4;
                int rd = (int)((w >> 16) & 31);
                int iWB = CarrierFmt.WidthOf((int)((w2 >> 21) & 31));//ori.rs承载宽度码与噪声
                if (rd > 15 || iWB > 2) return null;
                uint uV = ((w & 0xFFFF) << 16) | (w2 & 0xFFFF);
                int n = new[] { 1, 2, 4 }[iWB];
                o.Add(0x08); o.Add((byte)rd); o.Add((byte)iWB);
                for (int k = 0; k < n; k++) o.Add((byte)(uV >> (8 * k)));
                continue;
            }
            if (uOpc == (uint)OP_ORI) return null;//仅作MOVIMM第二字
            if (uOpc == (uint)OP_XORI)//NOT
            {
                int gD = (int)((w >> 16) & 31);
                int iWB = CarrierFmt.WidthOf((int)((w >> 21) & 31));
                if (gD > 15) return null;
                o.Add((byte)(0xF0 + iWB)); o.Add((byte)gD);
                continue;
            }
            if (uOpc == (uint)OP_BEQ)//JMP rt=0 Jcc rt=cond+1
            {
                int rt = (int)((w >> 16) & 31);
                short sOff = (short)(w & 0xFFFF);
                if (rt == 0) { o.Add(0x1D); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                o.Add(0x1E); o.Add((byte)(rt - 1));
                o.Add((byte)sOff); o.Add((byte)(sOff >> 8));
                continue;
            }
            if (uOpc == (uint)OP_BNE)//CALL
            {
                short sOff = (short)(w & 0xFFFF);
                o.Add(0x1F); o.Add((byte)sOff); o.Add((byte)(sOff >> 8));
                continue;
            }
            int iLd = -1, iSt = -1;
            for (int k = 0; k < 4; k++)
            {
                if (uOpc == (uint)LdOp[k]) { iLd = k; break; }
                if (uOpc == (uint)StOp[k]) { iSt = k; break; }
            }
            if (iLd >= 0 || iSt >= 0)
            {
                int rs = (int)((w >> 21) & 15);
                int rt = (int)((w >> 16) & 15);
                o.Add((byte)((iLd >= 0 ? 0x10 : 0x14) + (iLd >= 0 ? iLd : iSt)));
                o.Add((byte)((rs << 4) | rt));
                continue;
            }
            if (uOpc == 0)//R型
            {
                int iFunct = (int)(w & 0x3F);
                int rd = (int)((w >> 11) & 31), rs = (int)((w >> 21) & 31), rt = (int)((w >> 16) & 31);
                int iWB = CarrierFmt.WidthOf(rt);//rt承载宽度码与噪声
                if (iFunct == F_MOVREG) { o.Add(0x09); o.Add((byte)(((rs & 15) << 4) | (rd & 15))); continue; }
                if (iFunct == F_CMP) { o.Add(0x1C); o.Add((byte)(((rs & 15) << 4) | (rd & 15))); continue; }
                if (iFunct == F_BSWAP)//字节序指令字BSWAP
                {
                    if (rd > 15) return null;
                    o.Add((byte)(0x18 + iWB)); o.Add((byte)rd);
                    continue;
                }
                int iFam = -1;
                for (int k = 0; k < rgFamMap.Length; k++) if (rgFamMap[k] == iFunct) { iFam = 0x20 + k * 0x10; break; }
                if (iFam < 0) return null;
                if (rd > 15 || rs > 15) return null;//寄存器字段是真寄存器号
                o.Add((byte)(iFam + iWB));
                o.Add((byte)((rs << 4) | rd));
                continue;
            }
            return null;
        }
        return o.ToArray();
    }
}
