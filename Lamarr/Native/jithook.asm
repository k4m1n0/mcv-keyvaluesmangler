.code

PUBLIC g_orig, g_keyA, g_keyB, g_sigs, g_sigCount
PUBLIC InstallJitHook, SetJitHookKey, AddPayloadSig, VerifyJitHook, SetAntiDebugFlag, SetJitSlots

; rcx=ansi apiName -> rax=addr (kernel32, handles forwarders)
ResolveApi PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    mov r13, rcx                        ; api name
    mov rax, gs:[60h]                   ; PEB
    mov rax, [rax+18h]
    lea rbx, [rax+20h]                  ; InMemoryOrderModuleList
    mov rax, [rbx]
ra_kl:
    cmp rax, rbx
    je ra_fail
    mov rdx, [rax+50h]                  ; BaseDllName.Buffer
    mov r8, [rdx]
    mov r9, [rdx+8]
    mov r10, 006E00720065006Bh          ; "kern"
    mov r11, 00320033006C0065h          ; "el32"
    cmp r8, r10
    jne ra_kup
    cmp r9, r11
    je ra_got
ra_kup:
    mov r10, 004E00520045004Bh          ; "KERN"
    mov r11, 00320033004C0045h          ; "EL32"
    cmp r8, r10
    jne ra_nxt
    cmp r9, r11
    je ra_got
ra_nxt:
    mov rax, [rax]
    jmp ra_kl
ra_got:
    mov rdx, [rax+20h]                  ; DllBase (kernel32)
    mov rdi, r13
ra_exp:
    ; rdx=module base, rdi=name (ansi)
    mov eax, [rdx+3Ch]
    lea r8, [rdx+rax]
    mov eax, [r8+88h]
    test eax, eax
    jz ra_fail
    add rax, rdx
    mov r8, rax
    mov ebx, [r8+18h]
    test ebx, ebx
    jz ra_fail
    mov esi, [r8+20h]
    add rsi, rdx
    mov r11d, [r8+24h]
    add r11, rdx
    xor r10d, r10d
ra_nml:
    cmp r10d, ebx
    jae ra_fail
    mov eax, [rsi+r10*4]
    lea r9, [rdx+rax]
    mov rcx, rdi
ra_cmpl:
    mov al, [rcx]
    test al, al
    jnz ra_c2
    cmp byte ptr [r9], 0
    je ra_mok
    jmp ra_mnx
ra_c2:
    cmp al, [r9]
    jne ra_mnx
    inc rcx
    inc r9
    jmp ra_cmpl
ra_mnx:
    inc r10d
    jmp ra_nml
ra_mok:
    movzx eax, word ptr [r11+r10*2]
    mov ecx, [r8+1Ch]
    add rcx, rdx
    mov eax, [rcx+rax*4]
    ; forwarder check
    mov r9d, [rdx+3Ch]
    lea r9, [rdx+r9+18h+70h]
    mov r10d, [r9]
    mov r9d, [r9+4]
    cmp eax, r10d
    jb rfok
    add r9d, r10d
    cmp eax, r9d
    ja rfok
    ; forwarder "DLL.Func"
    lea rdi, [rdx+rax]
    mov rsi, rdi
fw_fd:
    mov al, [rsi]
    inc rsi
    cmp al, '.'
    jne fw_fd
    mov r12, rsi
    sub r12, rdi
    sub r12, 1
    mov rax, gs:[60h]
    mov rax, [rax+18h]
    lea rbx, [rax+20h]
    mov rax, [rbx]
fw_loop:
    cmp rax, rbx
    je ra_fail
    mov r8, [rax+50h]
    xor r9d, r9d
fw_cmp:
    cmp r9, r12
    jae fw_got
    movzx r10d, byte ptr [rdi+r9]
    movzx r11d, word ptr [r8+r9*2]
    cmp r10d, 61h
    jb fw_u1
    sub r10d, 20h
fw_u1:
    cmp r11d, 61h
    jb fw_u2
    sub r11d, 20h
fw_u2:
    cmp r10d, r11d
    jne fw_nxt
    inc r9
    jmp fw_cmp
fw_nxt:
    mov rax, [rax]
    jmp fw_loop
fw_got:
    mov rdx, [rax+20h]
    mov rdi, rsi
    jmp ra_exp
rfok:
    add rax, rdx
    jmp ra_ret
ra_fail:
    xor eax, eax
ra_ret:
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
ResolveApi ENDP



; rcx=ptr rdx=len -> eax
Crc32Fn PROC
    mov eax, 0FFFFFFFFh                 ; crc=~0
    test rdx, rdx
    jz crc_done
    xor r8, r8                          ; i=0
crc_loop:
    movzx r9d, byte ptr [rcx+r8]
    xor eax, r9d
    mov r10d, 8
crc_bit:
    mov r11d, eax
    and r11d, 1
    neg r11d
    and r11d, 0EDB88320h
    shr eax, 1
    xor eax, r11d
    dec r10d
    jnz crc_bit
    inc r8
    cmp r8, rdx
    jb crc_loop
crc_done:
    not eax
    ret
Crc32Fn ENDP



; rcx=dst rdx=src r8d=len r9d=key
; !! length-preserving dual-state nonlinear stream
; s1 = key ^ len*C1; s2 = key ^ C2 ^ len; s1 = s1*0x9E3779B1+0x9747B28C
; s2 = (s2*0x85EBCA6B+0xC2B2AE35) ^ (s1>>8) ^ (s1<<16); s2 = rol(s2,13); out = (s1>>24)^(s2>>16)^(s2>>24)
GenPerm PROC
    push rsi
    push rbx
    cmp qword ptr [g_permKey], r9
    je gp_done
    lea r8, g_permHi
    xor eax, eax
gp_hinit:
    mov byte ptr [r8+rax], al
    inc eax
    cmp eax, 16
    jb gp_hinit
    mov r9, qword ptr [rsp+60h]         ; key (XorDecrypt frame via push/ret)
    mov r10, 9E3779B97F4A7C15h
    xor r9, r10
    mov esi, 15
gp_hloop:
    mov r10, 0100019301000193h
    imul r9, r10
    mov r10, 9E3779B97F4A7C15h
    add r9, r10
    mov rax, r9
    shr rax, 56
    xor edx, edx
    lea ecx, [rsi+1]
    div ecx
    mov al, byte ptr [r8+rsi]
    mov bl, byte ptr [r8+rdx]
    mov byte ptr [r8+rsi], bl
    mov byte ptr [r8+rdx], al
    dec esi
    jns gp_hloop
    lea r8, g_permLo
    xor eax, eax
gp_linit:
    mov byte ptr [r8+rax], al
    inc eax
    cmp eax, 16
    jb gp_linit
    mov r9, qword ptr [rsp+60h]
    mov r10, 85EBCA6B85EBCA6Bh
    xor r9, r10
    mov esi, 15
gp_lloop:
    mov r10, 9E3779B19E3779B1h
    imul r9, r10
    mov r10, 9747B28C9747B28Ch
    add r9, r10
    mov rax, r9
    shr rax, 56
    xor edx, edx
    lea ecx, [rsi+1]
    div ecx
    mov al, byte ptr [r8+rsi]
    mov bl, byte ptr [r8+rdx]
    mov byte ptr [r8+rsi], bl
    mov byte ptr [r8+rdx], al
    dec esi
    jns gp_lloop
    lea r10, g_permHi
    lea r11, g_invHi
    xor ecx, ecx
gp_invh:
    movzx eax, byte ptr [r10+rcx]
    mov byte ptr [r11+rax], cl
    inc ecx
    cmp ecx, 16
    jb gp_invh
    lea r10, g_permLo
    lea r11, g_invLo
    xor ecx, ecx
gp_invl:
    movzx eax, byte ptr [r10+rcx]
    mov byte ptr [r11+rax], cl
    inc ecx
    cmp ecx, 16
    jb gp_invl
    mov r9, qword ptr [rsp+60h]
    mov qword ptr [g_permKey], r9
gp_done:
    pop rbx
    pop rsi
    ret
GenPerm ENDP



XorDecrypt PROC
    push r12
    push r13
    push r14
    push r15
    sub rsp, 60h                        ; C1[0]C2[8]C3[10]C4[18] uPrev[20] iHalf[28] out[2C] save[30] len[38] src[40] key[48] dst[50]
    test r8d, r8d
    jz xd_done
    lea r14, g_invHi
    lea r15, g_invLo
    mov qword ptr [rsp+48h], r9         ; save key
    mov dword ptr [rsp+38h], r8d        ; save len
    mov qword ptr [rsp+50h], rcx        ; save dst
    mov qword ptr [rsp+40h], rdx        ; save src
    call GenPerm
    mov r9, qword ptr [rsp+48h]         ; restore key
    mov r8d, dword ptr [rsp+38h]        ; restore len
    mov rcx, qword ptr [rsp+50h]        ; restore dst
    mov rdx, qword ptr [rsp+40h]        ; restore src
    ; C1 = (key ^ 0x9E3779B97F4A7C15) | 1
    mov rax, r9
    mov r10, 9E3779B97F4A7C15h
    xor rax, r10
    or rax, 1
    mov qword ptr [rsp+0], rax
    ; C2 = ((key * 0x0100019301000193) ^ 0x85EBCA6B85EBCA6B) | 1
    mov rax, r9
    mov r10, 0100019301000193h
    imul rax, r10
    mov r10, 85EBCA6B85EBCA6Bh
    xor rax, r10
    or rax, 1
    mov qword ptr [rsp+8], rax
    ; C3 = (rol(key,9) ^ 0x9747B28C9747B28C) | 1
    mov rax, r9
    rol rax, 9
    mov r10, 9747B28C9747B28Ch
    xor rax, r10
    or rax, 1
    mov qword ptr [rsp+10h], rax
    ; C4 = ((key * 0x85EBCA6B85EBCA6B) ^ 0xC2B2AE35C2B2AE35) | 1
    mov rax, r9
    mov r10, 85EBCA6B85EBCA6Bh
    imul rax, r10
    mov r10, 0C2B2AE35C2B2AE35h
    xor rax, r10
    or rax, 1
    mov qword ptr [rsp+18h], rax
    ; iHalf = len/2
    mov r12d, r8d
    shr r12d, 1
    mov dword ptr [rsp+28h], r12d
    ; variant dispatch
    mov r11d, dword ptr [g_variant]
    and r11d, 3
    jz xd_v0
    cmp r11d, 1
    je xd_v1
    cmp r11d, 3
    je xd_v0
    jmp xd_v2
xd_v0:
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    xor r11d, r11d                      ; i=0
xd0_loop:
    imul rax, qword ptr [rsp+0]         ; s1 *= C1
    add rax, qword ptr [rsp+10h]        ; s1 += C3
    imul r10, qword ptr [rsp+18h]       ; s2 *= C4
    add r10, qword ptr [rsp+8]          ; s2 += C2
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shr r12, 8
    xor r10, r12
    mov r12, rax
    shl r12, 16
    xor r10, r12
    rol r10, 13
    mov r12, rax
    shr r12, 24
    mov r13, r10
    shr r13, 16
    xor r12, r13
    mov r13, r10
    shr r13, 24
    xor r12, r13                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = cipher in (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    inc r11d
    cmp r11d, dword ptr [rsp+28h]       ; i < iHalf
    jb xd0_loop
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    mov r11d, r8d
    dec r11d                            ; i = len-1
xd0_rev:
    imul rax, qword ptr [rsp+0]         ; s1 *= C1
    add rax, qword ptr [rsp+10h]        ; s1 += C3
    imul r10, qword ptr [rsp+18h]       ; s2 *= C4
    add r10, qword ptr [rsp+8]          ; s2 += C2
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shr r12, 8
    xor r10, r12
    mov r12, rax
    shl r12, 16
    xor r10, r12
    rol r10, 13
    mov r12, rax
    shr r12, 24
    mov r13, r10
    shr r13, 16
    xor r12, r13
    mov r13, r10
    shr r13, 24
    xor r12, r13                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = 输入密文 (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    dec r11d
    cmp r11d, dword ptr [rsp+28h]       ; i >= iHalf
    jge xd0_rev
    jmp xd_done
xd_v1:
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    xor r11d, r11d                      ; i=0
xd1_loop:
    imul rax, qword ptr [rsp+18h]       ; s1 *= C4
    add rax, qword ptr [rsp+8]          ; s1 += C2
    imul r10, qword ptr [rsp+0]         ; s2 *= C1
    add r10, qword ptr [rsp+10h]        ; s2 += C3
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shr r12, 8
    xor r10, r12
    mov r12, rax
    shl r12, 16
    xor r10, r12
    rol r10, 11
    mov r12, r10
    shr r12, 24
    mov r13, rax
    shr r13, 16
    xor r12, r13
    mov r13, rax
    shr r13, 24
    xor r12, r13                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = 输入密文 (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    inc r11d
    cmp r11d, dword ptr [rsp+28h]       ; i < iHalf
    jb xd1_loop
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    mov r11d, r8d
    dec r11d                            ; i = len-1
xd1_rev:
    imul rax, qword ptr [rsp+18h]       ; s1 *= C4
    add rax, qword ptr [rsp+8]          ; s1 += C2
    imul r10, qword ptr [rsp+0]         ; s2 *= C1
    add r10, qword ptr [rsp+10h]        ; s2 += C3
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shr r12, 8
    xor r10, r12
    mov r12, rax
    shl r12, 16
    xor r10, r12
    rol r10, 11
    mov r12, r10
    shr r12, 24
    mov r13, rax
    shr r13, 16
    xor r12, r13
    mov r13, rax
    shr r13, 24
    xor r12, r13                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = 输入密文 (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    dec r11d
    cmp r11d, dword ptr [rsp+28h]       ; i >= iHalf
    jge xd1_rev
    jmp xd_done
xd_v2:
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    xor r11d, r11d                      ; i=0
xd2_loop:
    imul rax, qword ptr [rsp+0]         ; s1 *= C1
    add rax, qword ptr [rsp+10h]        ; s1 += C3
    mov r12, r10
    shr r12, 7
    xor rax, r12                        ; s1 ^= s2>>7
    imul r10, qword ptr [rsp+18h]       ; s2 *= C4
    add r10, qword ptr [rsp+8]          ; s2 += C2
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shl r12, 16
    xor r10, r12                        ; s2 ^= s1<<16
    rol r10, 17
    mov r12, rax
    shr r12, 16
    xor r12, r10
    mov r13, r10
    shr r13, 8
    xor r12, r13
    xor r12, rax                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = 输入密文 (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    inc r11d
    cmp r11d, dword ptr [rsp+28h]       ; i < iHalf
    jb xd2_loop
    mov rax, qword ptr [rsp+0]          ; C1
    mov r10d, r8d                       ; len
    imul rax, r10                       ; C1*len
    xor rax, qword ptr [rsp+48h]        ; s1 = key ^ C1*len
    mov r10, qword ptr [rsp+48h]        ; key
    xor r10, qword ptr [rsp+8]          ; key ^ C2
    mov r11d, r8d
    xor r10, r11                        ; s2 = key ^ C2 ^ len
    mov qword ptr [rsp+20h], 0          ; uPrev = 0
    mov r11d, r8d
    dec r11d                            ; i = len-1
xd2_rev:
    imul rax, qword ptr [rsp+0]         ; s1 *= C1
    add rax, qword ptr [rsp+10h]        ; s1 += C3
    mov r12, r10
    shr r12, 7
    xor rax, r12                        ; s1 ^= s2>>7
    imul r10, qword ptr [rsp+18h]       ; s2 *= C4
    add r10, qword ptr [rsp+8]          ; s2 += C2
    xor r10, qword ptr [rsp+20h]        ; s2 ^= uPrev
    mov r12, rax
    shl r12, 16
    xor r10, r12                        ; s2 ^= s1<<16
    rol r10, 17
    mov r12, rax
    shr r12, 16
    xor r12, r10
    mov r13, r10
    shr r13, 8
    xor r12, r13
    xor r12, rax                        ; out
    mov dword ptr [rsp+2Ch], r12d       ; stash out
    mov qword ptr [rsp+30h], rax        ; save s1 (inv-perm clobbers rax)
    movzx r13d, byte ptr [rdx+r11]
    mov qword ptr [rsp+20h], r13        ; uPrev = 输入密文 (CFB)
    mov rax, r13
    shr rax, 4
    movzx eax, byte ptr [r14+rax]
    shl rax, 4
    mov r12, r13
    and r12, 0Fh
    movzx r13d, byte ptr [r15+r12]
    or rax, r13
    xor eax, dword ptr [rsp+2Ch]
    mov byte ptr [rcx+r11], al
    mov rax, qword ptr [rsp+30h]        ; restore s1
    dec r11d
    cmp r11d, dword ptr [rsp+28h]       ; i >= iHalf
    jge xd2_rev
    jmp xd_done
xd_done:
    add rsp, 60h
    pop r15
    pop r14
    pop r13
    pop r12
    ret
XorDecrypt ENDP



; single-step slowdown detection via RDTSC
RdtscStart PROC
    push rax
    push rdx
    lfence                              ; serialize: rdtsc ordering stable
    rdtsc
    mov dword ptr [g_rdtsc_start], eax
    lfence
    pop rdx
    pop rax
    ret
RdtscStart ENDP



RdtscCheck PROC
    push rax
    push rdx
    push r10
    lfence
    rdtsc
    sub eax, dword ptr [g_rdtsc_start]
    ; threshold derived from key -> varies per run, fixed-const patch is useless
    mov r10d, dword ptr [g_keyA]
    xor r10d, dword ptr [g_keyB]
    and r10d, 07FFFFFFFh
    or r10d, 0200000h                   ; [2M, 2G) cycles budget
    cmp eax, r10d
    jb rc_ok
    ud2
rc_ok:
    lfence
    pop r10
    pop rdx
    pop rax
    ret
RdtscCheck ENDP



; -> rax = clrjit DllBase or 0 (PEB walk)
FindClrJit PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    mov rax, gs:[60h]
    mov rax, [rax+18h]
    lea rbx, [rax+20h]
    mov rax, [rbx]
fc_loop:
    cmp rax, rbx
    je fc_fail
    mov r13, rax
    mov rsi, [rax+50h]
    mov r10, 006A0072006C0063h          ; "clrj" wide LE
    mov r11, 00740069h                  ; "it" wide LE
    cmp qword ptr [rsi], r10
    jne fc_next
    cmp dword ptr [rsi+8], r11d
    je fc_got
fc_next:
    mov rax, [rax]
    jmp fc_loop
fc_got:
    mov rax, [r13+20h]                  ; DllBase
    jmp fc_ret
fc_fail:
    xor eax, eax
fc_ret:
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
FindClrJit ENDP



; rcx=base rdx=ansiName -> rax=addr or 0
FindExportInModule PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    mov r8, rcx
    mov rdi, rdx
    mov eax, [r8+3Ch]
    lea r9, [r8+rax]
    mov eax, [r9+88h]
    test eax, eax
    jz fe_fail
    add rax, r8
    mov r10, rax
    mov ebx, [r10+18h]
    test ebx, ebx
    jz fe_fail
    mov esi, [r10+20h]
    add rsi, r8
    mov r11d, [r10+24h]
    add r11, r8
    xor r12d, r12d
fe_nml:
    cmp r12d, ebx
    jae fe_fail
    mov eax, [rsi+r12*4]
    lea r9, [r8+rax]
    mov rcx, rdi
fe_cmpl:
    mov al, [rcx]
    test al, al
    jnz fe_c2
    cmp byte ptr [r9], 0
    je fe_mok
    jmp fe_mnx
fe_c2:
    cmp al, [r9]
    jne fe_mnx
    inc rcx
    inc r9
    jmp fe_cmpl
fe_mnx:
    inc r12d
    jmp fe_nml
fe_mok:
    movzx eax, word ptr [r11+r12*2]
    mov ecx, [r10+1Ch]
    add rcx, r8
    mov eax, [rcx+rax*4]
    add rax, r8
    jmp fe_ret
fe_fail:
    xor eax, eax
fe_ret:
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
FindExportInModule ENDP



; !! virtual slot call, so has self: rcx=self rdx=comp r8=info r9d=flags
; nativeEntry/nativeSize untouched on [rsp+28]/[rsp+30]
CompileHook PROC
    push rbp
    mov rbp, rsp
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 60h
    ; single-step (TF) detection
    pushfq
    pop rax
    test rax, 100h
    jz ch_ss_ok
    ud2
ch_ss_ok:
    cmp dword ptr [g_adFlag], 0         ; set by BootAntheil after AD
    jnz ch_ad_ok
    ud2
ch_ad_ok:
    xor r15, r15                        ; scratch=0
    mov rbx, rcx                        ; self
    mov r12, rdx                        ; comp
    mov r13, r8                         ; info
    mov r14d, r9d                       ; flags


; !! hook ICorJitInfo vtable canInline slot (offset per runtime version) on first compile
; JIT inline analysis reads callee IL from raw image (ciphertext)
; -> parses garbage -> broken codegen -> crash
; return INLINE_NEVER(-2) for encrypted callee to block inline IL reads
    mov rax, [r12]                      ; ICorJitInfo vtable
    test rax, rax
    jz gmi_skip
    cmp qword ptr [g_origCanInline], 0
    jne gmi_skip
    mov r10d, [g_ciOff]                 ; canInline slot offset (version-dependent)
    mov rcx, [rax+r10]                  ; vtable[canInlineSlot]
    xor eax, eax
    lock cmpxchg qword ptr [g_origCanInline], rcx
    test rax, rax
    jnz gmi_skip
    mov rax, [g_pVirtualProtect]
    test rax, rax
    jz gmi_skip
    mov rcx, [r12]
    mov r10d, [g_ciOff]
    add rcx, r10
    mov edx, 8
    mov r8d, 4                          ; PAGE_READWRITE
    lea r9, [rbp-30h]
    call rax
    test rax, rax
    jz gmi_skip
    mov rcx, [r12]
    lea rax, GetCanInlineHook
    mov r10d, [g_ciOff]
    mov [rcx+r10], rax                  ; vtable[canInlineSlot]=GetCanInlineHook
    mov rax, [g_pVirtualProtect]
    mov rcx, [r12]
    mov r10d, [g_ciOff]
    add rcx, r10
    mov edx, 8
    mov r8d, [rbp-30h]
    lea r9, [rbp-30h]
    call rax
gmi_skip:
    mov rsi, [r13+10h]                  ; ilCode
    mov edi, dword ptr [r13+18h]        ; ilSize
    test rsi, rsi
    jz ch_orig
    test rdi, rdi
    jz ch_orig
    mov rcx, rsi
    mov rdx, rdi
    call Crc32Fn
    mov ecx, eax                        ; ciphertext CRC (final layer)
    lea rax, g_sigs                     ; sig table
    xor ecx, dword ptr [g_maskA]
    xor ecx, dword ptr [g_maskB]        ; crc2 ^ mask (key-derived)
    xor edx, edx                        ; i=0
ch_sig:
    cmp edx, dword ptr [g_sigCount]
    jae ch_miss
    lea r10, [rdx*8]
    add r10, r10                        ; rdx*16 (128-bit entry stride)
    mov r8d, [rax+r10]                  ; entry lo32 = crc2^mask
    cmp r8d, ecx
    je ch_payload
    inc edx
    jmp ch_sig
ch_miss:
    jmp ch_orig

ch_payload:
    ; entry Hi64 = uKey2^mask64 -> uKey2 (per-method independent random 64-bit key)
    mov r9, [rax+r10+8]                 ; uKey2^mask64 (r10 = rdx*16)
    xor r9, qword ptr [g_maskA]
    xor r9, qword ptr [g_maskB]         ; uKey2 (64-bit)
    mov qword ptr [rsp+48h], r9         ; stash uKey2 (survives VirtualAlloc)
    xor ecx, ecx                        ; VirtualAlloc(NULL, ilSize+0x1000, MEM_RW, PAGE_RW) JIT only reads
    lea edx, [rdi+1000h]                ; ilSize + EH slack
    mov r8d, 3000h
    mov r9d, 4h
    mov rax, [g_pVirtualAlloc]
    test rax, rax
    jz ch_orig
    call rax
    test rax, rax
    jz ch_orig
    mov r15, rax                        ; scratch
    ; ---- acquire g_compLock: g_variant is global, JIT can compile concurrently ----
ch_lock_retry:
    lock bts dword ptr [g_compLock], 0
    jc ch_lock_wait
    jmp ch_lock_got
ch_lock_wait:
    pause
    test dword ptr [g_compLock], 1
    jnz ch_lock_wait
    jmp ch_lock_retry
ch_lock_got:
    ; layer2: scratch = ilCode ^ stream(uKey2) = layer1 ciphertext
    ; variant = (uKey2 ^ (g_keyA^g_keyB)) & 3 ; uKey2 stashed at [rsp+48h]
    mov r10, qword ptr [rsp+48h]        ; uKey2 (64-bit)
    mov rax, qword ptr [g_keyA]
    xor rax, qword ptr [g_keyB]         ; reassemble key (64-bit)
    xor r10, rax                        ; uKey2 ^ key
    and r10d, 3
    mov dword ptr [g_variant], r10d     ; set variant for layer2
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+48h]         ; uKey2 as layer2 key (64-bit)
    call RdtscStart
    call XorDecrypt
    call RdtscCheck
    ; layer1: scratch = layer1 ^ stream(g_keyA^g_keyB) = plaintext
    mov dword ptr [g_variant], 0        ; variant 0 for layer1
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]          ; reassemble key for layer1 (64-bit)
    call XorDecrypt
    ; ---- release g_compLock ----
    lock btr dword ptr [g_compLock], 0
    ; !! fat methods with more-sections (EH table): copy raw-image EH to scratch+ilSize
    ; JIT getEHinfo locates EH by ILCode+ILCodeSize; without this it reads OOB
    mov al, byte ptr [rsi-1]            ; header byte0
    and al, 3
    cmp al, 2                           ; tiny? tiny has no EH
    je ch_set_il
    movzx eax, byte ptr [rsi-12]        ; fat header flag byte
    test al, 8                          ; CorILMethod_MoreSects
    jz ch_set_il
    lea rax, [rsi+rdi]                  ; more-sections start (raw image)
    movzx ecx, byte ptr [rax]           ; kind
    cmp cl, 41h                         ; EH small section?
    je ch_eh_small
    cmp cl, 40h                         ; EH fat section? (0x40,00,00)
    jne ch_set_il
    cmp byte ptr [rax+1], 0             ; fat kind 2nd byte
    jne ch_set_il
    movzx ecx, byte ptr [rax+3]         ; dataSize (4-byte units, fat)
    lea r8d, [rcx*4+4]                  ; total = 4 + dataSize*4
    jmp ch_eh_len
ch_eh_small:
    movzx ecx, byte ptr [rax+1]         ; dataSize (2-byte units, small)
    lea r8d, [rcx*2+4]                  ; total = 4 + dataSize*2
ch_eh_len:
    cmp r8d, 1000h                      ; cap at EH slack
    ja ch_set_il
    lea r9, [r15+rdi]                   ; dst = scratch+ilSize
    xor r10d, r10d
ch_cpyeh:
    cmp r10, r8
    jae ch_set_il
    movzx r11d, byte ptr [rax+r10]
    mov byte ptr [r9+r10], r11b
    inc r10
    jmp ch_cpyeh
ch_set_il:
    mov [r13+10h], r15                  ; !! info->ILCode=scratch, rest filled by CLR, only patch pointer
ch_orig:
    mov rax, [g_orig]                   ; original compileMethod
    test rax, rax
    jz ch_done
    mov rcx, rbx
    mov rdx, r12
    mov r8, r13
    mov r9d, r14d
    add rsp, 60h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    pop rbp
    jmp rax                             ; tail-jump original, args untouched
ch_done:
    xor eax, eax
    add rsp, 60h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    pop rbp
    ret
CompileHook ENDP



; rcx=this rdx=callerHnd r8=calleeHnd -> eax (CorInfoInline)
; JIT inline analysis reads callee IL (raw ciphertext) -> garbage -> broken codegen
; return INLINE_NEVER(-2) for encrypted callee to block inline IL reads
GetCanInlineHook PROC
    push rbp
    mov rbp, rsp
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    sub rsp, 400h
    mov r12, rcx                        ; this
    mov r13, rdx                        ; caller
    mov r14, r8                         ; callee
    mov rax, [g_origCanInline]
    test rax, rax
    jz gci_done
    call rax                            ; orig canInline -> eax

    mov ebx, eax                        ; save original decision
    ; check if callee is encrypted: getMethodInfo (slot4) for IL + CRC
    mov rax, [r12]
    test rax, rax
    jz gci_ret
    mov r10d, [g_gmiOff]                ; getMethodInfo slot offset (version-dependent)
    mov rax, [rax+r10]
    test rax, rax
    jz gci_ret
    lea rsi, [rbp-400h]                 ; CORINFO_METHOD_INFO slot
    xor eax, eax
    mov rdi, rsi
    mov ecx, 80h
    rep stosb
    mov rcx, r12
    mov rdx, r14
    mov r8, rsi
    xor r9d, r9d                        ; context=NULL
    mov rax, [r12]
    mov r10d, [g_gmiOff]
    mov rax, [rax+r10]
    call rax                            ; getMethodInfo -> bool
    test al, al
    jz gci_ret
    mov rsi, [rbp-3F0h]                 ; info.ILCode
    test rsi, rsi
    jz gci_ret
    mov edi, dword ptr [rbp-3E8h]       ; info.ILCodeSize
    test edi, edi
    jz gci_ret
    mov rcx, rsi
    mov rdx, rdi
    call Crc32Fn
    mov ecx, eax
    lea rax, g_sigs
    xor ecx, dword ptr [g_maskA]
    xor ecx, dword ptr [g_maskB]        ; crc ^ mask (key-derived)
    xor edx, edx
gci_sig:
    cmp edx, dword ptr [g_sigCount]
    jae gci_ret
    lea r10, [rdx*8]
    add r10, r10                        ; rdx*16 (128-bit entry stride)
    mov r8d, [rax+r10]                  ; entry lo32 = crc^mask
    cmp r8d, ecx
    je gci_never
    inc edx
    jmp gci_sig
gci_never:
    mov ebx, -2                         ; INLINE_NEVER
gci_ret:
    mov eax, ebx
gci_done:
    add rsp, 400h
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    pop rbp
    ret
GetCanInlineHook ENDP



; PEB find clrjit, getJit, replace vtable slot0
InstallJitHook PROC
    push rbp
    mov rbp, rsp
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 40h

    ; single-step (TF) detection
    pushfq
    pop rax
    test rax, 100h
    jz ih_ss_ok
    ud2

ih_ss_ok:
    lea rcx, szVirtualAlloc
    call ResolveApi
    mov [g_pVirtualAlloc], rax
    lea rcx, szVirtualFree
    call ResolveApi
    mov [g_pVirtualFree], rax
    lea rcx, szVirtualProtect
    call ResolveApi
    mov [g_pVirtualProtect], rax
    test rax, rax
    jz ih_e1

    call FindClrJit
    test rax, rax
    jz ih_e2
    mov rbx, rax                        ; clrjit base

    mov rcx, rbx
    lea rdx, szGetJit
    call FindExportInModule
    test rax, rax
    jz ih_e3
    mov r12, rax                        ; getJit
    call r12
    test rax, rax
    jz ih_e4
    mov r13, rax                        ; ICorJitCompiler*
    mov r14, [r13]                      ; vtable
    mov [g_vtable], r14                 ; save vtable for VerifyJitHook
    test r14, r14
    jz ih_e5
    mov rax, [r14]                      ; original compileMethod
    mov [g_orig], rax

    mov rax, [g_pVirtualProtect]
    mov rcx, r14
    mov edx, 8
    mov r8d, 4                          ; PAGE_READWRITE
    lea r9, [rsp+20h]
    call rax
    test rax, rax
    jz ih_e6
    lea rax, CompileHook
    mov [r14], rax                      ; vtable[0]=CompileHook
    mov rax, [g_pVirtualProtect]
    mov rcx, r14
    mov edx, 8
    mov r8d, [rsp+20h]                  ; restore old protection
    lea r9, [rsp+20h]
    call rax

    xor eax, eax
    jmp ih_ret
ih_e1:
    mov eax, 1
    jmp ih_ret
ih_e2:
    mov eax, 2
    jmp ih_ret
ih_e3:
    mov eax, 3
    jmp ih_ret
ih_e4:
    mov eax, 4
    jmp ih_ret
ih_e5:
    mov eax, 5
    jmp ih_ret
ih_e6:
    mov eax, 6
    jmp ih_ret
ih_ret:
    add rsp, 40h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    pop rbp
    ret
InstallJitHook ENDP



; exports
SetJitHookKey PROC
    ; key split(64): g_keyA = key ^ SPLIT, g_keyB = SPLIT
    mov r8, 0DEADBEEFCAFEBABEh            ; SPLIT
    mov qword ptr [g_keyB], r8
    mov rax, rcx
    xor rax, r8
    mov qword ptr [g_keyA], rax
    ; mask = key ^ (key shr 16) ^ (key shl 13) ^ 0x9E3779B97F4A7C15
    mov rax, rcx
    shr rax, 16
    xor rax, rcx
    mov r9, rcx
    shl r9, 13
    xor rax, r9
    mov r9, 9E3779B97F4A7C15h
    xor rax, r9
    mov qword ptr [g_maskB], r8
    mov r9, rax
    xor r9, r8
    mov qword ptr [g_maskA], r9
    ret
SetJitHookKey ENDP






AddPayloadSig PROC
    mov eax, dword ptr [g_sigCount]
    cmp eax, 4096
    jae sig_full
    lea r8, g_sigs
    lea r9, [rax*8]
    add r9, r9                          ; rax*16 (128-bit entry stride)
    mov [r8+r9], rcx                    ; lo64: lo32=crc2^mask32
    mov [r8+r9+8], rdx                  ; hi64: uKey2^mask64
    inc dword ptr [g_sigCount]
sig_full:
    ret
AddPayloadSig ENDP



; vtable[0] must still be our CompileHook (exact address, not "anything != orig")
VerifyJitHook PROC
    mov rax, [g_vtable]
    test rax, rax
    jz vj_fail
    mov rdx, [rax]                      ; vtable[0]
    lea rcx, CompileHook                ; must still point at our hook
    cmp rdx, rcx
    jne vj_fail
    mov rcx, [g_orig]                   ; original compileMethod
    test rcx, rcx
    jz vj_fail
    cmp rdx, rcx                        ; == original -> restored
    je vj_fail
    xor eax, eax
    ret
vj_fail:
    mov eax, 1
    ret
VerifyJitHook ENDP



; set by BootAntheil once AD() passes
SetAntiDebugFlag PROC
    mov dword ptr [g_adFlag], ecx
    ret
SetAntiDebugFlag ENDP



; set by BootAntheil: rcx = ICorStaticInfo.getMethodInfo slot offset,
; rdx = canInline slot offset (per running coreclr version)
SetJitSlots PROC
    mov dword ptr [g_gmiOff], ecx
    mov dword ptr [g_ciOff], edx
    ret
SetJitSlots ENDP



.data

szVirtualAlloc    db "VirtualAlloc",0
szVirtualFree     db "VirtualFree",0
szVirtualProtect  db "VirtualProtect",0
szClrJit          db "clrjit",0
szGetJit          db "getJit",0

    align 8

g_orig          dq 0                    ; original compileMethod
g_keyA          dq 0                    ; stream cipher key split A (key ^ SPLIT, 64-bit)
g_keyB          dq 0                    ; stream cipher key split B (SPLIT, 64-bit)

g_sigCount      dd 0
g_rdtsc_start   dd 0

    align 8

g_sigs          dq 8192 dup(0)          ; 128bit entries hi=uKey2^mask64 lo=crc2^mask32

g_pVirtualAlloc    dq 0
g_pVirtualFree     dq 0
g_pVirtualProtect  dq 0
g_origGMA          dq 0                 ; original ICorJitInfo::getMethodAttribs (slot1)
g_origCanInline    dq 0                 ; original ICorJitInfo::canInline
g_gmiOff           dd 20h               ; ICorStaticInfo.getMethodInfo slot offset (net8 default)
g_ciOff            dd 30h               ; ICorStaticInfo.canInline slot offset (net8 default)
g_compLock         dd 0                 ; compile serialization lock
                   db 0,0,0             ; align
                   db 0,0,0             ; align

    align 8

g_vtable           dq 0                 ; vtable for VerifyJitHook
g_adFlag           dd 0                 ; set by BootAntheil after AD passes

g_maskA            dq 0                 ; key-derived 64-bit sig mask split A (mask ^ SPLIT)
g_maskB            dq 0                 ; key-derived 64-bit sig mask split B (SPLIT)
g_variant          dd 0                 ; per-method decrypt variant (CompileHook)
g_permKey          dq 0                 ; last perm-gen key
g_permHi           db 16 dup(0)         ; 4-bit high-nibble perm
g_permLo           db 16 dup(0)         ; 4-bit low-nibble perm
g_invHi            db 16 dup(0)         ; inverse perm high
g_invLo            db 16 dup(0)         ; inverse perm low

END