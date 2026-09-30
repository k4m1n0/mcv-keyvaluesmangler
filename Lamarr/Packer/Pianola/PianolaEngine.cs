namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    const int W8 = 0, W16 = 1, W32 = 2, W64 = 3;//opcode = 族基址 + 宽度w 0=8 1=16 2=32 3=64操作数 = [rs*16+rd]
    const int OP_END = 0x00, OP_RET = 0x01, OP_BAD = 0x7F;
    const int OP_MOVIMM = 0x08, OP_MOVREG = 0x09;
    const int OP_LD = 0x10, OP_ST = 0x14;
    const int OP_BSWAP = 0x18;
    const int OP_ADD = 0x20, OP_SUB = 0x30, OP_MUL = 0x40, OP_DIVU = 0x50, OP_MODU = 0x60,
        OP_AND = 0x70, OP_OR = 0x80, OP_XOR = 0x90, OP_SHL = 0xA0, OP_SHR = 0xB0, OP_ASR = 0xC0,
        OP_ROL = 0xD0, OP_ROR = 0xE0;
    const int OP_NOT = 0xF0;
    const int OP_CMP = 0x1C, OP_JMP = 0x1D, OP_JCC = 0x1E, OP_CALL = 0x1F;
    const int NReg = 16;
    //内存映射 低段是数据页与虚拟机栈 高段是解码缓冲 私有上下文不在内存里 依次存放流密钥与代码基址与代码长度
    const int VM_DATA = 0x1000;    //数据页大小 = LD/ST上界
    const int VM_CODE = 0x1000;    //解码缓冲基址
    const int VM_CODE_END = 0x1FF0;//解码缓冲上界
    const int VM_MEM = 0x2000;     //cbM下限
    const int VM_CTX_OFF = 128;    //pRegs私有区偏移
    const int CallMax = 64;//CALL深度上限 返回地址放在栈帧高位窗口 窗口200h正好装64个

    public static void AtRestCrypt(byte[] rg, uint uKey, bool bBe = true)//roll指令字at-rest逐字异或 流密钥由调用方经M尾部保留区传入 读入按ISA字节序 写回统一大端
    {
        uint uS = uKey != 0 ? uKey : 0x9E3779B9u;
        for (int i = 0; i + 4 <= rg.Length; i += 4)
        {
            uS = uS * 1664525u + 1013904223u;
            uint uV = bBe
                ? ((uint)rg[i] << 24) | ((uint)rg[i + 1] << 16) | ((uint)rg[i + 2] << 8) | rg[i + 3]
                : (uint)rg[i] | ((uint)rg[i + 1] << 8) | ((uint)rg[i + 2] << 16) | ((uint)rg[i + 3] << 24);
            uV ^= uS;
            rg[i] = (byte)(uV >> 24); rg[i + 1] = (byte)(uV >> 16); rg[i + 2] = (byte)(uV >> 8); rg[i + 3] = (byte)uV;
        }
    }

    public static uint TextSum(byte[] rgDll)
    {
        uint uH = 0x811C9DC5u;//与引擎侧初值相同 空.text也返回该值
        if (rgDll == null || rgDll.Length < 0x40) return uH;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        if (iPe <= 0 || iPe + 0x40 > rgDll.Length) return uH;
        int iSecCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSecOff = iPe + 24 + BitConverter.ToUInt16(rgDll, iPe + 20);
        for (int i = 0; i < iSecCnt; i++)
        {
            int o = iSecOff + i * 40;
            if (o + 40 > rgDll.Length) return uH;
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') != ".text") continue;
            int iRawSz = BitConverter.ToInt32(rgDll, o + 16);
            int iRaw = BitConverter.ToInt32(rgDll, o + 20);
            if (iRawSz <= 0 || iRaw < 0 || iRaw + iRawSz > rgDll.Length) return uH;
            for (int k = 0; k < iRawSz; k++) { uH ^= rgDll[iRaw + k]; uH *= 0x01000193u; }
            return uH;
        }
        return uH;
    }

    //布局置换管引擎代码怎么排 字节置换管字节含义 只做行为不变的置换
    static ulong _uSkel;
    static void SkelBegin(ulong uSeed) => _uSkel = uSeed ^ 0xD1B54A32D192ED03UL;
    static ulong SkelNext()
    {
        _uSkel += 0x9E3779B97F4A7C15UL;
        ulong z = _uSkel;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    static T[] SkelShuffle<T>(T[] rg)
    {
        for (int i = rg.Length - 1; i > 0; i--)
        {
            int j = (int)(SkelNext() % (ulong)(i + 1));
            (rg[i], rg[j]) = (rg[j], rg[i]);
        }
        return rg;
    }
    static string SkelPostProcess(string sAsm)//文本级后处理只动两类 同一寄存器上连续的cmp rX,K与je L是互斥测试 交换顺序行为不变 h_exit的ret之后与h_perm数据之前插种子派生死块
    {
        string[] rgLine = sAsm.Replace("\r\n", "\n").Split('\n');
        var lst = new System.Collections.Generic.List<string>(rgLine.Length + 64);
        int i = 0;
        while (i < rgLine.Length)
        {
            string sReg;
            if (i + 1 < rgLine.Length && IsSkelCmp(rgLine[i], out sReg) && IsSkelJe(rgLine[i + 1]))
            {
                string sRegRun = sReg;
                int iEnd = i;
                var lstPair = new System.Collections.Generic.List<string[]>();
                while (iEnd + 1 < rgLine.Length && IsSkelCmp(rgLine[iEnd], out sReg) && sReg == sRegRun && IsSkelJe(rgLine[iEnd + 1]))
                {
                    lstPair.Add(new[] { rgLine[iEnd], rgLine[iEnd + 1] });
                    iEnd += 2;
                }
                string[][] rgPair = lstPair.ToArray();
                if (rgPair.Length > 1) SkelShuffle(rgPair);
                foreach (string[] rgP in rgPair) { lst.Add(rgP[0]); lst.Add(rgP[1]); }
                i = iEnd;
                continue;
            }
            if (rgLine[i].TrimEnd() == "h_perm:") SkelDeadBlocks(lst);
            lst.Add(rgLine[i]);
            i++;
        }
        return string.Join(System.Environment.NewLine, lst);
    }
    static bool IsSkelCmp(string sLine, out string sReg)
    {
        sReg = "";
        string s = sLine.Trim();
        if (!s.StartsWith("cmp ")) return false;
        int iC = s.IndexOf(',');
        if (iC < 0) return false;
        sReg = s.Substring(4, iC - 4).Trim();
        return sReg.Length > 0 && sReg != "r12d";//r12d是at-rest流密钥 不在寄存器链内 出现就不参与置换
    }
    static bool IsSkelJe(string sLine) => sLine.Trim().StartsWith("je ");
    static void SkelDeadBlocks(System.Collections.Generic.List<string> lst)//死块2..5块x 3..8行db每块自带jmp自环
    {
        int iBlocks = 2 + (int)(SkelNext() % 4);
        for (int b = 0; b < iBlocks; b++)
        {
            lst.Add($"h_dead{b}:");
            int iRows = 3 + (int)(SkelNext() % 6);
            for (int r = 0; r < iRows; r++)
            {
                var rgB = new string[8];
                for (int k = 0; k < 8; k++) rgB[k] = $"0{(byte)(SkelNext() & 0xFFu):X2}h";
                lst.Add("    db " + string.Join(", ", rgB));
            }
            lst.Add("    jmp h_dead" + b);
        }
    }

    static readonly (int, string)[] Fam = { (OP_ADD,"add"),(OP_SUB,"sub"),(OP_MUL,"mul"),
        (OP_DIVU,"divu"),(OP_MODU,"modu"),(OP_AND,"and"),(OP_OR,"or"),(OP_XOR,"xor"),
        (OP_SHL,"shl"),(OP_SHR,"shr"),(OP_ASR,"asr"),(OP_ROL,"rol"),(OP_ROR,"ror") };
    static readonly (int, string)[] FamUn = { (OP_NOT,"not") };

    //内部字节码解释器 寄存器组按基址加序号寻址 标志位由两个位承载 调用约定按平台接口
public static string Generate(ulong uSeed, string sIsa = "ppc")
    {
        ulong uM = uSeed ^ 0x9E3779B97F4A7C15UL;//内部操作码表由种子派生 内部操作码槽位映射真置换表
        uM ^= uM << 13; uM ^= uM >> 7; uM ^= uM << 17;
        byte[] rgPi = BuildOpPerm(uM);
        OpTable.Begin(uSeed, sIsa);//h_perm把操作码置换的槽位映射到布局置换的槽位 操作码置换恒等时为空操作
        CarrierFmt.Begin(uSeed);//载体宽度码表以字节表烘焙进镜像
        SkelBegin(uSeed);//布局置换与操作码置换分属两域 都只看种子与ISA 互不影响
        byte[] rgPerm = (byte[])rgPi.Clone();
        {
            byte[] rgS = OpTable.Sigma;
            int[] rgCanon = new int[] { OP_END, OP_RET, OP_MOVIMM, OP_MOVREG, OP_CMP, OP_JMP, OP_JCC, OP_CALL };
            for (int i = 0; i < rgCanon.Length; i++) rgPerm[rgS[rgCanon[i]]] = rgPi[rgCanon[i]];
            for (int w = 0; w < 4; w++)
            {
                rgPerm[rgS[OP_LD + w]] = rgPi[OP_LD + w];
                rgPerm[rgS[OP_ST + w]] = rgPi[OP_ST + w];
                rgPerm[rgS[OP_BSWAP + w]] = rgPi[OP_BSWAP + w];//BSWAP的四宽度也要进置换表
            }
            foreach (var f in Fam) for (int w = 0; w < 4; w++) rgPerm[rgS[f.Item1 + w]] = rgPi[f.Item1 + w];//ALU与NOT的8/16位也是原生能力 四宽度全部进表
            foreach (var f in FamUn) for (int w = 0; w < 4; w++) rgPerm[rgS[f.Item1 + w]] = rgPi[f.Item1 + w];
        }
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine($"; Pianola engine (plain interpreter) seed=0x{uSeed:X16})");
        sb.AppendLine(".code");
        sb.AppendLine("PUBLIC PianolaRun");
        sb.AppendLine("PianolaRun:");
        sb.AppendLine("    push rbx");
        sb.AppendLine("    push rsi");
        sb.AppendLine("    push rdi");
        sb.AppendLine("    push r12");
        sb.AppendLine("    push r13");
        sb.AppendLine("    push r14");
        sb.AppendLine("    push r15");
        sb.AppendLine("    push rbp            ; VM stack pointer (M is the VM's own memory: no host stack state)");
        sb.AppendLine("    mov rbx, rdx        ; pRegs v[16] (private ctx follows at VM_CTX_OFF)");
        sb.AppendLine("    mov rdi, r8         ; pM (data memory base)");
        sb.AppendLine("    mov r13, r9         ; cbM");
        sb.AppendLine("    xor r12d, r12d      ; flags");
        sb.AppendLine("    xor r14d, r14d      ; spare/zero");
        sb.AppendLine("    xor r15d, r15d      ; call depth counter, return addresses live on the host stack");
        bool bCarrier = sIsa == "ppc" || sIsa == "rv64" || sIsa == "mips64" || sIsa == "loongarch" || sIsa == "alpha";
        sb.AppendLine("    cmp r13, 2000h      ; cbM must cover the data page plus the code window");//宿主栈不参与VM状态 VM栈全落在M里 cbM不足数据页+代码窗口即h_bad VM栈指针在rbp自数据页顶向下
        sb.AppendLine("    jb h_bad");
        sb.AppendLine("    lea rbp, [rdi+1000h] ; VM stack pointer: grows down inside the data page");
        if (bCarrier)
        {
            sb.AppendLine("    mov r12d, dword ptr [rbx+128]      ; at-rest stream key: private ctx, NOT in VM memory");//解密密钥 = 调用方置入的m_key ^ 引擎自身.text的滚动摘录
            sb.AppendLine("    mov r10, qword ptr [rbx+136]       ; absolute address of this engine .text (private ctx)");
            sb.AppendLine("    mov r11d, dword ptr [rbx+144]      ; length of this engine .text (private ctx)");
            sb.AppendLine("    mov eax, 0811C9DC5h");
            sb.AppendLine("    xor esi, esi");
            sb.AppendLine("    test r11d, r11d");
            sb.AppendLine("    jz key_mix_done");
            sb.AppendLine("key_mix:");
            sb.AppendLine("    movzx edx, byte ptr [r10+rsi]");
            sb.AppendLine("    xor eax, edx");
            sb.AppendLine("    imul eax, eax, 01000193h");
            sb.AppendLine("    inc esi");
            sb.AppendLine("    cmp esi, r11d");
            sb.AppendLine("    jb key_mix");
            sb.AppendLine("key_mix_done:");
            sb.AppendLine("    xor r12d, eax                        ; bound to its own measurement");
            sb.AppendLine("    jmp dec_entry");
        }
        else
        {
            sb.AppendLine("    mov rsi, rcx        ; pc, internal bytecode, carrier form is not enabled for this ISA");
        }
        sb.AppendLine("main_loop:");
        sb.AppendLine("    movzx eax, byte ptr [rsi]");
        sb.AppendLine("    lea r10, [h_perm]");
        sb.AppendLine("    movzx eax, byte ptr [r10+rax]       ; opcode -> handler slot (permutation table)");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    lea r10, [h_tbl]");
        sb.AppendLine("    movsxd rax, dword ptr [r10+rax*4]   ; relative offset, position independent");
        sb.AppendLine("    add rax, r10");
        sb.AppendLine("    jmp rax");

        foreach (var fnBody in SkelShuffle(new System.Action<System.Text.StringBuilder>[] {//处理器块顺序逐产物打乱 每块都自终止 h_tbl按标签取址 顺序无含义
            EmitEnd, EmitMov, EmitAluFam, EmitAluUn, EmitCmp, EmitBranch, EmitLdSt })) fnBody(sb);
        if (bCarrier)
        {
            switch (sIsa)
            {
                case "ppc": EmitIsaDecodePpc(sb); break;
                case "rv64": EmitIsaDecodeRv(sb); EmitIsaDecodeRv2(sb); break;
                case "mips64": EmitIsaDecodeMi(sb); EmitIsaDecodeMi2(sb); break;
                case "loongarch": EmitIsaDecodeLa(sb); EmitIsaDecodeLa2(sb); EmitIsaDecodeLa3(sb); break;
                case "alpha": EmitIsaDecodeAl(sb); EmitIsaDecodeAl2(sb); EmitIsaDecodeAl3(sb); break;
            }
            EmitIsaDecodeDone(sb);
        }

        sb.AppendLine("h_bad:");
        sb.AppendLine("    mov rax, -1");
        sb.AppendLine("h_exit:");
        sb.AppendLine("    ; V1: wipe the code window on the way out -- the decoded bytecode must not survive the call");
        sb.AppendLine("    mov rcx, rdi");
        sb.AppendLine("    add rcx, 1000h");
        sb.AppendLine("    mov edx, 0FF0h");
        sb.AppendLine("wipe_code:");
        sb.AppendLine("    mov qword ptr [rcx], 0");
        sb.AppendLine("    add rcx, 8");
        sb.AppendLine("    sub edx, 8");
        sb.AppendLine("    jnz wipe_code");
        sb.AppendLine("    pop rbp");
        sb.AppendLine("    pop r15");
        sb.AppendLine("    pop r14");
        sb.AppendLine("    pop r13");
        sb.AppendLine("    pop r12");
        sb.AppendLine("    pop rdi");
        sb.AppendLine("    pop rsi");
        sb.AppendLine("    pop rbx");
        sb.AppendLine("    ret");

        sb.AppendLine("h_perm:");//位置无关派发 存相对h_tbl的32位偏移 同节常数无重定位 h_perm在h_tbl之前 运行时先把操作码查成槽位再按槽位取处理程序偏移
        for (int i = 0; i < 256; i += 16)
        {
            var rgRow = new string[16];
            for (int k = 0; k < 16; k++) rgRow[k] = $"0{rgPerm[i + k]:X2}h";
            sb.AppendLine("    db " + string.Join(", ", rgRow));
        }
        sb.AppendLine("h_tbl:");
        var rgL = new string[256];
        for (int i = 0; i < 256; i++) rgL[i] = "h_bad";
        void Map(int op, string s) { rgL[rgPi[op]] = s; }
        Map(OP_END, "h_end"); Map(OP_RET, "h_ret");
        Map(OP_MOVIMM, "h_movimm"); Map(OP_MOVREG, "h_movreg");
        for (int w = 0; w < 4; w++) { Map(OP_LD + w, "h_ld" + w); Map(OP_ST + w, "h_st" + w); Map(OP_BSWAP + w, "h_bswap" + w); }
        foreach (var f in Fam) for (int w = 0; w < 4; w++) Map(f.Item1 + w, "h_" + f.Item2 + w);
        foreach (var f in FamUn) for (int w = 0; w < 4; w++) Map(f.Item1 + w, "h_" + f.Item2 + w);
        Map(OP_CMP, "h_cmp"); Map(OP_JMP, "h_jmp"); Map(OP_JCC, "h_jcc"); Map(OP_CALL, "h_call");
        for (int i = 0; i < 256; i += 4)
            sb.AppendLine($"    dd {rgL[i]} - h_tbl, {rgL[i + 1]} - h_tbl, {rgL[i + 2]} - h_tbl, {rgL[i + 3]} - h_tbl");
        sb.AppendLine("d_wcode:");//宽度码表载体第三字段[4:3]的2位值 内部宽度码数据紧跟h_tbl 解码时lea取基址再查
        sb.AppendLine($"    db {CarrierFmt.Inv[0]}, {CarrierFmt.Inv[1]}, {CarrierFmt.Inv[2]}, {CarrierFmt.Inv[3]}");
        sb.AppendLine("END");
        return SkelPostProcess(sb.ToString());
    }

static string Wx(int v) => $"0{v:X}h";
    static string MaskStr(int w) => w switch { 0 => "0FFh", 1 => "0FFFFh", 2 => "0FFFFFFFFh", _ => "-1" };

    static byte[] BuildOpPerm(ulong uSeed)//操作码槽位置换表 splitmix64驱动Fisher-Yates同一个种子逐位可复现且必是256! 中的一员
    {
        var rgP = new byte[256];
        for (int i = 0; i < 256; i++) rgP[i] = (byte)i;
        ulong uS = uSeed;
        for (int i = 255; i > 0; i--)
        {
            uS += 0x9E3779B97F4A7C15UL;
            ulong uZ = uS;
            uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9UL;
            uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBUL;
            uZ ^= uZ >> 31;
            int j = (int)(uZ % (ulong)(i + 1));
            (rgP[i], rgP[j]) = (rgP[j], rgP[i]);
        }
        return rgP;
    }

    static void EmitIsaDecodeDone(StringBuilder sb)//解码末尾复位r12为执行期标志 初值后进入执行
    {
        sb.AppendLine("dec_done:");
        sb.AppendLine("    xor r12d, r12d         ; r12 held the stream state during decode, clear it before execution");
        sb.AppendLine("    lea rsi, [rdi+1000h]   ; execute internal bytecode from the VM code window");
        sb.AppendLine("    jmp main_loop");
    }

    static uint XoOf2(int iFam) => NotenrolleIsa.XoForFam(iFam);//XO表只保留编码器那一份 这里不再抄一遍
}
