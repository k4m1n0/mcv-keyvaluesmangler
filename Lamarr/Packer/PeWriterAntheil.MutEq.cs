namespace Lamarr.Packer;

//扰动后的方法体比对 两版规范成逻辑指令序列与分支目标位置 按对齐关系映射过去逐条比对 重定位错误或行为改动都拒绝 打包期丢掉整个变异
internal partial class PeWriterAntheil
{
    private struct IlIns
    {
        public int iPos;
        public int iOp;
        public long lArg;
        public int iTgt;
        public int[]? rgSw;
    }

    private static bool IlCanon(byte[] rgIl, out List<IlIns> rg, out string sWhy)//短与长形式归一 分支目标记录成位置
    {
        rg = new List<IlIns>();
        sWhy = "";
        var setPos = new HashSet<int>();
        int p = 0;
        while (p < rgIl.Length)
        {
            int l0 = IlLen(rgIl, p);
            if (l0 <= 0 || p + l0 > rgIl.Length) { sWhy = $"badlen@{p}"; return false; }
            if (!setPos.Add(p)) { sWhy = $"dup@{p}"; return false; }
            p += l0;
        }
        p = 0;
        while (p < rgIl.Length)
        {
            int l = IlLen(rgIl, p);
            byte op = rgIl[p];
            var it = new IlIns { iPos = p, iOp = op, iTgt = -1, rgSw = null, lArg = 0 };
            if (op >= 0x02 && op <= 0x05) { it.iOp = 0x0E; it.lArg = op - 0x02; }
            else if (op >= 0x06 && op <= 0x09) { it.iOp = 0x11; it.lArg = op - 0x06; }
            else if (op >= 0x0A && op <= 0x0D) { it.iOp = 0x13; it.lArg = op - 0x0A; }
            else if (op == 0x0E || op == 0x11 || op == 0x13) { it.lArg = rgIl[p + 1]; }
            else if (op >= 0x15 && op <= 0x1E) { it.iOp = 0x1F; it.lArg = op == 0x15 ? -1 : op - 0x16; }
            else if (op == 0x1F) { it.lArg = (sbyte)rgIl[p + 1]; }
            else if (op == 0x20) { it.lArg = BitConverter.ToInt32(rgIl, p + 1); }
            else if (op >= 0x2B && op <= 0x44)
            {
                bool bShort = op <= 0x37;
                it.iOp = bShort ? op + 0x0D : op;//短分支归一到长形式
                it.iTgt = bShort ? p + 2 + (sbyte)rgIl[p + 1] : p + 5 + BitConverter.ToInt32(rgIl, p + 1);
            }
            else if (op == 0x45)
            {
                int n = BitConverter.ToInt32(rgIl, p + 1);
                int iBase = p + 5 + n * 4;
                it.rgSw = new int[n];
                for (int k = 0; k < n; k++) it.rgSw[k] = iBase + BitConverter.ToInt32(rgIl, p + 5 + k * 4);
            }
            else if (op == 0xDE) { it.iOp = 0xDD; it.iTgt = p + 2 + (sbyte)rgIl[p + 1]; }
            else if (op == 0xDD) { it.iTgt = p + 5 + BitConverter.ToInt32(rgIl, p + 1); }
            else if (op == 0xFE)
            {
                it.iOp = 0xFE00 | rgIl[p + 1];
                int iOpLen = l - 2;//前缀操作码的立即数按实际长度读 一律读四字节会把下一条指令的字节算进比较键
                if (iOpLen == 2 && p + 4 <= rgIl.Length) it.lArg = BitConverter.ToUInt16(rgIl, p + 2);
                else if (iOpLen == 4 && p + 6 <= rgIl.Length) it.lArg = BitConverter.ToUInt32(rgIl, p + 2);
            }
            else if (l == 2) { it.lArg = rgIl[p + 1]; }
            else if (l == 4 || l == 5) { it.lArg = BitConverter.ToUInt32(rgIl, p + 1); }
            else if (l == 9) { it.lArg = BitConverter.ToInt64(rgIl, p + 1); }
            rg.Add(it);
            p += l;
        }
        foreach (var i in rg)//所有分支目标必须落在指令起点
        {
            if (i.iTgt >= 0 && !setPos.Contains(i.iTgt)) { sWhy = $"tgt@{i.iPos}"; return false; }
            if (i.rgSw != null) foreach (int t in i.rgSw) if (!setPos.Contains(t)) { sWhy = $"sw@{i.iPos}"; return false; }
        }
        return true;
    }

    private static bool IlEquivalent(byte[] rgOld, byte[] rgNew, out string sWhy)//两版方法体的对齐 除nop外必须逐条相同
    {
        sWhy = "";
        if (!IlCanon(rgOld, out var rgA, out string sE1)) { sWhy = $"old:{sE1}"; return false; }
        if (!IlCanon(rgNew, out var rgB, out string sE2)) { sWhy = $"new:{sE2}"; return false; }
        var rgKa = new List<int>();//剔掉nop 目标是nop的引用顺延到下一个非nop
        var rgKb = new List<int>();
        for (int i = 0; i < rgA.Count; i++) if (rgA[i].iOp != 0x00) rgKa.Add(i);
        for (int i = 0; i < rgB.Count; i++) if (rgB[i].iOp != 0x00) rgKb.Add(i);
        if (rgKa.Count != rgKb.Count) { sWhy = $"count {rgKa.Count}!={rgKb.Count}"; return false; }
        var mapA = new Dictionary<int, int>();
        var mapB = new Dictionary<int, int>();
        foreach (int i in rgKa)
        {
            int j = 0; while (j < rgKa.Count && rgKa[j] < i) j++;
            mapA[i] = j;
        }
        foreach (int i in rgKb)
        {
            int j = 0; while (j < rgKb.Count && rgKb[j] < i) j++;
            mapB[i] = j;
        }
        for (int i = 0; i < rgKa.Count; i++)
        {
            var x = rgA[rgKa[i]];
            var y = rgB[rgKb[i]];
            if (x.iOp != y.iOp || x.lArg != y.lArg || (x.rgSw == null) != (y.rgSw == null) || x.rgSw?.Length != y.rgSw?.Length)
            { sWhy = $"ins{i} op{x.iOp:X2}/{y.iOp:X2} arg{x.lArg}/{y.lArg}"; return false; }
        }
        for (int i = 0; i < rgKa.Count; i++)//分支与switch目标 两版第i条经对齐映射后必须落在同一目标
        {
            var x = rgA[rgKa[i]];
            var y = rgB[rgKb[i]];
            if (x.iTgt >= 0)
            {
                int iA = rgA.FindIndex(o => o.iPos == x.iTgt);
                int iB = rgB.FindIndex(o => o.iPos == y.iTgt);
                if (iA < 0 || iB < 0) { sWhy = $"tgtmap{i}"; return false; }
                int jA = 0; while (jA < rgKa.Count && rgKa[jA] < iA) jA++;
                int jB = 0; while (jB < rgKb.Count && rgKb[jB] < iB) jB++;
                if (jA != jB) { sWhy = $"target{i} {jA}!={jB}"; return false; }
            }
            if (x.rgSw != null)
            {
                for (int k = 0; k < x.rgSw.Length; k++)
                {
                    int iA = rgA.FindIndex(o => o.iPos == x.rgSw[k]);
                    int iB = rgB.FindIndex(o => o.iPos == y.rgSw![k]);
                    if (iA < 0 || iB < 0) { sWhy = $"swmap{i}"; return false; }
                    int jA = 0; while (jA < rgKa.Count && rgKa[jA] < iA) jA++;
                    int jB = 0; while (jB < rgKb.Count && rgKb[jB] < iB) jB++;
                    if (jA != jB) { sWhy = $"swtgt{i},{k}"; return false; }
                }
            }
        }
        return true;
    }
}
