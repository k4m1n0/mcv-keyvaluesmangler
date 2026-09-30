namespace Lamarr.Packer;

public static class OpTable//内部字节码操作码到指令字流字节的置换表 只有操作数个数位宽宽度位三者相同的组内才可互换
{
    //组内字节可互换 置换与开关取值都写进escrow 环境变量是唯一显式开关
    static readonly byte[][] Groups =
    {
        new byte[] { 0x00, 0x01 },                       //G0 0操作数END, RET
        new byte[] { 0x09, 0x1C },                       //G1 1操作数MOVREG, CMP
        new byte[] { 0x1D, 0x1F },                       //G2 2字节偏移JMP, CALL
        new byte[] { 0x1E },                             //G3 Jcc自成一类
        new byte[] { 0x08 },                             //G4 MOVIMM自成一类
        new byte[] { 0x10, 0x14 },                       //G5宽度8LD8,ST8
        new byte[] { 0x11, 0x15 },                       //G6宽度16 LD16, ST16
        new byte[] {                                     //G7宽度32 LD32, ST32, ALU 13族w32
            0x12, 0x16,
            0x22, 0x32, 0x42, 0x52, 0x62, 0x72, 0x82, 0x92, 0xA2, 0xB2, 0xC2, 0xD2, 0xE2 },
        new byte[] {                                     //G8宽度64 LD64, ST64, ALU 13族w64
            0x13, 0x17,
            0x23, 0x33, 0x43, 0x53, 0x63, 0x73, 0x83, 0x93, 0xA3, 0xB3, 0xC3, 0xD3, 0xE3 },
        //取反与字节序反转是单寄存器形态 按宽度成组 同宽度内互换合法 不可并进访存组
        new byte[] { 0xF2, 0x1A },                       //G9NOTw32,BSWAPw32
        new byte[] { 0xF3, 0x1B },                       //G10NOTw64,BSWAPw64
        //ALU的8/16位各自成组 不可并进访存组 访存与算术字形的宽度位含义不同 同字形族内互换才保形
        new byte[] {                                     //G11宽度8 ALU 13族w8
            0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0 },
        new byte[] {                                     //G12宽度16 ALU 13族w16
            0x21, 0x31, 0x41, 0x51, 0x61, 0x71, 0x81, 0x91, 0xA1, 0xB1, 0xC1, 0xD1, 0xE1 },
        new byte[] { 0xF0, 0x18 },                       //G13NOTw8,BSWAPw8
        new byte[] { 0xF1, 0x19 },                       //G14NOTw16,BSWAPw16
    };

    public static readonly string GroupMask = Environment.GetEnvironmentVariable("LAMARR_OP_TABLE_GROUPS") ?? "";//每组独立Fisher-Yates 未启用时全空间恒等 取值none时全部组关闭
    public static bool Triage => GroupMask.Length != 0;
    public static bool GroupOn(int iGroup)
    {
        if (GroupMask.Length == 0) return true;
        if (string.Equals(GroupMask.Trim(), "none", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string s in GroupMask.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(s.Trim(), out int i) && i == iGroup) return true;
        return false;
    }
    public static byte[] BuildSigma(ulong uSeed)
    {
        var rgS = new byte[256];
        for (int i = 0; i < 256; i++) rgS[i] = (byte)i;
        ulong uS = uSeed ^ 0xA0761D6478BD642FUL;
        for (int iG = 0; iG < Groups.Length; iG++)
        {
            byte[] rgG = Groups[iG];
            if (rgG.Length < 2) continue;
            if (!GroupOn(iG)) continue;
            for (int i = rgG.Length - 1; i > 0; i--)
            {
                uS += 0x9E3779B97F4A7C15UL;
                ulong uZ = uS;
                uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9UL;
                uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBUL;
                uZ ^= uZ >> 31;
                int j = (int)(uZ % (ulong)(i + 1));
                (rgS[rgG[i]], rgS[rgG[j]]) = (rgS[rgG[j]], rgS[rgG[i]]);
            }
        }
        return rgS;
    }

    public static byte[] Invert(byte[] rgSigma)
    {
        var rgI = new byte[256];
        for (int i = 0; i < 256; i++) rgI[rgSigma[i]] = (byte)i;
        return rgI;
    }

    public static byte Map(byte[] rgSigma, byte op) => rgSigma[op];//规范操作码到指令字流字节 未启用时原样返回

    static byte[] Identity()
    {
        var rg = new byte[256];
        for (int i = 0; i < 256; i++) rg[i] = (byte)i;
        return rg;
    }
    static byte[] _rgSigma = Identity();//当前打包过程使用的置换表 未启用置换时是恒等
    static byte[] _rgInv = Identity();
    public static byte[] Sigma => _rgSigma;

    public static void Begin(ulong uSeed, string sIsa)//每产物一次 两侧同种子同ISA得到同一置换 同输入两次打包逐字节相同 非载体调试路径恒等
    {
        bool bCarrier = sIsa == "ppc" || sIsa == "rv64" || sIsa == "mips64" || sIsa == "loongarch" || sIsa == "alpha";
        _rgSigma = bCarrier ? BuildSigma(uSeed) : Identity();
        _rgInv = Invert(_rgSigma);
        _uLastSeed = uSeed; _sLastIsa = sIsa; _bLastDerived = bCarrier;
    }

    static ulong _uLastSeed;//上次Begin的实参与派生结果 escrow重打包要用
    static string _sLastIsa = "";
    static bool _bLastDerived;
    public static ulong LastSeed => _uLastSeed;
    public static string LastIsa => _sLastIsa;
    public static bool LastDerived => _bLastDerived;
    public static string SigmaHex => System.Convert.ToHexString(_rgSigma);

    public static byte Map(byte op) => _rgSigma[op];
    public static byte InvMap(byte y) => _rgInv[y];

    public static string Dump(ulong uSeed)//打印置换表 逆表与分组覆盖
    {
        byte[] rgS = BuildSigma(uSeed);
        byte[] rgI = Invert(rgS);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[op-table] derived=true triage={Triage} groups='{GroupMask}' seed=0x{uSeed:X16}");
        sb.AppendLine($"[op-table] sigma   = {Convert.ToHexString(rgS)}");
        sb.AppendLine($"[op-table] inverse = {Convert.ToHexString(rgI)}");
        foreach (byte[] rgG in Groups)
        {
            var rgMap = new string[rgG.Length];
            for (int i = 0; i < rgG.Length; i++) rgMap[i] = $"{rgG[i]:X2}->{rgS[rgG[i]]:X2}";
            sb.AppendLine($"[op-table] group[{rgG.Length.ToString().PadLeft(2)}] {string.Join(" ", rgMap)}");
        }
        return sb.ToString();
    }

    public static bool SelfCheck(ulong uSeed, out string sBad)//断言每个组映射到自身
    {
        byte[] rgS = BuildSigma(uSeed);
        var set = new System.Collections.Generic.HashSet<byte>();
        foreach (byte[] rgG in Groups) foreach (byte b in rgG) set.Add(b);
        int iExpect = 0;
        foreach (byte[] rgG in Groups) iExpect += rgG.Length;
        if (set.Count != iExpect) { sBad = $"overlapping group definitions: {set.Count} != {iExpect}"; return false; }
        foreach (byte[] rgG in Groups)
        {
            var rgIn = new System.Collections.Generic.HashSet<byte>(rgG);
            foreach (byte b in rgG)
            {
                if (!rgIn.Contains(rgS[b])) { sBad = $"out-of-group mapping {b:X2}->{rgS[b]:X2}"; return false; }
            }
        }
        foreach (byte b in System.Linq.Enumerable.Range(0, 256).Select(v => (byte)v))//未用字节必须保持恒等 否则解码器收到未定义映射
            if (!set.Contains(b) && rgS[b] != b) { sBad = $"unused byte moved {b:X2}"; return false; }
        sBad = "ok";
        return true;
    }
}
