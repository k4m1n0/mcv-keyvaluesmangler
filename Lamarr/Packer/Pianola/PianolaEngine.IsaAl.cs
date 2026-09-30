namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    static void EmitIsaDecodeAl(StringBuilder sb)
    {
        sb.AppendLine("; ---- ISA carrier decode, valid Alpha64 AXP words to internal bytecode ----");
        sb.AppendLine("dec_entry:");
        sb.AppendLine("    mov rsi, rcx");
        sb.AppendLine("    lea r14, [rdi+1000h]   ; internal bytecode write pointer: VM code window");
        sb.AppendLine("dec_loop:");
        sb.AppendLine("    lea rax, [r14+16]");
        sb.AppendLine("    sub rax, rdi");
        sb.AppendLine("    cmp rax, 1FF0h");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov eax, dword ptr [rsi]");
        sb.AppendLine("    bswap eax");
        sb.AppendLine("    imul r12d, r12d, 1664525");
        sb.AppendLine("    add r12d, 1013904223");
        sb.AppendLine("    xor eax, r12d");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    cmp eax, 6BFA8001h");//ret $31,,1
        sb.AppendLine("    je d_blr");
        sb.AppendLine("    cmp eax, 6BFA0000h");//jmp $31,,0
        sb.AppendLine("    je d_bctr");
        sb.AppendLine("    mov r11d, eax");
        sb.AppendLine("    shr r11d, 26           ; opcode");
        foreach (var e in new (int, string)[] {
            (0x0A,"d_ld0"),(0x0C,"d_ld1"),(0x28,"d_ld2"),(0x29,"d_ld3"),
            (0x0E,"d_st0"),(0x0D,"d_st1"),(0x2C,"d_st2"),(0x2D,"d_st3"),
            (0x09,"d_lis"),(0x38,"d_pay") })
        {
            sb.AppendLine($"    cmp r11d, 0{e.Item1:X}h");
            sb.AppendLine("    je " + e.Item2);
        }
        sb.AppendLine("    cmp r11d, 08h");//standalone lda
        sb.AppendLine("    je h_bad");
        sb.AppendLine("    jmp d_tpl");
        //结束与返回兼作解码终止判定 随置换换位
        sb.AppendLine("d_blr:");
        sb.AppendLine("    mov byte ptr [r14], 0");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_END) == OP_END ? "    jmp dec_done" : "    jmp dec_loop");
        sb.AppendLine("d_bctr:");
        sb.AppendLine("    mov byte ptr [r14], 1");
        sb.AppendLine("    inc r14");
        sb.AppendLine(OpTable.Map(OP_RET) == OP_RET ? "    jmp dec_loop" : "    jmp dec_done");
        //MOVIMM ldah rd,hi + lda rd,lo
        sb.AppendLine("d_lis:");
        sb.AppendLine("    mov r11d, dword ptr [rsi]");
        sb.AppendLine("    bswap r11d");
        sb.AppendLine("    imul r12d, r12d, 1664525");
        sb.AppendLine("    add r12d, 1013904223");
        sb.AppendLine("    xor r11d, r12d");
        sb.AppendLine("    add rsi, 4");
        sb.AppendLine("    mov r10d, r11d");
        sb.AppendLine("    shr r10d, 26");
        sb.AppendLine("    cmp r10d, 08h          ; must be lda");
        sb.AppendLine("    jne h_bad");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 31            ; rd");
        sb.AppendLine("    cmp ecx, 15");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov r8d, r11d");
        sb.AppendLine("    shr r8d, 16");
        sb.AppendLine("    and r8d, 31            ; A3: the lda rb field carries width code + noise");
        sb.AppendLine("    shr r8d, 3");
        sb.AppendLine("    and r8d, 3");
        sb.AppendLine("    lea r10, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r10+r8]   ; width code -> internal width");
        sb.AppendLine("    cmp r8d, 2");
        sb.AppendLine("    ja h_bad");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shl edx, 16            ; hi16<<16");
        sb.AppendLine("    mov r9d, r11d");
        sb.AppendLine("    test r9d, 8000h");
        sb.AppendLine("    jz d_li_no");
        sb.AppendLine("    sub edx, 10000h        ; undo the +1 compensation");
        sb.AppendLine("d_li_no:");
        sb.AppendLine("    movzx r9d, r11w");
        sb.AppendLine("    add edx, r9d           ; uV");
        sb.AppendLine("    jmp d_mi_emit");
    }

    static void EmitIsaDecodeAl2(StringBuilder sb)
    {
        foreach (var e in new (string, int)[] { ("d_ld0",0x10),("d_ld1",0x11),("d_ld2",0x12),("d_ld3",0x13),
                                                ("d_st0",0x14),("d_st1",0x15),("d_st2",0x16),("d_st3",0x17) })
        {
            sb.AppendLine($"{e.Item1}:");
            sb.AppendLine($"    mov r10d, 0{e.Item2:X}h");
            sb.AppendLine("    jmp d_mem");
        }
        sb.AppendLine("d_mem:");
        sb.AppendLine("    mov byte ptr [r14], r10b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 21");
        sb.AppendLine("    and ecx, 0Fh           ; Ra, target/data");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 16");
        sb.AppendLine("    and r10d, 0Fh          ; Rb, address");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_pay:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    shr ecx, 19");
        sb.AppendLine("    and ecx, 3             ; tag");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    and edx, 7FFFFh        ; payload");
        sb.AppendLine("    test ecx, ecx");
        sb.AppendLine("    je d_payjmp");
        sb.AppendLine("    cmp ecx, 1");
        sb.AppendLine("    je d_payjcc");
        sb.AppendLine("    cmp ecx, 2");
        sb.AppendLine("    je d_paycall");
        sb.AppendLine("    jmp h_bad");
        sb.AppendLine("d_payjmp:");
        sb.AppendLine("    mov byte ptr [r14], 1Dh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], dx");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_paycall:");
        sb.AppendLine("    mov byte ptr [r14], 1Fh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], dx");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_payjcc:");
        sb.AppendLine("    mov byte ptr [r14], 1Eh");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov ecx, edx");
        sb.AppendLine("    shr ecx, 16            ; cond");
        sb.AppendLine("    and ecx, 7");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov word ptr [r14], dx");
        sb.AppendLine("    add r14, 2");
        sb.AppendLine("    jmp dec_loop");
    }

    static void EmitIsaDecodeAl3(StringBuilder sb)
    {
        const uint RA_RC = (31u << 21) | 31u;
        uint[] rgFamTpl = NotenrolleIsaAl.rgFamMap;
        sb.AppendLine("d_tpl:");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    and r10d, 0FC00FFE0h   ; clear ra(25:21), rb(20:16), rc(4:0)");
        sb.AppendLine($"    cmp r10d, 0{0x4C220603u & ~(RA_RC | NotenrolleIsaAl.RB):X}h");
        sb.AppendLine("    je d_movreg");
        sb.AppendLine($"    cmp r10d, 0{0x402205A3u & ~(RA_RC | NotenrolleIsaAl.RB):X}h");
        sb.AppendLine("    je d_cmp");
        sb.AppendLine($"    cmp r10d, 0{0x40220043u & ~(RA_RC | NotenrolleIsaAl.RB):X}h");
        sb.AppendLine("    je d_not");
        sb.AppendLine($"    cmp r10d, 0{NotenrolleIsaAl.T_BSWAP & ~(RA_RC | NotenrolleIsaAl.RB):X}h          ; A3/W3: BSWAP");
        sb.AppendLine("    je d_bswap");
        for (int k = 0; k < rgFamTpl.Length; k++)
        {
            sb.AppendLine($"    cmp r10d, 0{rgFamTpl[k] & ~(RA_RC | NotenrolleIsaAl.RB):X}h");
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
        sb.AppendLine("d_pair:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    and ecx, 0Fh           ; rc");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 21");
        sb.AppendLine("    and r10d, 0Fh          ; ra");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_bswap:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    and ecx, 0Fh           ; rc");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 19            ; rb[4:3] = width code");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    mov r9d, 18h           ; BSWAP family base");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        sb.AppendLine("d_not:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    and ecx, 0Fh           ; rc = the register (A3: no width bit in it)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 19            ; rb[4:3] = width code (word bits 20:19)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    mov r9d, 0F0h");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    and ecx, 0Fh");
        sb.AppendLine("    mov byte ptr [r14], cl");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    jmp dec_loop");
        foreach (var f in Fam)
        {
            sb.AppendLine($"d_fam_{f.Item2}:");
            sb.AppendLine($"    mov r9d, 0{f.Item1:X}h");
            sb.AppendLine("    jmp d_alu_a");
        }
        sb.AppendLine("d_alu_a:");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    and ecx, 0Fh           ; rc (A3: no width bit in it)");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 19            ; rb[4:3] = width code (word bits 20:19)");
        sb.AppendLine("    and edx, 3");
        sb.AppendLine("    lea r11, [d_wcode]");
        sb.AppendLine("    movzx r8d, byte ptr [r11+rdx]");
        sb.AppendLine("    add r9d, r8d");
        sb.AppendLine("    mov byte ptr [r14], r9b");
        sb.AppendLine("    inc r14");
        sb.AppendLine("    and ecx, 0Fh");
        sb.AppendLine("    mov r10d, eax");
        sb.AppendLine("    shr r10d, 21");
        sb.AppendLine("    and r10d, 0Fh          ; ra");
        sb.AppendLine("    shl r10d, 4");
        sb.AppendLine("    or ecx, r10d");
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
