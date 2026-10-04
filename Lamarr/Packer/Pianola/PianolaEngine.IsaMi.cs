namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    static void EmitIsaDecodeMi(StringBuilder sb)//MIPS64指令字解码
    {
        sb.AppendLine("; ISA carrier decode, valid MIPS64 words to internal bytecode");
        sb.AppendLine("dec_entry:");
        sb.AppendLine("    mov rsi, rcx");
        sb.AppendLine("    lea r14, [rdi+1000h] ; internal bytecode write pointer: VM code window");
        sb.AppendLine("dec_loop:");
        sb.AppendLine("    lea rax, [r14+16]");
        sb.AppendLine("    sub rax, rdi");
        sb.AppendLine("    cmp rax, 1FF0h");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov eax, dword ptr [rsi]");
        sb.AppendLine("    bswap eax");
        
        sb.AppendLine("    call a_rest ; ChaCha12 at-rest keystream word to edx");
        sb.AppendLine("    xor eax, edx");
        sb.AppendLine("    xor r15d, eax ; fold the decoded word into the chain state");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    cmp eax, 03E00008h");//jr $ra
        sb.AppendLine("    je d_blr");
        sb.AppendLine("    cmp eax, 03200008h");//jr $25
        sb.AppendLine("    je d_bctr");
        sb.AppendLine("    mov r11d, eax");
        sb.AppendLine("    shr r11d, 26 ; opcode");
        foreach (var e in new (int, string)[] {
            (0x0F,"d_lis"),(0x0E,"d_not"),(0x04,"d_beq"),(0x05,"d_call"),
            (0x20,"d_ld0"),(0x21,"d_ld1"),(0x23,"d_ld2"),(0x37,"d_ld3"),
            (0x28,"d_st0"),(0x29,"d_st1"),(0x2B,"d_st2"),(0x3F,"d_st3") })
        {
            sb.AppendLine($"    cmp r11d, 0{e.Item1:X}h");
            sb.AppendLine("    je " + e.Item2);
        }
        sb.AppendLine("    test r11d, r11d");
        sb.AppendLine("    je d_rtype");
        sb.AppendLine("    jmp h_bad");
        sb.AppendLine("d_blr:");//END与RET兼作解码终止判定 随操作码置换换位
        sb.AppendLine("    mov byte ptr [r14], 0");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_END) == OP_END ? "    jmp dec_done" : "    jmp dec_loop");
        sb.AppendLine("d_bctr:");
        sb.AppendLine("    mov byte ptr [r14], 1");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_RET) == OP_RET ? "    jmp dec_loop" : "    jmp dec_done");
        sb.AppendLine("d_lis:");//MOVIMM lui + ori rs=wB
        sb.AppendLine("    mov r11d, dword ptr [rsi]");
        sb.AppendLine("    bswap r11d");
        
        sb.AppendLine("    call a_rest ; ChaCha12 at-rest keystream word to edx");
        sb.AppendLine("    xor r11d, edx");
        sb.AppendLine("    xor r15d, r11d ; fold the decoded word into the chain state");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    mov r10d, r11d");
        sb.AppendLine("    shr r10d, 26");
        sb.AppendLine("    cmp r10d, 0Dh ; must be ori");
        sb.AppendLine("    jne h_bad");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    and ecx, 31 ; rd");
        sb.AppendLine("    cmp ecx, 15");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov r8d, r11d");
        sb.AppendLine("    shr r8d, 21");
        sb.AppendLine("    and r8d, 31 ; the ori rs field carries width code + noise");
        sb.AppendLine("    shr r8d, 3");
        sb.AppendLine("    and r8d, 3");
        sb.AppendLine("    lea r10, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r10+r8] ; width code to internal width");
        sb.AppendLine("    cmp r8d, 2");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shl edx, 16 ; lui imm16 << 16");
        sb.AppendLine("    movzx r9d, r11w");
        sb.AppendLine("    or edx, r9d ; uV");
        sb.AppendLine("    jmp d_mi_emit");
        foreach (var e in new (string, int)[] { ("d_ld0",0x10),("d_ld1",0x11),("d_ld2",0x12),("d_ld3",0x13),//LD/ST rt是目标或数据寄存器rs是地址寄存器
                                                ("d_st0",0x14),("d_st1",0x15),("d_st2",0x16),("d_st3",0x17) })
        {
            sb.AppendLine($"{e.Item1}:");
            sb.AppendLine($"    mov r10d, 0{e.Item2:X}h");
            sb.AppendLine("    jmp d_mem");
        }
        sb.AppendLine("d_mem:");
        sb.AppendLine("    mov byte ptr [r14], r10b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 21");
        sb.AppendLine("    and r10d, 0Fh ; rs = address register");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    and ecx, 0Fh ; rt = target/data register");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_not:");//NOT xori
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    and ecx, 0Fh ; rt = the register (no width bit in it)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 24 ; rs[4:3] = width code (word bits 25:24)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r10, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r10+rdx]");
        sb.AppendLine("    mov r9d, 0F0h");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    and ecx, 0Fh");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
    }

    static void EmitIsaDecodeMi2(StringBuilder sb)
    {
        int[] rgFunct = NotenrolleIsaMi.rgFamMap;
        sb.AppendLine("d_beq:");//BEQ rt=0走JMP rt=cond+1走Jcc
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    and ecx, 31 ; rt");
        sb.AppendLine("    test ecx, ecx");
        sb.AppendLine("    jnz d_jcc");
        sb.AppendLine("    mov byte ptr [r14], 1Dh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_jcc:");
        sb.AppendLine("    mov byte ptr [r14], 1Eh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    dec ecx");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_call:");//BNE走CALL
        sb.AppendLine("    mov byte ptr [r14], 1Fh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], ax");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_rtype:");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    and r10d, 3Fh ; funct");
        sb.AppendLine("    cmp r10d, 21h");
        sb.AppendLine("    je d_movreg");
        sb.AppendLine("    cmp r10d, 2Ah");
        sb.AppendLine("    je d_cmp");
        sb.AppendLine($"    cmp r10d, {NotenrolleIsaMi.F_BSWAP:X}h ; dsbh form maps to BSWAP");
        sb.AppendLine("    je d_bswap");
        for (int k = 0; k < Fam.Length; k++)
        {
            sb.AppendLine($"    cmp r10d, 0{rgFunct[k]:X}h");
            sb.AppendLine("    je d_fam_" + Fam[k].Item2);
        }
        sb.AppendLine("    jmp h_bad");
        sb.AppendLine("d_movreg:");
        sb.AppendLine("    mov byte ptr [r14], 9");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp d_pair");
        sb.AppendLine("d_cmp:");
        sb.AppendLine("    mov byte ptr [r14], 1Ch");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp d_pair");
        for (int k = 0; k < Fam.Length; k++)
        {
            sb.AppendLine($"d_fam_{Fam[k].Item2}:");
            sb.AppendLine($"    mov r9d, 0{Fam[k].Item1:X}h");
            sb.AppendLine("    jmp d_alu");
        }
        sb.AppendLine("d_pair:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 11");
        sb.AppendLine("    and ecx, 0Fh ; rd");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 21");
        sb.AppendLine("    and r10d, 0Fh ; rs");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_alu:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 11");
        sb.AppendLine("    and ecx, 0Fh ; rd (no width bit in it)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 19 ; rt[4:3] = width code (word bits 20:19)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 11");
        sb.AppendLine("    and r10d, 0Fh ; rd");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh ; rs");
        sb.AppendLine("    shl ecx, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_bswap:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 11");
        sb.AppendLine("    and ecx, 0Fh ; rd");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 19 ; rt[4:3] = width code");
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
        sb.AppendLine("d_mi_emit:");
        sb.AppendLine("    mov byte ptr [r14], 8");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], r8b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    test r8d, r8d");
        sb.AppendLine("    je d_mi1");
        sb.AppendLine("    cmp r8d, 1");
        sb.AppendLine("    je d_mi2");
        sb.AppendLine("    mov dword ptr [r14], edx");
        sb.AppendLine("    add r14, 4");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_mi2:");
        sb.AppendLine("    mov word ptr [r14], dx");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_mi1:");
        sb.AppendLine("    mov byte ptr [r14], dl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
    }

}