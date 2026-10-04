namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    static void DecPair(StringBuilder sb)//解双寄存器操作数字节ecx=rd r10=rs rsi已越过操作码开始读操作数
    {
        sb.AppendLine("    movzx eax, byte ptr [rsi]");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    and ecx, 0Fh");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 4");
        sb.AppendLine("    and r10d, 0Fh");
    }

    static void EmitEnd(StringBuilder sb)
    {
        sb.AppendLine("h_end:");
        sb.AppendLine("    mov rax, [rbx]");
        sb.AppendLine("    jmp h_exit");
    }
    static void EmitMov(StringBuilder sb)
    {
        sb.AppendLine("h_movimm:");//MOVimm [rd][w][imm LE]
        sb.AppendLine("    movzx ecx, byte ptr [rsi]");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    movzx eax, byte ptr [rsi]");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    movzx r8d, al ; width");
        sb.AppendLine("    cmp r8d, 0");
        sb.AppendLine("    je mi_8");
        sb.AppendLine("    cmp r8d, 1");
        sb.AppendLine("    je mi_16");
        sb.AppendLine("    cmp r8d, 2");
        sb.AppendLine("    je mi_32");
        sb.AppendLine("    mov rax, qword ptr [rsi]");
        sb.AppendLine("    add rsi, 8");
        sb.AppendLine("    jmp mi_st");
        sb.AppendLine("mi_32:");
        sb.AppendLine("    mov eax, dword ptr [rsi]");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    jmp mi_st");
        sb.AppendLine("mi_16:");
        sb.AppendLine("    movzx eax, word ptr [rsi]");
        sb.AppendLine("    add rsi, 2");
        sb.AppendLine("    jmp mi_st");
        sb.AppendLine("mi_8:");
        sb.AppendLine("    movzx eax, byte ptr [rsi]");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("mi_st:");
        sb.AppendLine("    mov [rbx+rcx*8], rax");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("h_movreg:");//MOVreg [rdrs]
        DecPair(sb);
        sb.AppendLine("    mov rax, [rbx+r10*8]");
        sb.AppendLine("    mov [rbx+rcx*8], rax");
        sb.AppendLine("    jmp main_loop");
    }

    static string RdReg(int w, string r64, string sIdx, bool bSigned = false)//算术结果保留在64位寄存器 w<64时与掩码内联助手按宽度取值/掩码/计数取模 8/16位从槽低字节低字movzx 32位用32位读 64位整读
    {
        if (w >= 2)
            return $"    mov {(w == 3 ? r64 : (r64 == "rax" ? "eax" : "r8d"))}, [rbx+{sIdx}*8]";
        return $"    {(bSigned ? "movsx" : "movzx")} {(r64 == "rax" ? "eax" : "r8d")}, {(w == 0 ? "byte ptr " : "word ptr ")}[rbx+{sIdx}*8]";
    }
    //窄宽度移位与旋转的计数先按位宽取模 否则整圈旋转得零 旋转须在宽度大小的寄存器上做

    static string Acc32(int w) => w == 3 ? "rax" : "eax";
    static ulong MaskOf(int w) => w == 0 ? 0xFFUL : w == 1 ? 0xFFFFUL : w == 2 ? 0xFFFFFFFFUL : 0UL;
    static string CntMod(int w) => w == 0 ? "    and cl, 7" : w == 1 ? "    and cl, 15" : "";
    static string RotReg(int w) => w == 0 ? "al" : w == 1 ? "ax" : w == 2 ? "eax" : "rax";
    static void EmitAluFam(StringBuilder sb)
    {
        foreach (var (b, nm) in Fam)
        {
            for (int w = 0; w < 4; w++)//后四宽度都是原生能力
            {
                sb.AppendLine($"h_{nm}{w}:");
                DecPair(sb);
                EmitAluOp(sb, nm, w);
            }
        }
    }
    static void EmitAluOp(StringBuilder sb, string nm, int w)
    {
        switch (nm)
        {
            case "add":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    add rax, [rbx+r10*8]");
                break;
            case "sub":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    sub rax, [rbx+r10*8]");
                break;
            case "mul":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    imul rax, [rbx+r10*8]");
                break;
            case "divu"://除法与取模 被除数与除数都必须按宽度取 高位脏值会让商与余数算错
            case "modu":
                sb.AppendLine(RdReg(w, "rax", "rcx"));
                sb.AppendLine(RdReg(w, "r8", "r10"));
                sb.AppendLine("    xor edx, edx");
                sb.AppendLine("    div " + (w == 3 ? "r8" : "r8d"));
                if (nm == "modu") sb.AppendLine($"    mov {Acc32(w)}, {(w == 3 ? "rdx" : "edx")}");
                break;
            case "and":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    and rax, [rbx+r10*8]");
                break;
            case "or":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    or rax, [rbx+r10*8]");
                break;
            case "xor":
                sb.AppendLine("    mov rax, [rbx+rcx*8]");
                sb.AppendLine("    xor rax, [rbx+r10*8]");
                break;
            case "shl": case "shr": case "asr": case "rol": case "ror"://移位与旋转值按宽度取 计数先按位宽取模再移
            {
                sb.AppendLine("    mov r9, rcx ; preserve rd (cl is reused for the count)");
                sb.AppendLine(RdReg(w, "rax", "r9", nm == "asr"));
                sb.AppendLine("    mov cl, byte ptr [rbx+r10*8]");
                string sMod = CntMod(w);
                if (sMod.Length > 0) sb.AppendLine(sMod);
                string sIns = nm switch { "shl" => "shl", "shr" => "shr", "asr" => "sar", "rol" => "rol", _ => "ror" };
                bool bRot = nm == "rol" || nm == "ror";
                sb.AppendLine($"    {sIns} {(bRot ? RotReg(w) : Acc32(w))}, cl");
                sb.AppendLine("    mov rcx, r9");
                break;
            }
        }
        EmitNarrowAny(sb, MaskOf(w));//A族8/16/32位按掩码截断 64位不加
        sb.AppendLine("    mov [rbx+rcx*8], rax");
        sb.AppendLine("    jmp main_loop");
    }

    static void EmitNarrowAny(StringBuilder sb, ulong uMask)//宽度掩码只有w32/w64 32位须用寄存器掩码 and rax,0FFFFFFFFh会被编成imm8符号扩展
    {
        if (uMask == 0) return;
        if (uMask <= 0xFFFFFFFFUL)
            sb.AppendLine($"    mov r8d, 0{uMask:X}h");//写r8d会零扩展高32位
        else
            sb.AppendLine($"    mov r8, 0{uMask:X}h");
        sb.AppendLine("    and rax, r8");
    }

    static void EmitAluUn(StringBuilder sb)
    {
        for (int w = 0; w < 4; w++)//NOT单目0xF0+w w为32或64 NOT四宽度原生
        {
            sb.AppendLine($"h_not{w}:");
            sb.AppendLine("    movzx ecx, byte ptr [rsi]");
            sb.AppendLine("    inc rsi");
            sb.AppendLine("    and ecx, 0Fh");
            sb.AppendLine("    mov rax, [rbx+rcx*8]");
            sb.AppendLine("    not rax");
            EmitNarrowAny(sb, MaskOf(w));//8/16位也按宽度截断
            sb.AppendLine("    mov [rbx+rcx*8], rax");
            sb.AppendLine("    jmp main_loop");
        }
        for (int w = 0; w < 4; w++)//BSWAP 0x18+w
        {
            sb.AppendLine($"h_bswap{w}:");
            sb.AppendLine("    movzx ecx, byte ptr [rsi]");
            sb.AppendLine("    inc rsi");
            sb.AppendLine("    and ecx, 0Fh");
            sb.AppendLine("    mov rax, [rbx+rcx*8]");
            if (w == 0) { sb.AppendLine("    jmp main_loop"); continue; }//此处的字节交换是恒等
            else if (w == 1) { sb.AppendLine("    rol ax, 8"); EmitNarrowAny(sb, 0xFFFFUL); }
            else if (w == 2) { sb.AppendLine("    bswap eax"); }//写eax会清零高32位
            else { sb.AppendLine("    bswap rax"); }
            sb.AppendLine("    mov [rbx+rcx*8], rax");
            sb.AppendLine("    jmp main_loop");
        }
    }

static void EmitCmp(StringBuilder sb)
    {
        sb.AppendLine("h_cmp:");//CMP 0x1C [rdrs] 无符号比较v[rd]和v[rs] 置ZF=bit0 CF借位=bit1
        DecPair(sb);
        sb.AppendLine("    mov rax, [rbx+rcx*8]");
        sb.AppendLine("    cmp rax, [rbx+r10*8]");
        sb.AppendLine("    ; collect ZF bit 0 and CF bit 1 into r12, flags are not modified, setcc only reads them");
        sb.AppendLine("    setz r12b");
        sb.AppendLine("    setc dl");
        sb.AppendLine("    movzx eax, r12b");
        sb.AppendLine("    movzx edx, dl");
        sb.AppendLine("    shl edx, 1");
        sb.AppendLine("    or eax, edx");
        sb.AppendLine("    mov r12d, eax");
        sb.AppendLine("    jmp main_loop");
    }

static void EmitBranch(StringBuilder sb)
    {
        sb.AppendLine("h_jmp:");//rsi指向操作码后第一个操作数字节 off相对操作码起点rsi-1 JMP/CALL后随[off16 LE] Jcc后随[cond][off16 LE] cond 0=always 1=eq 2=ne 3=cs 4=cc
        sb.AppendLine("    movsx rax, word ptr [rsi]");
        sb.AppendLine("    add rsi, 2");
        sb.AppendLine("    lea rsi, [rsi+rax-3]");
        sb.AppendLine("    jmp main_loop");

        sb.AppendLine("h_call:");
        sb.AppendLine("    ; the return address goes onto the VM stack inside M (host stack holds no VM state)");
        sb.AppendLine("    movsx rax, word ptr [rsi]");
        sb.AppendLine("    add rsi, 2");
        sb.AppendLine("    inc r15");
        sb.AppendLine($"    cmp r15, {CallMax} ; depth cap, runaway recursion stops at h_bad");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    lea rdx, [rbp-8] ; push: the VM stack must stay at or above the data page base");
        sb.AppendLine("    cmp rdx, rdi");
        sb.AppendLine("    jb h_bad");
        sb.AppendLine("    mov [rdx], rsi");
        sb.AppendLine("    mov rbp, rdx");
        sb.AppendLine("    ; target = opcode + off, rsi is opcode+3 here so target = rsi+off-3");
        sb.AppendLine("    lea rsi, [rsi+rax-3]");
        sb.AppendLine("    jmp main_loop");

        sb.AppendLine("h_jcc:");
        sb.AppendLine("    movzx ecx, byte ptr [rsi] ; cond");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    movsx rax, word ptr [rsi] ; off (signed 64)");
        sb.AppendLine("    add rsi, 2");
        sb.AppendLine("    mov r8, rax");
        sb.AppendLine("    cmp ecx, 0");
        sb.AppendLine("    je jcc_take");
        sb.AppendLine("    cmp ecx, 1");
        sb.AppendLine("    jne jcc_c2");
        sb.AppendLine("    test r12b, 1");
        sb.AppendLine("    jnz jcc_take");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("jcc_c2:");
        sb.AppendLine("    cmp ecx, 2");
        sb.AppendLine("    jne jcc_c3");
        sb.AppendLine("    test r12b, 1");
        sb.AppendLine("    jz jcc_take");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("jcc_c3:");
        sb.AppendLine("    cmp ecx, 3");
        sb.AppendLine("    jne jcc_c4");
        sb.AppendLine("    test r12b, 2");
        sb.AppendLine("    jnz jcc_take");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("jcc_c4:");
        sb.AppendLine("    test r12b, 2");
        sb.AppendLine("    jz jcc_take");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("jcc_take:");
        sb.AppendLine("    lea rsi, [rsi+r8-4] ; rsi=opcode+4 (1 opcode+1 cond+2 off); target=opcode+off");
        sb.AppendLine("    jmp main_loop");
        sb.AppendLine("h_ret:");
        sb.AppendLine("    ; pop the return address off the VM stack; depth in r15, an unbalanced RET goes to h_bad");
        sb.AppendLine("    cmp r15, 0");
        sb.AppendLine("    jbe h_bad");
        sb.AppendLine("    lea rdx, [rbp+8] ; the VM stack top must stay inside the data page");
        sb.AppendLine("    lea rcx, [rdi+1000h]");
        sb.AppendLine("    cmp rdx, rcx");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    dec r15");
        sb.AppendLine("    mov rsi, [rbp]");
        sb.AppendLine("    mov rbp, rdx");
        sb.AppendLine("    jmp main_loop");
    }

static void EmitLdSt(StringBuilder sb)
    {
        for (int w = 0; w < 4; w++)
        {
            sb.AppendLine($"h_ld{w}:");
            EmitMem(sb, "ld", w);
            sb.AppendLine($"h_st{w}:");
            EmitMem(sb, "st", w);
        }
    }
    static void EmitMem(StringBuilder sb, string sDir, int w)
    {
        DecPair(sb);//ecx=rd, r10=rs
        sb.AppendLine("    mov rax, [rbx+r10*8] ; addr");
        sb.AppendLine("    mov r8, rax");
        sb.AppendLine("    cmp r8, 1000h ; the address itself must stay inside the data page, a negative value wraps past this test");
        sb.AppendLine("    jae h_bad");
        int nb = new[] { 1, 2, 4, 8 }[w];//边界检查addr+size<=cbM
        sb.AppendLine($"    lea rax, [r8+{nb}]");
        sb.AppendLine("    cmp rax, 1000h ; data page limit (code window / host ctx are not VM addressable)");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    ; page-window translation. The data page is carved into 2^10 byte slots; ctx[172]");
        sb.AppendLine("    ; points at a per-run permutation table (0 = identity, i.e. the pre-permutation mapping).");
        sb.AppendLine("    mov r9, qword ptr [rbx+172]");
        sb.AppendLine("    test r9, r9");
        sb.AppendLine($"    jz ldstv_{sDir}{w}");
        sb.AppendLine("    mov rax, r8");
        sb.AppendLine("    shr rax, 10");
        sb.AppendLine("    mov eax, dword ptr [r9+rax*4]");
        sb.AppendLine("    and eax, 3");
        sb.AppendLine("    shl eax, 10");
        sb.AppendLine("    and r8, 3FFh");
        sb.AppendLine("    or r8, rax");
        sb.AppendLine($"ldstv_{sDir}{w}:");
        if (sDir == "ld")
        {
            string sSzr = w == 0 ? "byte" : w == 1 ? "word" : w == 2 ? "dword" : "qword";
            if (w <= 1) sb.AppendLine($"    movzx eax, {sSzr} ptr [rdi+r8]");
            else if (w == 2) sb.AppendLine($"    mov eax, {sSzr} ptr [rdi+r8]");
            else sb.AppendLine($"    mov rax, {sSzr} ptr [rdi+r8]");
            sb.AppendLine("    mov [rbx+rcx*8], rax");
        }
        else
        {
            string st = new[] { "byte", "word", "dword", "qword" }[w];
            string vr = new[] { "al", "ax", "eax", "rax" }[w];
            sb.AppendLine("    mov rax, [rbx+rcx*8]");
            sb.AppendLine($"    mov {st} ptr [rdi+r8], {vr}");
        }
        sb.AppendLine("    jmp main_loop");
    }
}
