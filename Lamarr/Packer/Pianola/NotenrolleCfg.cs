namespace Lamarr.Packer;

//宽度码0..3=8/16/32/64位 位域与字节序随载体指令集
using System.Collections.Generic;

public static class NotenrolleCfg
{
    const int OpEnd = 0x00, OpRet = 0x01, OpMovImm = 0x08, OpMovReg = 0x09,
        OpLd = 0x10, OpSt = 0x14, OpBswap = 0x18, OpCmp = 0x1C,
        OpJmp = 0x1D, OpJcc = 0x1E, OpCall = 0x1F, OpNot = 0xF0;
    const int RegLo = 12, RegHi = 15, RegA0 = 0;//状态寄存器与两个临时寄存器 每次构建随机从12到15里挑三个
    public sealed class Info
    {
        public int RegState, RegId, RegAddr;
        public bool bRevChk;//检查链是否倒序
        public List<int> Entries = new List<int>();
    }
    const int PoisonLen = 26;//MOVIMM r0 7 + MOVIMM 7 + ST32 2 + JMP 3 末尾不能放END 载体解码器把END当流结束
    static readonly int[] RgImmLen = { 1, 2, 4, 8 };//毒化也写r0 0/1/3/5号内核的输出在r0 只写内存盖不到

    static byte[] Bail(byte[] rgBc, string sWhy)
    {
        Console.WriteLine($"[kern-cfg] WARN skipped: {sWhy}");
        return rgBc;
    }

    //单条指令的字节数 不识别返回-1
    internal static int ILen(byte[] b, int i)
    {
        int op = b[i];
        if (op == OpEnd || op == OpRet) return 1;
        if (op == OpMovImm)
        {
            if (i + 2 >= b.Length) return -1;
            int w = b[i + 2];
            return (w >= 0 && w < 4) ? 3 + RgImmLen[w] : -1;
        }
        if (op == OpMovReg || op == OpCmp) return 2;
        if (op >= OpLd && op < OpLd + 4) return 2;
        if (op >= OpSt && op < OpSt + 4) return 2;
        if (op >= OpBswap && op < OpBswap + 4) return 2;
        if (op >= OpNot && op < OpNot + 4) return 2;
        if (op == OpJmp || op == OpCall) return 3;
        if (op == OpJcc) return 4;
        if (op >= 0x20 && op <= 0xEF)
        {
            int w = op & 15;//四种宽度都是合法内部码 只有宽度位越界才算畸形
            return (w <= 3) ? 2 : -1;
        }
        return -1;
    }

    static void PutInstr(List<byte> w, byte[] b, int p, int L, short sOff)//单条指令原样写出 off16由调用方回填
    {
        for (int k = 0; k < L; k++) w.Add(b[p + k]);
        if (L == 3 && (b[p] == OpJmp || b[p] == OpCall)) { w[w.Count - 2] = (byte)sOff; w[w.Count - 1] = (byte)(sOff >> 8); }
        else if (L == 4 && b[p] == OpJcc) { w[w.Count - 2] = (byte)sOff; w[w.Count - 1] = (byte)(sOff >> 8); }
    }

    static void MovImm(List<byte> w, int rd, uint uV)//MOVIMM rd = uV 立即数限32位以内 所有ISA编码器都支持
    {
        w.Add(OpMovImm); w.Add((byte)(rd & 15)); w.Add(2);
        for (int i = 0; i < 4; i++) w.Add((byte)(uV >> (8 * i)));
    }

    static uint NewId(System.Random rng, HashSet<uint> hs)
    {
        while (true)
        {
            uint u = (uint)(rng.Next() & 0x7FFFFFFF) | 1u;//非零 零立即数在指令字层表达不出来
            if (hs.Add(u)) return u;
        }
    }

    static bool AddIds(List<uint> rgDst, List<uint> rgSrc)
    {
        bool b = false;
        foreach (uint u in rgSrc) if (!rgDst.Contains(u)) { rgDst.Add(u); b = true; }
        return b;
    }

    public static byte[] Apply(byte[] rgBc, int iOutOff, System.Random rng, out Info inf)//给内核字节码插桩 iOutOff=静默毒化的M偏移 info回填受保护块入口偏移 结构不认识则原样返回
    {
        inf = new Info();
        int iN3 = RegHi - RegLo + 1;//寄存器三元组与检查链形状 每次构建随机
        int iA = rng.Next(iN3);
        int iB = (iA + 1 + rng.Next(iN3 - 1)) % iN3;
        int iC = 0;
        for (int k = 0; k < iN3; k++) if (k != iA && k != iB) { iC = k; break; }
        inf.RegState = RegLo + iA; inf.RegId = RegLo + iB; inf.RegAddr = RegLo + iC;
        inf.bRevChk = rng.Next(2) == 0;
        if (rgBc.Length < 3) return Bail(rgBc, "bytecode shorter than one instruction");
        var rgPos = new List<int>();//线性解码 结束指令必须是最后一条 尾部不留别的字节
        var rgLen = new List<int>();
        int p = 0;
        bool bEnd = false;
        while (p < rgBc.Length)
        {
            int L = ILen(rgBc, p);
            if (L <= 0 || p + L > rgBc.Length) return Bail(rgBc, $"unknown opcode at {p} (0x{rgBc[p]:X2})");
            rgPos.Add(p); rgLen.Add(L);
            if (rgBc[p] == OpEnd) { bEnd = true; p += L; break; }
            p += L;
        }
        if (!bEnd || p != rgBc.Length) return Bail(rgBc, "END is not the last instruction");
        int m = rgPos.Count;
        var mpIdx = new Dictionary<int, int>();
        for (int i = 0; i < m; i++) mpIdx[rgPos[i]] = i;
        var rgTgt = new int[m];//转移目标与条件跳转直落CALL 另有返回点
        var rgFall = new int[m];
        var rgRetPos = new List<int>();//CALL之后的返回点由调用方回填
        for (int i = 0; i < m; i++)
        {
            rgTgt[i] = -1; rgFall[i] = -1;
            int op = rgBc[rgPos[i]];
            if (op != OpJmp && op != OpJcc && op != OpCall) continue;
            int o = (op == OpJcc) ? 2 : 1;//跳转与调用都是操作码后跟16位偏移
            int sOff = (short)(rgBc[rgPos[i] + o] | (rgBc[rgPos[i] + o + 1] << 8));
            if (!mpIdx.TryGetValue(rgPos[i] + sOff, out int t)) return Bail(rgBc, $"jump target {rgPos[i] + sOff} is not an instruction boundary");
            rgTgt[i] = t;
            if (op == OpJcc) { if (i + 1 >= m) return Bail(rgBc, "Jcc is the last instruction"); rgFall[i] = i + 1; }
            if (op == OpCall) { if (i + 1 >= m) return Bail(rgBc, "CALL is the last instruction"); rgRetPos.Add(i + 1); }
        }
        var stStart = new HashSet<int> { 0 };//记基本块起点与各条边的id
        for (int i = 0; i < m; i++)
        {
            if (rgTgt[i] >= 0) stStart.Add(rgTgt[i]);
            if (rgFall[i] >= 0) stStart.Add(rgFall[i]);
        }
        foreach (int r in rgRetPos) stStart.Add(r);
        var hsId = new HashSet<uint>();
        var uSite = new uint[m];//转移点自己的id 它的目标边与直落边共用这一个
        for (int i = 0; i < m; i++) if (rgTgt[i] >= 0) uSite[i] = NewId(rng, hsId);
        uint uEntry = NewId(rng, hsId);
        uint uPoison = NewId(rng, hsId);
        var rgCur = new List<uint>[m];//状态集合传播取合并点并集 转移指令先写前缀id 两条后继看到的状态都是本转移点id RET不改状态
        for (int i = 0; i < m; i++) rgCur[i] = new List<uint>();
        rgCur[0].Add(uEntry);
        for (bool bChg = true; bChg;)
        {
            bChg = false;
            for (int i = 0; i < m; i++)
            {
                if (rgCur[i].Count == 0) continue;
                int op = rgBc[rgPos[i]];
                List<uint> lsOut = rgTgt[i] >= 0 ? new List<uint> { uSite[i] } : rgCur[i];
                if (rgTgt[i] >= 0) bChg |= AddIds(rgCur[rgTgt[i]], lsOut);
                if (rgFall[i] >= 0) bChg |= AddIds(rgCur[rgFall[i]], lsOut);
                else if (op == OpRet) { foreach (int r in rgRetPos) bChg |= AddIds(rgCur[r], lsOut); }
                else if (rgTgt[i] < 0 && i + 1 < m) bChg |= AddIds(rgCur[i + 1], lsOut);
            }
        }
        var rgChk = new List<uint>[m];
        var rgEntryIdx = new List<int>();
        for (int i = 0; i < m; i++)
        {
            if (!stStart.Contains(i) || rgCur[i].Count == 0) continue;
            rgChk[i] = new List<uint>(rgCur[i]);
            rgEntryIdx.Add(i);//块入口在重排后的偏移 稍后回填成绝对偏移
        }
        var rgPre = new byte[m][];//每条指令前的插入内容 块入口是校验链加毒化块 转移前写本转移点的边id
        var rgPoison = new List<(int iBlk, int iOff)>();//毒化块末尾跳转的回填位置
        for (int i = 0; i < m; i++)
        {
            var w = new List<byte>();
            if (i == 0) MovImm(w, inf.RegState, uEntry);//入口状态 0号块自己的检查表里也有它
            if (rgChk[i] != null)
            {
                int iK = rgChk[i].Count;
                for (int iSeq = 0; iSeq < iK; iSeq++)//检查顺序每次构建随机 id按集合处理 与顺序无关
                {
                    uint uId = rgChk[i][inf.bRevChk ? (iK - 1 - iSeq) : iSeq];
                    MovImm(w, inf.RegId, uId);
                    w.Add(OpCmp); w.Add((byte)(((inf.RegId & 15) << 4) | (inf.RegState & 15)));//ZF = 状态等于该边id
                    int iOff = (iK - 1 - iSeq) * 13 + 4 + PoisonLen;//13是单条检查的字节数 4是这条条件跳转自身 相等就跳过剩下的检查与毒化块
                    w.Add(OpJcc); w.Add(1); w.Add((byte)iOff); w.Add((byte)(iOff >> 8));
                }
                //状态对不上就写错值再跳到流末 静默失败 流中不能放结束指令 解码器会当成整条流结束
                MovImm(w, RegA0, uPoison);//0号寄存器是返回值 0与1与3与5号内核的输出只在这
                MovImm(w, inf.RegId, uPoison);
                MovImm(w, inf.RegAddr, (uint)iOutOff);
                w.Add(OpSt + 2); w.Add((byte)(((inf.RegAddr & 15) << 4) | (inf.RegId & 15)));
                w.Add(OpJmp); w.Add(0); w.Add(0);
                rgPoison.Add((i, w.Count - 2));
            }
            if (rgTgt[i] >= 0) MovImm(w, inf.RegState, uSite[i]);
            rgPre[i] = w.ToArray();
        }
        var iPre0 = new int[m];//重排并回填跳转 off16是相对操作码位置的符号偏移 转移目标是目标块的前缀起点 跳过去先跑校验链
        var iOp0 = new int[m];
        int iCur = 0;
        for (int i = 0; i < m; i++) { iPre0[i] = iCur; iCur += rgPre[i].Length; iOp0[i] = iCur; iCur += rgLen[i]; }
        var rgOff = new short[m];
        for (int i = 0; i < m; i++)
        {
            if (rgTgt[i] < 0) continue;
            int iOff = iPre0[rgTgt[i]] - iOp0[i];
            if (iOff < -32768 || iOff > 32767) return Bail(rgBc, $"block jump out of int16 range ({iOff})");
            rgOff[i] = (short)iOff;
        }
        foreach (var (iBlk, iOff) in rgPoison)//毒化块末尾那条跳转统一指向流末的结束指令 只跳它的操作码免得再跑一遍校验
        {
            int iPo = iPre0[iBlk] + iOff - 1;
            int iOff2 = iOp0[m - 1] - iPo;
            if (iOff2 < -32768 || iOff2 > 32767) return Bail(rgBc, $"poison jump out of int16 range ({iOff2})");
            rgPre[iBlk][iOff] = (byte)iOff2; rgPre[iBlk][iOff + 1] = (byte)(iOff2 >> 8);
        }
        foreach (int i in rgEntryIdx) inf.Entries.Add(iPre0[i]);//回填块入口的绝对偏移
        var w2 = new List<byte>(iCur);
        for (int i = 0; i < m; i++)
        {
            w2.AddRange(rgPre[i]);
            PutInstr(w2, rgBc, rgPos[i], rgLen[i], rgOff[i]);
        }
        return w2.ToArray();
    }

    public static List<(int iPos, int iLen, int iTgt, int iFall)> Disasm(byte[] rgBc)//指令级反汇编 iTgt/iFall是指令下标-1表示无 只认指令边界上的跳转 整段线性解码 不识别的编码才停
    {
        var ls = new List<(int, int, int, int)>();
        var rgPos = new List<int>();
        var rgLen = new List<int>();
        int p = 0;
        while (p < rgBc.Length)
        {
            int L = ILen(rgBc, p);
            if (L <= 0 || p + L > rgBc.Length) return ls;
            rgPos.Add(p); rgLen.Add(L);
            p += L;
        }
        var mp = new Dictionary<int, int>();
        for (int i = 0; i < rgPos.Count; i++) mp[rgPos[i]] = i;
        for (int i = 0; i < rgPos.Count; i++)
        {
            int op = rgBc[rgPos[i]], iTgt = -1, iFall = -1;
            if (op == OpJmp || op == OpJcc)
            {
                int o = (op == OpJmp) ? 1 : 2;
                int sOff = (short)(rgBc[rgPos[i] + o] | (rgBc[rgPos[i] + o + 1] << 8));
                iTgt = mp.TryGetValue(rgPos[i] + sOff, out int t) ? t : -1;
                if (op == OpJcc && i + 1 < rgPos.Count) iFall = i + 1;
            }
            ls.Add((rgPos[i], rgLen[i], iTgt, iFall));
        }
        return ls;
    }
}
