L_DEF  equ 012h
L_1B   equ 0FFh + L_DEF
L_2B   equ 0FFFFh + L_1B
HM     equ 07FFFFh

ST_INPOS  equ 00h
ST_IHIST  equ 04h
ST_OUTPOS equ 08h
ST_TAG    equ 0Ch
ST_IBC    equ 10h
ST_FLAGS  equ 14h
ST_REMAIN equ 18h
ST_SRC    equ 1Ch
ST_NIB    equ 20h
ST_SRCBASE equ 24h
ST_SRCLEN  equ 28h
ST_PAGESTART equ 2Ch
ST_PAGEEND  equ 30h
ST_DSTLEN   equ 34h

F_PENDING equ 1
F_FROMHIST equ 2
F_PSKIP   equ 4
F_SENT    equ 8

.code

NibRead MACRO
    LOCAL nb,nd
    test r12b, 1
    jz nb
    mov al, [rsi]
    shr al, 4
    mov ah, [rsi+1]
    shl ah, 4
    or al, ah
    inc rsi
    jmp nd
nb: lodsb
nd:
ENDM

Read20 MACRO
    LOCAL rl
    mov eax, dword ptr [rsi]
    test r12b, 1
    jz rl
    shr eax, 4
rl: and eax, 0FFFFFh
    inc rsi
ENDM

Iamdec PROC EXPORT
    push rbx
    push rbp
    push r12
    push r13
    push r14
    push r15
    sub rsp, 38h
    ; [rsp+00h]=bSkip [rsp+08h]=uSrc [rsp+10h]=flags
    ; [rsp+18h]=pageStart [rsp+20h]=tag&7Fh [rsp+28h]=len [rsp+30h]=dist
    mov rbx, rcx                        ; state (56B block)
    mov r15, rdx                        ; hist (512KB window, caller-owned)
    mov rdi, r8                         ; page (4KB output)
    mov [rbx+ST_SRCBASE], r9d           ; save src base
    mov r13, r9                         ; src end
    mov eax, [rbx+ST_SRCLEN]
    add r13, rax                        ; r13 = src + srcLen
    mov rsi, r9                         ; src ptr
    mov eax, [rbx+ST_INPOS]
    add rsi, rax                        ; rsi = src + inPos
    mov r14d, [rbx+ST_PAGEEND]          ; r14 = pageEnd (caller-set)
    mov r11d, [rbx+ST_OUTPOS]           ; r11 = outPos (global)
    mov r12d, [rbx+ST_NIB]              ; r12b = nibble sync bit
    mov ebp, [rbx+ST_TAG]               ; ebp = tag
    mov r8d, [rbx+ST_IBC]               ; r8 = iBC (bit count)
    mov r10d, [rbx+ST_REMAIN]           ; r10 = remain (copy chunk)
    mov edx, [rbx+ST_IHIST]             ; edx = iHist (window offset)
    mov eax, [rbx+ST_FLAGS]
    mov [rsp+10h], eax                  ; flags
    mov eax, [rbx+ST_SRC]
    mov [rsp+08h], eax                  ; uSrc
    mov eax, [rbx+ST_PAGESTART]
    mov [rsp+18h], eax                  ; pageStart

; !! paged state machine: decodes [uOutPos,pageEnd) so plaintext never spans pages
; caller owns pageStart/pageEnd/dstLen
; decoder persists inPos/outPos/tag/iBC/flags/remain/src/nib
; main loop
lam_loop:
    cmp r11d, r14d
    jae lam_done
    mov ecx, [rsp+18h]
    xor eax, eax
    cmp r11d, ecx
    jae lam_skip0
    mov eax, 1
lam_skip0:
    mov [rsp+00h], eax

    test byte ptr [rsp+10h], F_PENDING
    jz di_new
    test byte ptr [rsp+10h], F_SENT
    jnz pump_raw
    jmp pump_hist

; new item
di_new:
    test r8b, r8b
    jnz di_have_tag
    mov rax, r13
    sub rax, r12
    cmp rsi, rax
    jae di_exhaust
    NibRead
    mov ebp, eax
di_have_tag:
    mov al, bpl
    shl al, 1
    jc di_match

; literal
    mov rax, r13
    sub rax, r12
    cmp rsi, rax
    jae di_exhaust
    NibRead
    mov r9d, edx
    and r9d, HM
    mov [r15+r9], al
    inc edx
    test byte ptr [rsp+00h], 1
    jnz wb_nopg
    mov ecx, r11d
    and ecx, 0FFFh
    mov [rdi+rcx], al
wb_nopg:
    inc r11d
    jmp di_adv

; match
di_match:
    mov eax, ebp
    and eax, 7Fh
    mov [rsp+20h], eax

; ReadDist -> r9d
    Read20
    cmp r11d, 0881h
    jae rd_long
    mov r9d, eax
    shr r9d, 1
    test al, 1
    jz rd_s0
    add rsi, r12
    xor r12b, 1
    and r9d, 07FFh
    add r9d, 081h
    jmp rd_done
rd_s0:
    and r9d, 07Fh
    inc r9d
    jmp rd_done
rd_long:
    mov r9d, eax
    shr r9d, 2
    mov ecx, eax
    and ecx, 3
    cmp ecx, 0
    je rd_l00
    cmp ecx, 1
    je rd_l01
    cmp ecx, 2
    je rd_l10
    add rsi, r12
    inc rsi
    xor r12b, 1
    and r9d, 03FFFFh
    add r9d, 04441h
    jmp rd_done
rd_l10:
    inc rsi
    and r9d, 03FFFh
    add r9d, 0441h
    jmp rd_done
rd_l01:
    add rsi, r12
    xor r12b, 1
    and r9d, 03FFh
    add r9d, 041h
    jmp rd_done
rd_l00:
    and r9d, 03Fh
    inc r9d
rd_done:
    mov [rsp+30h], r9d                  ; dist (stash)

; ReadLen -> eax
    movzx eax, word ptr [rsi]
    test r12b, 1
    jz rl12a
    shr eax, 4
rl12a: and  eax, 0FFFh
    add rsi, r12
    xor r12b, 1
    mov ecx, eax
    and ecx, 0Fh
    cmp ecx, 0Fh
    jne rl_s
    inc rsi
    cmp eax, 0FFFh
    jne rl_m
    test r12b, 1
    jz rl_u16
    mov eax, dword ptr [rsi]
    shr eax, 4
    and eax, 0FFFFh
    jmp rl_u
rl_u16:
    movzx eax, word ptr [rsi]
rl_u:
    add eax, L_1B
    add rsi, 2
    jmp rl_done
rl_s:
    and eax, 0Fh
    add eax, 3
    jmp rl_done
rl_m:
    shr eax, 4
    add eax, 012h
rl_done:
    mov [rsp+28h], eax

; len == L_2B -> copy chunk
    cmp eax, L_2B
    jne di_match_hist

; copy chunk (raw copy from input)
    test r12b, 1
    jz cn0
    movzx ecx, byte ptr [rsi-4]
    and ecx, 0FCh
    shl ecx, 5
    inc rsi
    xor r12d, r12d
    jmp cc_calc
cn0:
    movzx ecx, word ptr [rsi-5]
    and ecx, 0FC0h
    shl ecx, 1
cc_calc:
    add ecx, [rsp+20h]
    add ecx, 4
    shl ecx, 1
    mov eax, [rsp+00h]
    shl eax, 2
    or eax, F_PENDING or F_SENT
    mov [rsp+10h], eax
    shl ecx, 2
    mov r10d, ecx
    jmp pump_raw

; normal match (history copy)
di_match_hist:
    mov ecx, [rsp+30h]
    cmp r11d, ecx
    jb di_err_dist
    mov eax, [rsp+28h]
    add eax, r11d
    cmp eax, [rbx+ST_DSTLEN]
    ja di_err_out
    mov eax, [rsp+00h]
    shl eax, 2
    or eax, F_PENDING or F_FROMHIST
    mov [rsp+10h], eax
    mov eax, r11d
    sub eax, [rsp+30h]
    mov [rsp+08h], eax
    mov eax, [rsp+28h]
    mov r10d, eax
    jmp pump_hist

; pump: raw copy from input
pump_raw:
pr_loop:
    test r10d, r10d
    jz pump_finish
    cmp r11d, r14d
    jae lam_done
    cmp rsi, r13
    jae di_err_overrun
    lodsb
    mov r9d, edx
    and r9d, HM
    mov [r15+r9], al
    inc edx
    test byte ptr [rsp+10h], F_PSKIP
    jnz pr_nopg
    mov ecx, r11d
    and ecx, 0FFFh
    mov [rdi+rcx], al
pr_nopg:
    inc r11d
    dec r10d
    jmp pr_loop

; pump: history copy (byte by byte)
pump_hist:
ph_loop:
    test r10d, r10d
    jz pump_finish
    cmp r11d, r14d
    jae lam_done
    mov eax, [rsp+08h]
    and eax, HM
    mov al, [r15+rax]
    inc dword ptr [rsp+08h]
    mov r9d, edx
    and r9d, HM
    mov [r15+r9], al
    inc edx
    test byte ptr [rsp+10h], F_PSKIP
    jnz ph_nopg
    mov ecx, r11d
    and ecx, 0FFFh
    mov [rdi+rcx], al
ph_nopg:
    inc r11d
    dec r10d
    jmp ph_loop

; pump finish
pump_finish:
    and byte ptr [rsp+10h], 0FEh
    test byte ptr [rsp+10h], F_SENT
    jnz pf_sent
    shl ebp, 1
    inc r8b
    and r8b, 7
    jmp di_after_pump
pf_sent:
    mov r8b, 0
di_after_pump:
    cmp r11d, r14d
    jae lam_done
    jmp lam_loop

; tag advance
di_adv:
    shl ebp, 1
    inc r8b
    and r8b, 7
    jmp lam_loop

; errors
di_exhaust:
    mov eax, 105h
    jmp lam_exit
di_err_dist:
    mov eax, 104h
    jmp lam_exit
di_err_out:
    mov eax, 106h
    jmp lam_exit
di_err_overrun:
    mov eax, 107h
    jmp lam_exit

; !! write back state so caller resumes next page
; hist/page buffers are owned by caller
lam_done:
    xor eax, eax
lam_exit:
    mov ecx, esi
    sub ecx, [rbx+ST_SRCBASE]
    mov [rbx+ST_INPOS], ecx
    mov [rbx+ST_IHIST], edx
    mov [rbx+ST_OUTPOS], r11d
    mov [rbx+ST_TAG], ebp
    mov [rbx+ST_IBC], r8b
    mov ecx, [rsp+10h]
    mov [rbx+ST_FLAGS], ecx
    mov [rbx+ST_REMAIN], r10d
    mov ecx, [rsp+08h]
    mov [rbx+ST_SRC], ecx
    mov [rbx+ST_NIB], r12b
    add rsp, 38h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbp
    pop rbx
    ret
Iamdec ENDP

END