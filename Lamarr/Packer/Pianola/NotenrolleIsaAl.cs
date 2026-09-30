namespace Lamarr.Packer;

using System.Collections.Generic;

//指令字层负责内部字节码与指令字流互转 宽度码放在第三操作数字段 字节序为小端 位域位置见下方常量 载荷型偏移放21位位移 寄存器字段放真寄存器号 宽度码与噪声放第三操作数字段 立即数搬移用取地址指令的字段承载宽度码
public static class NotenrolleIsaAl
{
    static void PutW(List<byte> w, uint v)
    { w.Add((byte)(v >> 24)); w.Add((byte)(v >> 16)); w.Add((byte)(v >> 8)); w.Add((byte)v); }
    static uint ReadW(byte[] b, int i) => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    internal static readonly uint[] rgFamMap = {
        0x40220000u,0x40220120u,0x4C220000u,0x44220100u,0x44220500u,
        0x44220000u,0x44220400u,0x44220800u,0x48220720u,0x48220680u,
        0x48220780u,0x44220900u,0x40220400u };//addlsublmullbicornotandbisxorsllsrlsraeqvaddq
    static readonly int[] rgFamIdx = { 0x20,0x30,0x40,0x50,0x60,0x70,0x80,0x90,0xA0,0xB0,0xC0,0xD0,0xE0 };
    internal const uint T_MOVREG = 0x4C220603u;//umulh $1,$2,$3
    internal const uint T_NOT = 0x40220043u;//s4addl $1,$2,$3
    internal const uint T_CMP = 0x402205A3u;//cmpeq $1,$2,$3
    internal const uint T_BSWAP = 0x4C220800u;//字节序指令字合成 三操作数形态
    internal const uint RA_RC = (31u << 21) | 31u;
    internal const uint RB = 31u << 16;
    static uint Canon(uint uTpl, int rc, int ra) => (uTpl & ~RA_RC) | ((uint)(ra & 31) << 21) | ((uint)(rc & 31));
    static uint Canon3(uint uTpl, int rc, int ra, int rb)//rb=宽度码字段 模板匹配与展开都算它
        => (uTpl & ~(RA_RC | RB)) | ((uint)(ra & 31) << 21) | ((uint)(rb & 31) << 16) | ((uint)(rc & 31));

    const uint W_END = 0x6BFA8001u;//ret $31,,1
    const uint W_RET = 0x6BFA0000u;//jmp $31,,0
    const uint T_PAY = 0xE0000000u;//blbc Ra=0 操作码0x38<<26 21位位移 [20:18]=tag [17:0]=载荷
    const uint T_LDAH = 0x24000000u, T_LDA = 0x20000000u;
    static readonly uint[] LdOp = { 0x28000000u, 0x30000000u, 0xA0000000u, 0xA4000000u };//ldbu/ldwu/ldl/ldq
    static readonly uint[] StOp = { 0x38000000u, 0x34000000u, 0xB0000000u, 0xB4000000u };//stb/stw/stl/stq
    static uint Mem(uint uOp, int ra, int rb) => uOp | ((uint)(ra & 31) << 21) | ((uint)(rb & 31) << 16);

    //同形状类载体按置换选操作数字节并按规范解析 宽度码放第三操作数字段 访存类的宽度在操作码本身 位移字段填噪声
    static void PutCarrierPair(List<byte> w, byte byOpC, int iPair, int iWidth)
    {
        if (byOpC >= 0x10 && byOpC <= 0x13) { PutW(w, Mem(LdOp[byOpC - 0x10], iPair & 15, iPair >> 4) | (uint)CarrierFmt.NoiseBits(16)); return; }
        if (byOpC >= 0x14 && byOpC <= 0x17) { PutW(w, Mem(StOp[byOpC - 0x14], iPair & 15, iPair >> 4) | (uint)CarrierFmt.NoiseBits(16)); return; }
        if (byOpC >= 0x18 && byOpC <= 0x1B)//BSWAP单目
        {
            PutW(w, Canon3(T_BSWAP, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;
        }
        if (byOpC >= 0xF0 && byOpC <= 0xF3)//NOT单目
        {
            PutW(w, Canon3(T_NOT, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;//rc放真寄存器 ra放噪声 rb放宽度码与噪声
        }
        if (byOpC >= 0x20 && byOpC <= 0xEF)
        {
            PutW(w, Canon3(rgFamMap[(byOpC - (byOpC & 3) - 0x20) / 0x10], iPair & 15, iPair >> 4, CarrierFmt.Dup(iWidth))); return;
        }
        throw new InvalidOperationException($"Alpha pair carrier unsupported op {byOpC:X2}");
    }

    public static byte[] EncodeLinear(byte[] b)
    {
        var w = new List<byte>(); int i = 0;
        while (i < b.Length)
        {
            byte op = b[i];
            byte byOpC = OpTable.Map(op);//操作数解析用规范操作码 选载体用操作码置换[op]
            if (op == 0x00 || op == 0x01) { PutW(w, byOpC == 0x00 ? W_END : W_RET); i++; continue; }//END/RET ret/jmp
            if (op == 0x08)//MOVIMM ldah rd,hi + lda rd,lo
            {
                int rd = b[i + 1], iWB = b[i + 2];
                if (iWB < 0 || iWB > 2) throw new InvalidOperationException($"AL MOVIMM iWB unsupported {iWB}");
                int n = new[] { 1, 2, 4 }[iWB];
                uint uV = 0; for (int k = 0; k < n; k++) uV |= (uint)b[i + 3 + k] << (8 * k);
                i += 3 + n;
                uint hi = (uV >> 16) & 0xFFFF, lo = uV & 0xFFFF;
                if ((lo & 0x8000) != 0) hi = (hi + 1) & 0xFFFF;//补偿lda的disp16符号扩展
                PutW(w, T_LDAH | ((uint)rd << 21) | (31u << 16) | hi);
                PutW(w, T_LDA | ((uint)rd << 21) | ((uint)CarrierFmt.Dup(iWB) << 16) | lo);
                continue;
            }
            if (op == 0x09) { int p = b[i + 1]; i += 2; if (byOpC == 0x09) PutW(w, Canon(T_MOVREG, p & 15, p >> 4)); else PutW(w, Canon(T_CMP, p & 15, p >> 4)); continue; }//MOVREG与CMP互换(组内09/1C)
            if (op == 0x1C) { int p = b[i + 1]; i += 2; if (byOpC == 0x1C) PutW(w, Canon(T_CMP, p & 15, p >> 4)); else PutW(w, Canon(T_MOVREG, p & 15, p >> 4)); continue; }//CMP与MOVREG互换
            if ((op >= 0x10 && op <= 0x1B) || (op >= 0x20 && op <= 0xEF) || (op >= 0xF0 && op <= 0xF3))//同形状类
            {
                int iWidth = op & 3; int p = b[i + 1]; i += 2;//8/16位也是原生能力 不按宽度拒绝
                PutCarrierPair(w, byOpC, p, iWidth);
                continue;
            }
            if (op == 0x1D || op == 0x1F)//JMP与CALL同形 操作码置换组内1D与1F互换
            {
                uint uOff = (uint)(short)(b[i + 1] | (b[i + 2] << 8)); i += 3;
                PutW(w, byOpC == 0x1D ? (T_PAY | (uOff & 0xFFFFu)) : (T_PAY | (2u << 19) | (uOff & 0xFFFFu)));
                continue;
            }
            if (op == 0x1E)//Jcc自成一类
            {
                uint uCond = b[i + 1]; uint uOff = (uint)(short)(b[i + 2] | (b[i + 3] << 8)); i += 4;
                PutW(w, T_PAY | (1u << 19) | (((uCond & 7u) << 16) | (uOff & 0xFFFFu)));
                continue;
            }
            throw new InvalidOperationException($"AL opcode not covered {op:X2}");
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
            uint uOp = w >> 26;
            if (uOp == (LdOp[0] >> 26) || uOp == (LdOp[1] >> 26) || uOp == (LdOp[2] >> 26) || uOp == (LdOp[3] >> 26))//LD/ST按操作码直接映射宽度
            {
                int k = uOp == (LdOp[0] >> 26) ? 0 : uOp == (LdOp[1] >> 26) ? 1 : uOp == (LdOp[2] >> 26) ? 2 : 3;
                o.Add((byte)(0x10 + k));
                o.Add((byte)((((w >> 16) & 15) << 4) | ((w >> 21) & 15)));
                continue;
            }
            if (uOp == (StOp[0] >> 26) || uOp == (StOp[1] >> 26) || uOp == (StOp[2] >> 26) || uOp == (StOp[3] >> 26))
            {
                int k = uOp == (StOp[0] >> 26) ? 0 : uOp == (StOp[1] >> 26) ? 1 : uOp == (StOp[2] >> 26) ? 2 : 3;
                o.Add((byte)(0x14 + k));
                o.Add((byte)((((w >> 16) & 15) << 4) | ((w >> 21) & 15)));
                continue;
            }
            if (uOp == (T_LDAH >> 26))//ldah+lda MOVIMM
            {
                if (i + 4 > rgIsa.Length) return null;
                uint w2 = ReadW(rgIsa, i);
                if ((w2 >> 26) != (T_LDA >> 26)) return null;
                i += 4;
                int rd = (int)((w >> 21) & 31);
                int iWB = CarrierFmt.WidthOf((int)((w2 >> 16) & 31));//lda.rb承载宽度码与噪声
                if (rd > 15 || iWB > 2) return null;
                uint hi = w & 0xFFFF, lo = w2 & 0xFFFF;
                uint uV = (hi << 16);
                if ((lo & 0x8000) != 0) uV = (uV - 0x10000u) & 0xFFFFFFFFu;//撤销+1补偿
                uV = (uV + (lo & 0xFFFF)) & 0xFFFFFFFFu;
                int n = new[] { 1, 2, 4 }[iWB];
                o.Add(0x08); o.Add((byte)rd); o.Add((byte)iWB);
                for (int k = 0; k < n; k++) o.Add((byte)(uV >> (8 * k)));
                continue;
            }
            if (uOp == (T_LDA >> 26)) return null;//孤立lda
            if (uOp == (T_PAY >> 26))//blbc tag在bit20:19载荷19位
            {
                int iTag = (int)((w >> 19) & 3);
                uint uPay = w & 0x7FFFFu;
                short sOff = (short)(uPay & 0xFFFF);
                if (iTag == 0) { o.Add(0x1D); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                if (iTag == 2) { o.Add(0x1F); o.Add((byte)sOff); o.Add((byte)(sOff >> 8)); continue; }
                if (iTag == 1)
                {
                    o.Add(0x1E); o.Add((byte)((uPay >> 16) & 7));
                    o.Add((byte)sOff); o.Add((byte)(sOff >> 8));
                    continue;
                }
                return null;
            }
            //三操作数模板
            {
                int rc = (int)(w & 31), ra = (int)((w >> 21) & 31), rb = (int)((w >> 16) & 31);
                uint uCanon = w & ~(RA_RC | RB);
                if (uCanon == (T_MOVREG & ~(RA_RC | RB))) { o.Add(0x09); o.Add((byte)(((ra & 15) << 4) | (rc & 15))); continue; }
                if (uCanon == (T_CMP & ~(RA_RC | RB))) { o.Add(0x1C); o.Add((byte)(((ra & 15) << 4) | (rc & 15))); continue; }
                int iWB = CarrierFmt.WidthOf(rb);
                if (uCanon == (T_NOT & ~(RA_RC | RB)))
                {
                    if (rc > 15) return null;
                    o.Add((byte)(0xF0 + iWB)); o.Add((byte)rc);
                    continue;
                }
                if (uCanon == (T_BSWAP & ~(RA_RC | RB)))//字节序指令字BSWAP模板
                {
                    if (rc > 15) return null;
                    o.Add((byte)(0x18 + iWB)); o.Add((byte)rc);
                    continue;
                }
                int iFam = -1;
                for (int k = 0; k < rgFamMap.Length; k++) if ((rgFamMap[k] & ~(RA_RC | RB)) == uCanon) { iFam = rgFamIdx[k]; break; }
                if (iFam < 0) return null;
                if (rc > 15 || ra > 15) return null;//寄存器字段是真寄存器号
                o.Add((byte)(iFam + iWB));
                o.Add((byte)((ra << 4) | rc));
                continue;
            }
        }
        return o.ToArray();
    }
}
