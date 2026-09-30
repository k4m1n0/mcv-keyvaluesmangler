namespace Lamarr.Packer;

using System.Collections.Generic;

//LoongArch64指令字层内部字节码与指令字流 字节序le 位域位置见下方字位常量 宽度码在rk字段与ori的rj字段 载荷型偏移放pcaddu12i的si20 单目宽度码放rk 寄存器放rd
public static class NotenrolleIsaLa
{
    static void PutW(List<byte> w, uint v)
    { w.Add((byte)v); w.Add((byte)(v >> 8)); w.Add((byte)(v >> 16)); w.Add((byte)(v >> 24)); }
    static uint ReadW(byte[] b, int i) => (uint)b[i] | ((uint)b[i + 1] << 8) | ((uint)b[i + 2] << 16) | ((uint)b[i + 3] << 24);

    internal static readonly uint[] rgFamMap = {
        0x00108000u,0x00118000u,0x001D8000u,0x00218000u,0x00228000u,
        0x00148000u,0x00150000u,0x00158000u,0x00170000u,0x00178000u,
        0x00180000u,0x00160000u,0x00188000u };
    static uint La3R(uint uBase, int rd, int rj, int rk)
        => uBase | ((uint)(rd & 31)) | ((uint)(rj & 31) << 5) | ((uint)(rk & 31) << 10);

    const uint W_END = 0x4C000020u;//jirl $zero,$ra,0
    const uint W_RET = 0x4C000021u;//jirl $ra,$ra,0
    const uint B_MOVREG = 0x00138000u, B_NOT = 0x001C8000u, B_CMP = 0x00120000u;
    //字节序指令字合成用revb形态 取一个族表与现有指令都没占的空档 三寄存器判别无歧义
    internal const uint B_BSWAP = 0x001E8000u;
    const uint B_LU12I = 0x14000000u, B_ORI = 0x03800000u, B_PCADD = 0x1C000000u;
    static readonly uint[] B_LD = { 0x28000000u, 0x28400000u, 0x28800000u, 0x28C00000u };//ld.b/h/w/d
    static readonly uint[] B_ST = { 0x29000000u, 0x29400000u, 0x29800000u, 0x29C00000u };//st.b/h/w/d

    //同形状类载体按操作码置换[op]选操作数字节并按规范解析 宽度码放第三操作数字段rk 访存类宽度在操作码本身 imm12填噪声
    static void PutCarrierPair(List<byte> w, byte byOpC, int iPair, int iWidth)
    {
        if (byOpC >= 0x10 && byOpC <= 0x13) { PutW(w, B_LD[byOpC - 0x10] | ((uint)(iPair & 15)) | ((uint)(iPair >> 4) << 5) | ((uint)CarrierFmt.NoiseBits(12) << 10)); return; }
        if (byOpC >= 0x14 && byOpC <= 0x17) { PutW(w, B_ST[byOpC - 0x14] | ((uint)(iPair & 15)) | ((uint)(iPair >> 4) << 5) | ((uint)CarrierFmt.NoiseBits(12) << 10)); return; }
        if (byOpC >= 0x18 && byOpC <= 0x1B)//BSWAP单目
        {
            PutW(w, La3R(B_BSWAP, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;
        }
        if (byOpC >= 0xF0 && byOpC <= 0xF3)//NOT单目
        {
            //rd放真寄存器 rj放噪声 rk放宽度码与噪声
            PutW(w, La3R(B_NOT, iPair & 15, CarrierFmt.NoiseReg(), CarrierFmt.Dup(iWidth))); return;
        }
        if (byOpC >= 0x20 && byOpC <= 0xEF)
        {
            PutW(w, La3R(rgFamMap[(byOpC - (byOpC & 3) - 0x20) / 0x10], iPair & 15, iPair >> 4, CarrierFmt.Dup(iWidth))); return;
        }
        throw new InvalidOperationException($"LA pair carrier unsupported op {byOpC:X2}");
    }

    public static byte[] EncodeLinear(byte[] b)
    {
        var w = new List<byte>(); int i = 0;
        while (i < b.Length)
        {
            byte op = b[i];
            byte byOpC = OpTable.Map(op);//操作数解析用规范操作码 选载体用操作码置换[op]
            if (op == 0x00 || op == 0x01) { PutW(w, byOpC == 0x00 ? W_END : W_RET); i++; continue; }//END/RET jirl $zero/$ra
            if (op == 0x08)//MOVIMM lu12i.w rd,hi20 + ori rd,rj=iWB,lo12
            {
                int rd = b[i + 1], iWB = b[i + 2];
                if (iWB < 0 || iWB > 2) throw new InvalidOperationException($"LA MOVIMM iWB unsupported {iWB}");
                int n = new[] { 1, 2, 4 }[iWB];
                uint uV = 0; for (int k = 0; k < n; k++) uV |= (uint)b[i + 3 + k] << (8 * k);
                i += 3 + n;
                PutW(w, B_LU12I | ((uint)rd) | (((uV >> 12) & 0xFFFFFu) << 5));
                PutW(w, B_ORI | ((uint)rd) | ((uint)CarrierFmt.Dup(iWB) << 5) | ((uV & 0xFFFu) << 10));
                continue;
            }
            if (op == 0x09) { int p = b[i + 1]; i += 2; if (byOpC == 0x09) PutW(w, La3R(B_MOVREG, p & 15, p >> 4, p >> 4)); else PutW(w, La3R(B_CMP, p & 15, p >> 4, 0)); continue; }//MOVREG与CMP互换(组内09/1C)
            if (op == 0x1C) { int p = b[i + 1]; i += 2; if (byOpC == 0x1C) PutW(w, La3R(B_CMP, p & 15, p >> 4, 0)); else PutW(w, La3R(B_MOVREG, p & 15, p >> 4, p >> 4)); continue; }//CMP与MOVREG互换
            if ((op >= 0x10 && op <= 0x1B) || (op >= 0x20 && op <= 0xEF) || (op >= 0xF0 && op <= 0xF3))//同形状类
            {
                //8位与16位也是原生能力 不按宽度拒绝
                int iWidth = op & 3; int p = b[i + 1]; i += 2;
                PutCarrierPair(w, byOpC, p, iWidth);
                continue;
            }
            if (op == 0x1D || op == 0x1F)//JMP与CALL同形 操作码置换组内1D与1F互换
            {
                uint uOff = (uint)(short)(b[i + 1] | (b[i + 2] << 8)); i += 3;
                PutW(w, B_PCADD | (byOpC == 0x1D ? 0u : 2u) | ((uOff & 0xFFFFu) << 5));
                continue;
            }
            if (op == 0x1E)//Jcc自成一类
            {
                uint uCond = b[i + 1]; uint uOff = (uint)(short)(b[i + 2] | (b[i + 3] << 8)); i += 4;
                PutW(w, B_PCADD | 1u | ((((uCond & 7u) << 16) | (uOff & 0xFFFFu)) << 5));
                continue;
            }
            throw new InvalidOperationException($"LA opcode not covered {op:X2}");
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
            uint uT22 = w >> 22;
            if (uT22 >= (B_LD[0] >> 22) && uT22 <= (B_LD[3] >> 22))
            {
                o.Add((byte)(0x10 + (uT22 - (B_LD[0] >> 22))));
                o.Add((byte)((((w >> 5) & 15) << 4) | (w & 15)));
                continue;
            }
            if (uT22 >= (B_ST[0] >> 22) && uT22 <= (B_ST[3] >> 22))
            {
                o.Add((byte)(0x14 + (uT22 - (B_ST[0] >> 22))));
                o.Add((byte)((((w >> 5) & 15) << 4) | (w & 15)));
                continue;
            }
            uint uT25 = w >> 25;
            if (uT25 == (B_LU12I >> 25))//lu12i.w+ori MOVIMM
            {
                if (i + 4 > rgIsa.Length) return null;
                uint w2 = ReadW(rgIsa, i);
                if ((w2 >> 25) != (B_ORI >> 25)) return null;
                i += 4;
                int rd = (int)(w & 31);
                int iWB = CarrierFmt.WidthOf((int)((w2 >> 5) & 31));//ori.rj承载宽度码与噪声
                if (rd > 15 || iWB > 2) return null;
                uint uV = (((w >> 5) & 0xFFFFFu) << 12) | ((w2 >> 10) & 0xFFFu);
                int n = new[] { 1, 2, 4 }[iWB];
                o.Add(0x08); o.Add((byte)rd); o.Add((byte)iWB);
                for (int k = 0; k < n; k++) o.Add((byte)(uV >> (8 * k)));
                continue;
            }
            if (uT25 == (B_ORI >> 25)) return null;//仅作MOVIMM第二字
            if (uT25 == (B_PCADD >> 25))//pcaddu12i载荷
            {
                int iTag = (int)(w & 31);
                uint uPay = (w >> 5) & 0xFFFFFu;
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
            if (uT22 == 0)//3R族用bit15+区分
            {
                uint kB = w >> 15;
                int rd = (int)(w & 31), rj = (int)((w >> 5) & 31), rk = (int)((w >> 10) & 31);
                if (kB == (B_MOVREG >> 15)) { o.Add(0x09); o.Add((byte)(((rj & 15) << 4) | (rd & 15))); continue; }
                if (kB == (B_CMP >> 15)) { o.Add(0x1C); o.Add((byte)(((rj & 15) << 4) | (rd & 15))); continue; }
                if (kB == (B_NOT >> 15))
                {
                    int iWB = CarrierFmt.WidthOf(rk);//rk承载宽度码与噪声
                    if (rd > 15) return null;
                    o.Add((byte)(0xF0 + iWB)); o.Add((byte)rd);
                    continue;
                }
                if (kB == (B_BSWAP >> 15))//字节序指令字BSWAP
                {
                    int iWB = CarrierFmt.WidthOf(rk);
                    if (rd > 15) return null;
                    o.Add((byte)(0x18 + iWB)); o.Add((byte)rd);
                    continue;
                }
                int iFam = -1;
                for (int k = 0; k < rgFamMap.Length; k++) if ((rgFamMap[k] >> 15) == kB) { iFam = 0x20 + k * 0x10; break; }
                if (iFam < 0) return null;
                if (rd > 15 || rj > 15) return null;//寄存器字段是真寄存器号
                o.Add((byte)(iFam + CarrierFmt.WidthOf(rk)));
                o.Add((byte)((rj << 4) | rd));
                continue;
            }
            return null;
        }
        return o.ToArray();
    }
}
