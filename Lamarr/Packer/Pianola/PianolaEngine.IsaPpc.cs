namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    //指令字解码 大端字流解到栈缓冲 未知指令字走错误分支
    static void EmitIsaDecodePpc(StringBuilder sb)
    {
        sb.AppendLine("; ISA carrier decode, valid PPC64 BE words to internal bytecode");
        sb.AppendLine("dec_entry:");
        sb.AppendLine("    mov rsi, rcx ; carrier stream read pointer");
        sb.AppendLine("    lea r14, [rdi+1000h] ; internal bytecode write pointer: VM code window");
        sb.AppendLine("dec_loop:");
        sb.AppendLine("    lea rax, [r14+16]");
        sb.AppendLine("    sub rax, rdi");
        sb.AppendLine("    cmp rax, 1FF0h");
        sb.AppendLine("    ja h_bad ; decode buffer out of range");
        sb.AppendLine("    mov eax, dword ptr [rsi]");
        sb.AppendLine("    bswap eax ; big endian word to host order");
        
        sb.AppendLine("    call a_rest ; ChaCha12 at-rest keystream word to edx");
        sb.AppendLine("    xor eax, edx ; at-rest decrypt");
        sb.AppendLine("    xor r15d, eax ; fold the decoded word into the chain state");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    mov r11d, eax");
        sb.AppendLine("    shr r11d, 26 ; primary");
        foreach (var e in new (uint, string)[] {
            (19,"d_p19"),(15,"d_lis"),(31,"d_xform"),
            (34,"d_ld0"),(40,"d_ld1"),(32,"d_ld2"),(58,"d_ld3"),
            (38,"d_st0"),(44,"d_st1"),(36,"d_st2"),(62,"d_st3"),
            (26,"d_xori"),(25,"d_oris"),(27,"d_xoris"),(29,"d_andis") })
        {
            sb.AppendLine("    cmp r11d, " + e.Item1);
            sb.AppendLine("    je " + e.Item2);
        }
        sb.AppendLine("    jmp h_bad");

        //链接跳转与寄存器跳转兼作解码终止判定 该判定随置换换位 否则越过内核末尾
        sb.AppendLine("d_blr:");//END结束解码并交给执行
        sb.AppendLine("    mov byte ptr [r14], 0");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_END) == OP_END ? "    jmp dec_done" : "    jmp dec_loop");
        sb.AppendLine("d_bctr:");
        sb.AppendLine("    mov byte ptr [r14], 1");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_RET) == OP_RET ? "    jmp dec_loop" : "    jmp dec_done");
        sb.AppendLine("d_p19:");
        sb.AppendLine("    cmp eax, 4E800020h");
        sb.AppendLine("    je d_blr");
        sb.AppendLine("    cmp eax, 4E800420h");
        sb.AppendLine("    je d_bctr");
        sb.AppendLine("    jmp h_bad");

        //立即数搬移用高位装入与或立即数两条 目标寄存器字段承载宽度码
        sb.AppendLine("d_lis:");
        sb.AppendLine("    mov r11d, dword ptr [rsi]");
        sb.AppendLine("    bswap r11d");
        
        sb.AppendLine("    call a_rest ; ChaCha12 at-rest keystream word to edx");
        sb.AppendLine("    xor r11d, edx ; at-rest decrypt");
        sb.AppendLine("    xor r15d, r11d ; fold the decoded word into the chain state");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    mov r10d, r11d");
        sb.AppendLine("    shr r10d, 26");
        sb.AppendLine("    cmp r10d, 24 ; must be ori");
        sb.AppendLine("    jne h_bad");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 31 ; rd");
        sb.AppendLine("    cmp ecx, 15");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov r8d, r11d");
        sb.AppendLine("    shr r8d, 16");
        sb.AppendLine("    and r8d, 31 ; the ori rA field carries width code + noise");
        sb.AppendLine("    shr r8d, 3");
        sb.AppendLine("    and r8d, 3");
        sb.AppendLine("    lea r10, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r10+r8] ; width code to internal width");
        sb.AppendLine("    cmp r8d, 2");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    movzx eax, ax");
        sb.AppendLine("    shl eax, 16");
        sb.AppendLine("    movzx edx, r11w");
        sb.AppendLine("    or eax, edx ; uV");
        sb.AppendLine("    mov byte ptr [r14], 8");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], r8b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    cmp r8d, 0");
        sb.AppendLine("    je d_mi1");
        sb.AppendLine("    cmp r8d, 1");
        sb.AppendLine("    je d_mi2");
        sb.AppendLine("    mov dword ptr [r14], eax");
        sb.AppendLine("    add r14, 4");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_mi2:");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_mi1:");
        sb.AppendLine("    mov byte ptr [r14], al");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");

        //载荷直传 内部偏移原样落在立即数字段
        sb.AppendLine("d_xori:");//JMP
        sb.AppendLine("    mov byte ptr [r14], 1Dh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_oris:");//CALL
        sb.AppendLine("    mov byte ptr [r14], 1Fh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_andis:");//Jcc
        sb.AppendLine("    mov byte ptr [r14], 1Eh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; cond");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_xoris:");//CMP
        sb.AppendLine("    mov byte ptr [r14], 1Ch");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp d_pair");

        sb.AppendLine("d_ld0:"); sb.AppendLine("    mov ecx, 10h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_ld1:"); sb.AppendLine("    mov ecx, 11h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_ld2:"); sb.AppendLine("    mov ecx, 12h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_ld3:"); sb.AppendLine("    mov ecx, 13h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_st0:"); sb.AppendLine("    mov ecx, 14h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_st1:"); sb.AppendLine("    mov ecx, 15h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_st2:"); sb.AppendLine("    mov ecx, 16h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_st3:"); sb.AppendLine("    mov ecx, 17h"); sb.AppendLine("    jmp d_mem");
        sb.AppendLine("d_mem:");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp d_pair");

        //双寄存器载荷 低位是目标寄存器字段 高位是源寄存器字段
        sb.AppendLine("d_pair:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; rD field");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 16");
        sb.AppendLine("    and r10d, 0Fh ; rA field");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");

        //扩展族里搬移走带进位加 取反走等价 其余走算术逻辑类
        sb.AppendLine("d_xform:");
        sb.AppendLine("    mov r11d, eax");
        sb.AppendLine("    shr r11d, 1");
        sb.AppendLine("    and r11d, 3FFh ; XO");
        sb.AppendLine("    cmp r11d, 10 ; ADDC to MOVREG");
        sb.AppendLine("    je d_movreg");
        sb.AppendLine("    cmp r11d, 284 ; EQV to NOT");
        sb.AppendLine("    je d_not");
        sb.AppendLine($"    cmp r11d, {(int)NotenrolleIsa.X_BSWAP} ; BSWAP (same opcode const as the ppc encoder)");
        sb.AppendLine("    je d_bswap");
        foreach (var f in Fam)
        {
            sb.AppendLine("    cmp r11d, " + XoOf2(f.Item1));
            sb.AppendLine("    je d_fam_" + f.Item2);
        }
        sb.AppendLine("    jmp h_bad");
        foreach (var f in Fam)
        {
            sb.AppendLine($"d_fam_{f.Item2}:");
            sb.AppendLine($"    mov r9d, 0{f.Item1:X}h");
            sb.AppendLine("    jmp d_alu");
        }
        sb.AppendLine("d_movreg:");
        sb.AppendLine("    mov byte ptr [r14], 9");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp d_pair");
        sb.AppendLine("d_not:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; rd (the register field carries no width bit)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 14 ; rB[4:3] = width code (2 bit)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    mov r9d, 0F0h");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_bswap:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; rd");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 14 ; rB[4:3] = width code");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    mov r9d, 18h ; BSWAP family base");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_alu:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; rd (the register field carries no width bit)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 14 ; rB[4:3] = width code (2 bit)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    add r9d, r8d ; internal op = family base + width");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 16");
        sb.AppendLine("    and r10d, 0Fh ; gS");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
    }

}
