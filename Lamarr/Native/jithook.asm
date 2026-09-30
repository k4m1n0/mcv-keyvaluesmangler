.code

PUBLIC g_orig, g_keyA, g_keyB, g_pSigs, g_sigCount
; boot arms the hook only after the two setters are bound, so they stay exported
; JitHookTable hands over the other five entry points, they stay internal
PUBLIC JitHookTable, InstallJitHook

; sig table entry count comes from the boot handover, this is a sanity guard
SIGMAX          equ 100000h

; decrypt cache, linear probe table reused across recompiles, one VirtualAlloc
CACHECAP        equ 2048
CACHEPROBE      equ 8

; decrypted scratch pool, in flight slots hold the buffers handed to the JIT
; a buffer is reclaimed and wiped once its compile has unwound, a nested compile sits lower on the stack
POOLN           equ 16
POOL_SLOT       equ 56
POOL_OFF_RBP    equ POOLN*POOL_SLOT
POOL_SIZE       equ POOL_OFF_RBP+32*32+64
; in flight slot, +0 ilCode, +8 size, +12 crc, +16 scratch, +24 cap
; +28 tid, +32 state, +40 rbp, +48 gen
POOL_ST_IL      equ 0
POOL_ST_SIZE    equ 8
POOL_ST_CRC     equ 12
POOL_ST_SCR     equ 16
POOL_ST_CAP     equ 24
POOL_ST_TID     equ 28
POOL_ST_STATE   equ 32
POOL_ST_RBP     equ 40
POOL_ST_GEN     equ 48
POOL_ST_IDX     equ 52

; a handed out buffer is also keyed by method handle, so a recompile that hands the patched ILCode back is answered from the image
MAPN            equ 256
MAP_SLOT        equ 40
MAP_ST_FTN      equ 0
MAP_ST_SCR      equ 8
MAP_ST_IMG      equ 16
MAP_ST_SIZE     equ 24
MAP_ST_IDX      equ 28
MAP_SIZE        equ MAPN*MAP_SLOT+64

; ICorJitInfo proxy, getMethodInfo and canInline are intercepted, the JIT sees our object
; 256 slots cover every runtime seen, 191 194 202
SHIMSLOTS       equ 256
SHIMTBLSZ       equ SHIMSLOTS*8         ;bytes of one table, ours and the runtime copy next to it

; cheap "looks like IL" test for the variant retry, a real body ends with a terminator, a wrong variant gives uniform bytes
; rcx = body, rdx = len -> eax = 1; touches rax/rcx/rdx/r8/r9, never r12/r14
IlPlausible PROC
    xor eax, eax
    cmp rdx, 2
    jb ip_out
    movzx r8d, byte ptr [rcx+rdx-1]
    cmp r8b, 2Ah                        ; ret
    je ip_yes
    cmp r8b, 7Ah                        ; throw
    je ip_yes
    cmp r8b, 0DCh                       ; endfinally
    je ip_yes
    cmp r8b, 0DDh                       ; leave
    je ip_yes
    cmp r8b, 0DEh                       ; leave.s
    je ip_yes
    jmp ip_out
ip_yes:
    mov eax, 1
ip_out:
    ret
IlPlausible ENDP

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
jit_slot_0001::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
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
jit_slot_0002::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    mov r10, 004E00520045004Bh          ; "KERN"
    mov r11, 00320033004C0045h          ; "EL32"
    cmp r8, r10
    jne ra_nxt
    cmp r9, r11
    je ra_got
ra_nxt:
jit_slot_0003::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
    mov rax, [rax]
    jmp ra_kl
ra_got:
jit_slot_0004::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 44h, 00h, 00h
    mov rdx, [rax+20h]                  ; DllBase (kernel32)
    mov rdi, r13
ra_exp:
jit_slot_0005::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
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
jit_slot_0006::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 66h, 90h
    cmp r10d, ebx
    jae ra_fail
    mov eax, [rsi+r10*4]
    lea r9, [rdx+rax]
    mov rcx, rdi
ra_cmpl:
jit_slot_0007::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    mov al, [rcx]
    test al, al
    jnz ra_c2
    cmp byte ptr [r9], 0
    je ra_mok
    jmp ra_mnx
ra_c2:
jit_slot_0008::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 40h, 00h
    cmp al, [r9]
    jne ra_mnx
    inc rcx
    inc r9
    jmp ra_cmpl
ra_mnx:
jit_slot_0009::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
    inc r10d
    jmp ra_nml
ra_mok:
jit_slot_0010::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 40h, 00h, 90h
    movzx eax, word ptr [r11+r10*2]
    mov ecx, [r8+1Ch]
    add rcx, rdx
    mov eax, [rcx+rax*4]
    ; check for a forwarded export
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
jit_slot_0011::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 66h, 90h
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
jit_slot_0012::
    db 0Fh, 1Fh, 00h, 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp rax, rbx
    je ra_fail
    mov r8, [rax+50h]
    xor r9d, r9d
fw_cmp:
jit_slot_0013::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 90h
    cmp r9, r12
    jae fw_got
    movzx r10d, byte ptr [rdi+r9]
    movzx r11d, word ptr [r8+r9*2]
    cmp r10d, 61h
    jb fw_u1
    sub r10d, 20h
fw_u1:
jit_slot_0014::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    cmp r11d, 61h
    jb fw_u2
    sub r11d, 20h
fw_u2:
jit_slot_0015::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
    cmp r10d, r11d
    jne fw_nxt
    inc r9
    jmp fw_cmp
fw_nxt:
jit_slot_0016::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 66h, 90h
    mov rax, [rax]
    jmp fw_loop
fw_got:
jit_slot_0017::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 90h
    mov rdx, [rax+20h]
    mov rdi, rsi
    jmp ra_exp
rfok:
jit_slot_0018::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    add rax, rdx
    jmp ra_ret
ra_fail:
jit_slot_0019::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    xor eax, eax
ra_ret:
jit_slot_0020::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h
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
jit_slot_0021::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 90h
    movzx r9d, byte ptr [rcx+r8]
    xor eax, r9d
    mov r10d, 8
crc_bit:
jit_slot_0022::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
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
jit_slot_0023::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    not eax
    ret
Crc32Fn ENDP



; rcx=dst rdx=src r8d=len r9d=key
; !! length preserving dual state nonlinear stream
; s1 = key ^ len*C1; s2 = key ^ C2 ^ len; s1 = s1*0x9E3779B1+0x9747B28C
; s2 = (s2*0x85EBCA6B+0xC2B2AE35) ^ (s1>>8) ^ (s1<<16); s2 = rol(s2,13); out = (s1>>24)^(s2>>16)^(s2>>24)
GenPerm PROC
    push rsi
    push rbx
    cmp qword ptr [g_permKey], r9
    je gp_done
    lea r8, g_permHi
    ;%poly:opseq
    xor eax, eax
    ;%poly:alt
    sub eax, eax
    ;%poly:end
gp_hinit:
jit_slot_0024::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    mov byte ptr [r8+rax], al
    inc eax
    cmp eax, 16
    jb gp_hinit
    mov r9, qword ptr [rsp+60h]         ; key (XorDecrypt frame via push/ret)
    mov r10, 9E3779B97F4A7C15h
    xor r9, r10
    mov esi, 15
gp_hloop:
jit_slot_0025::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
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
    ;%poly:opseq
    xor eax, eax
    ;%poly:alt
    sub eax, eax
    ;%poly:end
gp_linit:
jit_slot_0026::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
    mov byte ptr [r8+rax], al
    inc eax
    cmp eax, 16
    jb gp_linit
    mov r9, qword ptr [rsp+60h]
    mov r10, 85EBCA6B85EBCA6Bh
    xor r9, r10
    mov esi, 15
gp_lloop:
jit_slot_0027::
    db 0Fh, 1Fh, 40h, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
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
    ;%poly:opseq
    xor ecx, ecx
    ;%poly:alt
    sub ecx, ecx
    ;%poly:end
gp_invh:
jit_slot_0028::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    movzx eax, byte ptr [r10+rcx]
    mov byte ptr [r11+rax], cl
    inc ecx
    cmp ecx, 16
    jb gp_invh
    lea r10, g_permLo
    lea r11, g_invLo
    ;%poly:opseq
    xor ecx, ecx
    ;%poly:alt
    sub ecx, ecx
    ;%poly:end
gp_invl:
jit_slot_0029::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    movzx eax, byte ptr [r10+rcx]
    mov byte ptr [r11+rax], cl
    inc ecx
    cmp ecx, 16
    jb gp_invl
    mov r9, qword ptr [rsp+60h]
    mov qword ptr [g_permKey], r9
gp_done:
jit_slot_0030::
    db 0Fh, 1Fh, 44h, 00h, 00h, 90h
    pop rbx
    pop rsi
    ret
GenPerm ENDP



; ChaCha20 quarter round over 4 dwords at [r13+oa..od]
CHQ MACRO oa, ob, oc, od
    mov eax, [r13+oa]
    add eax, [r13+ob]
    mov [r13+oa], eax
    mov edx, [r13+od]
    xor edx, eax
    rol edx, 16
    mov [r13+od], edx
    mov eax, [r13+oc]
    add eax, edx
    mov [r13+oc], eax
    mov edx, [r13+ob]
    xor edx, eax
    rol edx, 12
    mov [r13+ob], edx
    mov eax, [r13+oa]
    add eax, [r13+ob]
    mov [r13+oa], eax
    mov edx, [r13+od]
    xor edx, eax
    rol edx, 8
    mov [r13+od], edx
    mov eax, [r13+oc]
    add eax, edx
    mov [r13+oc], eax
    mov edx, [r13+ob]
    xor edx, eax
    rol edx, 7
    mov [r13+ob], edx
ENDM


; rcx=dst rdx=src r8d=len r9=key r10d=layer(0=layer1 1=layer2)
; ChaCha20 keystream + nibble inverse perm + CFB, mixed layer
XorDecrypt PROC
    push r12
    push r13
    push r14
    push r15
    sub rsp, 120h
    test r8d, r8d
    jz xd_done
    lea r14, g_invHi
    lea r15, g_invLo
    mov qword ptr [rsp+48h], r9         ; key (GenPerm reads [rsp+48h])
    mov dword ptr [rsp+38h], r8d        ; len
    mov qword ptr [rsp+40h], rcx        ; dst
    mov qword ptr [rsp+50h], rdx        ; src
    mov dword ptr [rsp+58h], r10d       ; layer
    call GenPerm
    mov dword ptr [rsp+0A0h], 61707865h
    mov dword ptr [rsp+0A4h], 3320646Eh
    mov dword ptr [rsp+0A8h], 79622D32h
    mov dword ptr [rsp+0ACh], 6B206574h
    ;%poly:opseq
    xor eax, eax
    ;%poly:alt
    sub eax, eax
    ;%poly:end
    mov dword ptr [rsp+0B0h], eax
    mov dword ptr [rsp+0B4h], eax
    mov dword ptr [rsp+0B8h], eax
    mov dword ptr [rsp+0BCh], eax
    mov dword ptr [rsp+0C0h], eax
    mov dword ptr [rsp+0C4h], eax
    mov dword ptr [rsp+0C8h], eax
    mov dword ptr [rsp+0CCh], eax
    mov dword ptr [rsp+0D0h], eax       ; counter = 0
    mov eax, dword ptr [rsp+48h]        ; key lo
    mov dword ptr [rsp+0D4h], eax
    mov eax, dword ptr [rsp+4Ch]        ; key hi
    mov dword ptr [rsp+0D8h], eax
    mov dword ptr [rsp+0DCh], 017A9C53h
    lea r12, [rsp+0A0h]
    lea r13, [rsp+0E0h]
    mov eax, [r12+0]
mov [r13+0], eax
    mov eax, [r12+4]
mov [r13+4], eax
    mov eax, [r12+8]
mov [r13+8], eax
    mov eax, [r12+0Ch]
mov [r13+0Ch], eax
    mov eax, [r12+10h]
mov [r13+10h], eax
    mov eax, [r12+14h]
mov [r13+14h], eax
    mov eax, [r12+18h]
mov [r13+18h], eax
    mov eax, [r12+1Ch]
mov [r13+1Ch], eax
    mov eax, [r12+20h]
mov [r13+20h], eax
    mov eax, [r12+24h]
mov [r13+24h], eax
    mov eax, [r12+28h]
mov [r13+28h], eax
    mov eax, [r12+2Ch]
mov [r13+2Ch], eax
    mov eax, [r12+30h]
mov [r13+30h], eax
    mov eax, [r12+34h]
mov [r13+34h], eax
    mov eax, [r12+38h]
mov [r13+38h], eax
    mov eax, [r12+3Ch]
mov [r13+3Ch], eax
    mov r8d, 10
kdf_round:
jit_slot_0031::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 40h, 00h
    CHQ 0,10h,20h,30h
    CHQ 4,14h,24h,34h
    CHQ 8,18h,28h,38h
    CHQ 0Ch,1Ch,2Ch,3Ch
    CHQ 0,14h,28h,3Ch
    CHQ 4,18h,2Ch,30h
    CHQ 8,1Ch,20h,34h
    CHQ 0Ch,10h,24h,38h
    dec r8d
    jnz kdf_round
    mov eax, [r13+0]
    add eax, [r12+0]
mov [rsp+0], eax
    mov eax, [r13+4]
    add eax, [r12+4]
mov [rsp+4], eax
    mov eax, [r13+8]
    add eax, [r12+8]
mov [rsp+8], eax
    mov eax, [r13+0Ch]
    add eax, [r12+0Ch]
mov [rsp+0Ch], eax
    mov eax, [r13+10h]
    add eax, [r12+10h]
mov [rsp+10h], eax
    mov eax, [r13+14h]
    add eax, [r12+14h]
mov [rsp+14h], eax
    mov eax, [r13+18h]
    add eax, [r12+18h]
mov [rsp+18h], eax
    mov eax, [r13+1Ch]
    add eax, [r12+1Ch]
mov [rsp+1Ch], eax
ks_ready:
jit_slot_0032::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    mov eax, dword ptr [rsp+48h]        ; key lo
    mov dword ptr [rsp+20h], eax        ; nonce[0]
    cmp dword ptr [rsp+58h], 1
    jne ks_l1
    mov eax, dword ptr [rsp+4Ch]        ; key hi
    mov dword ptr [rsp+24h], eax        ; nonce[1]
    mov eax, dword ptr [g_variant]
    and eax, 3
    xor eax, 2B7E1516h
    mov dword ptr [rsp+28h], eax        ; nonce[2]
    jmp ks_n
ks_l1:
jit_slot_0033::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    mov dword ptr [rsp+24h], 0
    mov dword ptr [rsp+28h], 0C2B2AE35h
ks_n:
jit_slot_0034::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    mov dword ptr [rsp+2Ch], 0          ; counter = 0
    mov qword ptr [rsp+30h], 0          ; uPrev = 0
    xor r11d, r11d                      ; i = 0
    lea rcx, [rsp+0]
    lea rdx, [rsp+20h]
    mov r8d, dword ptr [rsp+2Ch]
    lea r9, [rsp+60h]
    call ChachaGen
    jmp xd_loop

xd_loop:
jit_slot_0035::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    cmp r11d, dword ptr [rsp+38h]
    jae xd_done
    mov eax, r11d
    and eax, 3Fh
    movzx eax, byte ptr [rsp+60h+rax]
    mov rdx, qword ptr [rsp+30h]
    xor eax, edx                        ; K = ks ^ uPrev
    mov rcx, qword ptr [rsp+50h]
    movzx r9d, byte ptr [rcx+r11]       ; c
    mov rdx, r9
    shr rdx, 4
    movzx edx, byte ptr [r14+rdx]
    shl edx, 4
    mov rcx, r9
    and rcx, 0Fh
    movzx ecx, byte ptr [r15+rcx]
    or edx, ecx                         ; PInv(c)
    xor eax, edx                        ; m
    mov rcx, qword ptr [rsp+40h]
    mov byte ptr [rcx+r11], al
    mov qword ptr [rsp+30h], r9         ; uPrev = c
    inc r11d
    test r11d, 3Fh
    jnz xd_loop
    inc dword ptr [rsp+2Ch]
    lea rcx, [rsp+0]
    lea rdx, [rsp+20h]
    mov r8d, dword ptr [rsp+2Ch]
    lea r9, [rsp+60h]
    call ChachaGen
    jmp xd_loop
xd_done:
jit_slot_0036::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 66h, 0Fh, 1Fh, 44h, 00h, 00h
    add rsp, 120h
    pop r15
    pop r14
    pop r13
    pop r12
    ret
XorDecrypt ENDP

; rcx=key(32B) rdx=nonce(12B) r8d=counter r9=out(64B) -> ChaCha20 block
ChachaGen PROC
    push r12
    push r13
    sub rsp, 0A0h
    mov dword ptr [rsp+0], 61707865h
    mov dword ptr [rsp+4], 3320646Eh
    mov dword ptr [rsp+8], 79622D32h
    mov dword ptr [rsp+0Ch], 6B206574h
    mov eax, [rcx+0]
    mov [rsp+10h], eax
    mov eax, [rcx+4]
    mov [rsp+14h], eax
    mov eax, [rcx+8]
    mov [rsp+18h], eax
    mov eax, [rcx+0Ch]
    mov [rsp+1Ch], eax
    mov eax, [rcx+10h]
    mov [rsp+20h], eax
    mov eax, [rcx+14h]
    mov [rsp+24h], eax
    mov eax, [rcx+18h]
    mov [rsp+28h], eax
    mov eax, [rcx+1Ch]
    mov [rsp+2Ch], eax
    mov [rsp+30h], r8d
    mov eax, [rdx+0]
    mov [rsp+34h], eax
    mov eax, [rdx+4]
    mov [rsp+38h], eax
    mov eax, [rdx+8]
    mov [rsp+3Ch], eax
    lea r12, [rsp+0]
    lea r13, [rsp+40h]
    mov eax, [r12+0]
    mov [r13+0], eax
    mov eax, [r12+4]
    mov [r13+4], eax
    mov eax, [r12+8]
    mov [r13+8], eax
    mov eax, [r12+0Ch]
    mov [r13+0Ch], eax
    mov eax, [r12+10h]
    mov [r13+10h], eax
    mov eax, [r12+14h]
    mov [r13+14h], eax
    mov eax, [r12+18h]
    mov [r13+18h], eax
    mov eax, [r12+1Ch]
    mov [r13+1Ch], eax
    mov eax, [r12+20h]
    mov [r13+20h], eax
    mov eax, [r12+24h]
    mov [r13+24h], eax
    mov eax, [r12+28h]
    mov [r13+28h], eax
    mov eax, [r12+2Ch]
    mov [r13+2Ch], eax
    mov eax, [r12+30h]
    mov [r13+30h], eax
    mov eax, [r12+34h]
    mov [r13+34h], eax
    mov eax, [r12+38h]
    mov [r13+38h], eax
    mov eax, [r12+3Ch]
    mov [r13+3Ch], eax
    mov r8d, 10
tb_r:
jit_slot_0037::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
    CHQ 0,10h,20h,30h
    CHQ 4,14h,24h,34h
    CHQ 8,18h,28h,38h
    CHQ 0Ch,1Ch,2Ch,3Ch
    CHQ 0,14h,28h,3Ch
    CHQ 4,18h,2Ch,30h
    CHQ 8,1Ch,20h,34h
    CHQ 0Ch,10h,24h,38h
    dec r8d
    jnz tb_r
    mov eax, [r13+0]
    add eax, [r12+0]
mov [r9+0], eax
    mov eax, [r13+4]
    add eax, [r12+4]
mov [r9+4], eax
    mov eax, [r13+8]
    add eax, [r12+8]
mov [r9+8], eax
    mov eax, [r13+0Ch]
    add eax, [r12+0Ch]
mov [r9+0Ch], eax
    mov eax, [r13+10h]
    add eax, [r12+10h]
mov [r9+10h], eax
    mov eax, [r13+14h]
    add eax, [r12+14h]
mov [r9+14h], eax
    mov eax, [r13+18h]
    add eax, [r12+18h]
mov [r9+18h], eax
    mov eax, [r13+1Ch]
    add eax, [r12+1Ch]
mov [r9+1Ch], eax
    mov eax, [r13+20h]
    add eax, [r12+20h]
mov [r9+20h], eax
    mov eax, [r13+24h]
    add eax, [r12+24h]
mov [r9+24h], eax
    mov eax, [r13+28h]
    add eax, [r12+28h]
mov [r9+28h], eax
    mov eax, [r13+2Ch]
    add eax, [r12+2Ch]
mov [r9+2Ch], eax
    mov eax, [r13+30h]
    add eax, [r12+30h]
mov [r9+30h], eax
    mov eax, [r13+34h]
    add eax, [r12+34h]
mov [r9+34h], eax
    mov eax, [r13+38h]
    add eax, [r12+38h]
mov [r9+38h], eax
    mov eax, [r13+3Ch]
    add eax, [r12+3Ch]
mov [r9+3Ch], eax
    add rsp, 0A0h
    pop r13
    pop r12
    ret
ChachaGen ENDP



; single step slowdown detection via RDTSC
RdtscStart PROC
    push rax
    push rdx
    lfence                              ; serialize, rdtsc ordering stable
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
    ; threshold derived from key, varies per run, a fixed const patch is useless
    mov r10d, dword ptr [g_keyA]
    xor r10d, dword ptr [g_keyB]
    and r10d, 07FFFFFFFh
    or r10d, 0200000h                   ; [2M, 2G) cycles budget
    cmp eax, r10d
    jb rc_ok
    ud2
rc_ok:
jit_slot_0038::
    db 0Fh, 1Fh, 44h, 00h, 00h, 90h
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
jit_slot_0039::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
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
jit_slot_0040::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 40h, 00h
    mov rax, [rax]
    jmp fc_loop
fc_got:
jit_slot_0041::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    mov rax, [r13+20h]                  ; DllBase
    jmp fc_ret
fc_fail:
jit_slot_0042::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 90h
    xor eax, eax
fc_ret:
jit_slot_0043::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
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
jit_slot_0044::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h, 66h, 90h
    cmp r12d, ebx
    jae fe_fail
    mov eax, [rsi+r12*4]
    lea r9, [r8+rax]
    mov rcx, rdi
fe_cmpl:
jit_slot_0045::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    mov al, [rcx]
    test al, al
    jnz fe_c2
    cmp byte ptr [r9], 0
    je fe_mok
    jmp fe_mnx
fe_c2:
jit_slot_0046::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 90h
    cmp al, [r9]
    jne fe_mnx
    inc rcx
    inc r9
    jmp fe_cmpl
fe_mnx:
jit_slot_0047::
    db 0Fh, 1Fh, 40h, 00h, 0Fh, 1Fh, 00h, 90h
    inc r12d
    jmp fe_nml
fe_mok:
jit_slot_0048::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 90h
    movzx eax, word ptr [r11+r12*2]
    mov ecx, [r10+1Ch]
    add rcx, r8
    mov eax, [rcx+rax*4]
    add rax, r8
    jmp fe_ret
fe_fail:
jit_slot_0049::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    xor eax, eax
fe_ret:
jit_slot_0050::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
FindExportInModule ENDP



; !! virtual slot call, so has self, rcx=self rdx=comp r8=info r9d=flags
; nativeEntry/nativeSize untouched on [rsp+28]/[rsp+30]

; rcx = buffer, edx = length; byte loop so no other register is touched
ZeroBuf PROC
    test rcx, rcx
    jz zb_done
    test edx, edx
    jz zb_done
    mov r10d, edx
    xor eax, eax
    xor r11d, r11d
zb_loop:
    mov byte ptr [rcx+r11], al
    inc r11d
    cmp r11d, r10d
    jb zb_loop
zb_done:
    ret
ZeroBuf ENDP

; rcx = ftn, rdx = scratch handed to the JIT, r8d = request ILCodeSize -> rax = image ILCode (0 when unknown), edx = sig index
; r8d = ILCodeSize, r9d = crc^mask, r11d = 0 handle match, 1 buffer match
MapFind PROC
    push rbx                            ; r8d carries the request size in, so it gets a register of its own
    mov ebx, r8d
    mov r10, qword ptr [g_pMap]
    test r10, r10
    jz mf_none
    mov r11, rcx
    shr r11, 4
    mov r9d, 9E3779B1h                  ; imul takes a signed imm32, so the constant goes through a register
    imul r11d, r9d
    shr r11d, 24
    and r11d, MAPN-1
    mov eax, MAPN
mf_loop:
    dec eax
    jz mf_scan
    mov r9, r11
    imul r9, r9, MAP_SLOT
    add r9, r10
    mov r8, qword ptr [r9+MAP_ST_FTN]
    test r8, r8
    jz mf_none                          ; empty slot, this method never had a buffer
    cmp r8, rcx
    je mf_match
    inc r11d
    and r11d, MAPN-1
    jmp mf_loop
mf_match:
    cmp qword ptr [r9+MAP_ST_SCR], rdx
    jne mf_scan                         ; same method, other pointer, try the buffer itself below
    xor r11d, r11d                      ; matched through the method handle
    jmp mf_crc
mf_scan:
    ; the runtime may hand the patched pointer back under another handle, so
    ; the buffer is looked up too, one generic body serves many MethodDescs
    xor r11d, r11d
mf_scan_loop:
    cmp r11d, MAPN
    jae mf_none
    mov r9, r11
    imul r9, r9, MAP_SLOT
    add r9, r10
    cmp qword ptr [r9+MAP_ST_SCR], rdx
    jne mf_scan_next
    cmp dword ptr [r9+MAP_ST_SIZE], ebx
    je mf_scan_hit
mf_scan_next:
    inc r11d
    jmp mf_scan_loop
mf_scan_hit:
    mov r11d, 1                         ; matched through the buffer, not through the method handle
mf_crc:
    mov rax, qword ptr [r9+MAP_ST_IMG]
    test rax, rax
    jz mf_none
    mov r8d, dword ptr [r9+MAP_ST_SIZE]
    mov edx, dword ptr [r9+MAP_ST_IDX]
    mov r10, qword ptr [g_pSigs]
    test r10, r10
    jz mf_none
    mov r9d, dword ptr [r10+rdx*4]      ; the key is recomputed from the signature table entry
    pop rbx
    ret
mf_none:
    xor eax, eax
    pop rbx
    ret
MapFind ENDP

; rcx = ftn, rdx = scratch, r8 = ILCode, r9d = ILCodeSize, r10d = sig index
; args are read back from the stack after the slot pointer took r9
MapPut PROC
    push rcx
    push rdx
    push r8
    push r9                             ; [rsp+8]  = ILCodeSize
    push r10                            ; [rsp+0]  = signature table index
    mov rax, qword ptr [g_pMap]
    test rax, rax
    jnz mp_have
    xor ecx, ecx
    mov edx, MAP_SIZE
    mov r8d, 3000h
    mov r9d, 4h
    mov rax, qword ptr [g_pVirtualAlloc]
    test rax, rax
    jz mp_out
    call rax
    test rax, rax
    jz mp_out
    mov qword ptr [g_pMap], rax
mp_have:
    mov r10, qword ptr [g_pMap]
    mov r11, qword ptr [rsp+32]         ; ftn
    shr r11, 4
    mov r9d, 9E3779B1h
    imul r11d, r9d
    shr r11d, 24
    and r11d, MAPN-1
    mov eax, MAPN
mp_loop:
    dec eax
    jz mp_out
    mov r9, r11
    imul r9, r9, MAP_SLOT
    add r9, r10
    mov r8, qword ptr [r9+MAP_ST_FTN]    ; r8 is the pushed argument slot, so it is free as a scratch here
    test r8, r8
    jz mp_put                            ; empty slot
    cmp r8, qword ptr [rsp+32]
    je mp_put                            ; same method, refresh the entry
    inc r11d
    and r11d, MAPN-1
    jmp mp_loop
mp_put:
    mov rax, qword ptr [rsp+32]
    mov qword ptr [r9+MAP_ST_FTN], rax
    mov rax, qword ptr [rsp+24]
    mov qword ptr [r9+MAP_ST_SCR], rax
    mov rax, qword ptr [rsp+16]
    mov qword ptr [r9+MAP_ST_IMG], rax
    mov eax, dword ptr [rsp+8]
    mov dword ptr [r9+MAP_ST_SIZE], eax
    mov eax, dword ptr [rsp+0]
    mov dword ptr [r9+MAP_ST_IDX], eax
mp_out:
    pop r10
    pop r9
    pop r8
    pop rdx
    pop rcx
    ret
MapPut ENDP
; hooks inside the JIT see no frame, so the slot records its owner
; and the compile that owns it has unwound or is still live
PoolSetCur PROC
    mov r10, qword ptr [g_pPool]
    test r10, r10
    jz psc_done
    mov r11, qword ptr gs:[48h]
    mov rax, r11
    imul eax, eax, 6D2B79F5h
    shr eax, 27
    and eax, 31
    shl rax, 5
    add r10, POOL_OFF_RBP
    add r10, rax
    mov qword ptr [r10], r11
    mov qword ptr [r10+8], rcx
    inc dword ptr [r10+16]              ; every compile gets its own generation on this thread
psc_done:
    ret
PoolSetCur ENDP

; -> rax = frame of this thread's compile (0 when none), edx = its generation
PoolGetCur PROC
    xor edx, edx
    mov r10, qword ptr [g_pPool]
    test r10, r10
    jz pgc_none
    mov r11, qword ptr gs:[48h]
    mov rax, r11
    imul eax, eax, 6D2B79F5h
    shr eax, 27
    and eax, 31
    shl rax, 5
    add r10, POOL_OFF_RBP
    add r10, rax
    cmp qword ptr [r10], r11
    jne pgc_none
    mov edx, dword ptr [r10+16]
    mov rax, qword ptr [r10+8]
    ret
pgc_none:
    xor eax, eax
    ret
PoolGetCur ENDP

; rcx = crc^mask, rdx = ilCode, r8d = ilSize -> rax = scratch (0 on failure), edx = 1 when it holds this body's plaintext
; a slot whose compile has unwound is wiped then reused or released, so no plaintext body outlives its compile
PoolAcquire PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 40h
    mov qword ptr [rsp+30h], 0          ; recycled buffer picked for this request (0 = none yet)
    mov qword ptr [rsp+10h], r9         ; ftn, the handed out buffer is registered against its method
    mov dword ptr [rsp+18h], r11d       ; signature table index of that body
    mov r12d, ecx                       ; crc^mask
    mov r13, rdx                        ; ilCode
    mov r14d, r8d                       ; ilSize
    mov rax, qword ptr [g_pPool]
    test rax, rax
    jnz pa_have
    xor ecx, ecx
    mov edx, POOL_SIZE
    mov r8d, 3000h
    mov r9d, 4h
    mov rax, qword ptr [g_pVirtualAlloc]
    test rax, rax
    jz pa_fail
    call rax
    test rax, rax
    jz pa_fail
    mov qword ptr [g_pPool], rax
pa_have:
    mov rbx, qword ptr [g_pPool]
    call PoolGetCur
    mov qword ptr [rsp+20h], rax        ; frame of the compile asking
    mov dword ptr [rsp+2Ch], edx        ; its generation, in its own slot, it must not touch the frame pointer
    mov r15, qword ptr gs:[48h]         ; my thread id
    ; body already decrypted by this compile, slot holds plaintext and EH
    xor edi, edi
pa_scan_hit:
jit_slot_0051::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    cmp edi, POOLN
    jae pa_scan_victim
    mov rsi, rdi
    imul rsi, rsi, POOL_SLOT
    add rsi, rbx
    cmp dword ptr [rsi+POOL_ST_TID], r15d
    jne pa_hit_next
    mov eax, dword ptr [rsp+2Ch]
    cmp dword ptr [rsi+POOL_ST_GEN], eax
    jne pa_hit_next
    cmp qword ptr [rsi+POOL_ST_IL], r13
    jne pa_hit_next
    cmp dword ptr [rsi+POOL_ST_SIZE], r14d
    jne pa_hit_next
    cmp dword ptr [rsi+POOL_ST_CRC], r12d
    jne pa_hit_next
    cmp dword ptr [rsi+POOL_ST_STATE], 1
    jne pa_hit_next
    mov rax, qword ptr [rsi+POOL_ST_SCR]
    mov r10, rsi
    mov edx, 1
    jmp pa_done
pa_hit_next:
    inc edi
    jmp pa_scan_hit
    ; wipe and release every scratch of mine whose compile has finished
    ; its generation and frame address make that safe
pa_scan_victim:
    xor edi, edi
pa_v_loop:
jit_slot_0052::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp edi, POOLN
    jae pa_pick
    mov rsi, rdi
    imul rsi, rsi, POOL_SLOT
    add rsi, rbx
    cmp dword ptr [rsi+POOL_ST_TID], r15d
    jne pa_v_next
    mov eax, dword ptr [rsp+2Ch]
    cmp dword ptr [rsi+POOL_ST_GEN], eax
    je pa_v_next                        ; this compile still owns it, the JIT may be reading it right now
    mov rax, qword ptr [rsp+20h]
    test rax, rax
    jz pa_v_next                        ; no frame recorded, nothing is provably unwound
    cmp qword ptr [rsi+POOL_ST_RBP], rax
    ja pa_v_next                        ; owner sits at a younger frame, that compile may still be in flight
    mov rcx, qword ptr [rsi+POOL_ST_SCR]
    mov edx, dword ptr [rsi+POOL_ST_CAP]
    call ZeroBuf                        ; plaintext leaves the buffer before anything else touches it
    mov eax, dword ptr [rsi+POOL_ST_CAP]
    cmp eax, r14d
    jb pa_v_release
    cmp qword ptr [rsp+30h], 0
    jne pa_v_release                    ; keep one recycled buffer, release the surplus
    mov qword ptr [rsp+30h], rsi        ; reuse candidate, wiped and large enough
    jmp pa_v_next
pa_v_release:
    mov rcx, qword ptr [rsi+POOL_ST_SCR]
    mov rax, qword ptr [g_pVirtualFree]
    test rax, rax
    jz pa_v_drop
    test rcx, rcx
    jz pa_v_drop
    xor edx, edx
    mov r8d, 8000h                      ; MEM_RELEASE
    call rax
pa_v_drop:
    mov qword ptr [rsi+POOL_ST_SCR], 0
    mov dword ptr [rsi+POOL_ST_CAP], 0
    mov qword ptr [rsi+POOL_ST_IL], 0
    mov dword ptr [rsi+POOL_ST_SIZE], 0
    mov dword ptr [rsi+POOL_ST_CRC], 0
    mov dword ptr [rsi+POOL_ST_STATE], 0
    mov dword ptr [rsi+POOL_ST_GEN], 0
    mov qword ptr [rsi+POOL_ST_RBP], 0
    mov dword ptr [rsi+POOL_ST_TID], 0
pa_v_next:
    inc edi
    jmp pa_v_loop
pa_pick:
    mov rsi, qword ptr [rsp+30h]        ; a wiped buffer that is large enough, hand it out again
    test rsi, rsi
    jz pa_alloc
    mov qword ptr [rsi+POOL_ST_IL], r13
    mov dword ptr [rsi+POOL_ST_SIZE], r14d
    mov dword ptr [rsi+POOL_ST_CRC], r12d
    mov dword ptr [rsi+POOL_ST_STATE], 0
    mov eax, dword ptr [rsp+2Ch]
    mov dword ptr [rsi+POOL_ST_GEN], eax
    mov eax, dword ptr [rsp+18h]
    mov dword ptr [rsi+POOL_ST_IDX], eax
    mov rcx, qword ptr [rsp+20h]
    mov qword ptr [rsi+POOL_ST_RBP], rcx
    mov r10, rsi
    mov rax, qword ptr [rsi+POOL_ST_SCR]
    xor edx, edx
    jmp pa_done
    ; allocate and track it in a free slot
pa_alloc:
    xor edi, edi
pa_slot:
    cmp edi, POOLN
    jae pa_untracked
    mov rsi, rdi
    imul rsi, rsi, POOL_SLOT
    add rsi, rbx
    cmp dword ptr [rsi+POOL_ST_TID], 0
    je pa_new
    inc edi
    jmp pa_slot
pa_new:
    xor ecx, ecx
    lea edx, [r14+1000h]                ; body + EH slack
    mov r8d, 3000h
    mov r9d, 4h                         ; PAGE_READWRITE, the JIT only reads it
    mov rax, qword ptr [g_pVirtualAlloc]
    test rax, rax
    jz pa_fail
    call rax
    test rax, rax
    jz pa_fail
    mov qword ptr [rsi+POOL_ST_SCR], rax
    mov qword ptr [rsi+POOL_ST_IL], r13
    mov dword ptr [rsi+POOL_ST_SIZE], r14d
    mov dword ptr [rsi+POOL_ST_CRC], r12d
    lea ecx, [r14+1000h]
    mov dword ptr [rsi+POOL_ST_CAP], ecx
    mov dword ptr [rsi+POOL_ST_TID], r15d
    mov dword ptr [rsi+POOL_ST_STATE], 0
    mov ecx, dword ptr [rsp+2Ch]        ; generation, written through ecx so rax keeps the buffer
    mov dword ptr [rsi+POOL_ST_GEN], ecx
    mov ecx, dword ptr [rsp+18h]
    mov dword ptr [rsi+POOL_ST_IDX], ecx
    mov rcx, qword ptr [rsp+20h]
    mov qword ptr [rsi+POOL_ST_RBP], rcx
    mov r10, rsi
    xor edx, edx
    jmp pa_done
pa_untracked:
    ; every slot belongs to a compile still in flight, the buffer stays valid
    ; but untracked, released only when the process ends
    xor ecx, ecx
    lea edx, [r14+1000h]
    mov r8d, 3000h
    mov r9d, 4h
    mov rax, qword ptr [g_pVirtualAlloc]
    test rax, rax
    jz pa_fail
    call rax
    test rax, rax
    jz pa_fail
    xor r10d, r10d
    xor edx, edx
    jmp pa_done
pa_fail:
    xor eax, eax
    xor edx, edx
    xor r10d, r10d
pa_done:
    mov qword ptr [rsp+38h], r10        ; owning slot, kept until the map call is done
    mov qword ptr [rsp+0], rax          ; buffer
    mov dword ptr [rsp+8], edx          ; plaintext state
    test rax, rax
    jz pa_ret
    mov rcx, qword ptr [rsp+10h]        ; ftn
    test rcx, rcx
    jz pa_ret
    mov rdx, rax
    mov r8, r13                         ; the image still holds that body in ciphertext
    mov r9d, r14d
    mov r10d, dword ptr [rsp+18h]
    call MapPut
pa_ret:
    mov rax, qword ptr [rsp+0]
    mov edx, dword ptr [rsp+8]
    mov r10, qword ptr [rsp+38h]
    add rsp, 40h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
PoolAcquire ENDP

; decrypt cache, key ilCode + ilSize + crc after mask, value decrypted scratch
; rcx=crc rdx=ilCode r8d=ilSize -> rax=scratch or 0, rsi/rdi/r13/r15 preserved
CacheLookup PROC
    cmp dword ptr [g_cacheOn], 0        ; tiered on, never reuse a buffer across compiles
    jz cl_miss2
    mov r11, [g_pCache]
    test r11, r11
    jz cl_miss2                         ; table not built yet -> miss
    mov r10d, 9E3779B1h
    mov eax, ecx
    imul eax, r10d
    xor eax, r8d
    and eax, CACHECAP-1
    shl rax, 5                          ; *32
    add r11, rax
    mov r9, qword ptr gs:[48h]          ; buffers are per thread, never shared
    mov r10d, CACHEPROBE
cl_loop:
jit_slot_0053::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    mov rax, [r11]
    test rax, rax
    jz cl_miss                          ; empty slot -> miss
    cmp rax, rdx
    jne cl_next
    cmp dword ptr [r11+8], r8d
    jne cl_next
    cmp dword ptr [r11+12], ecx
    jne cl_next
    cmp dword ptr [r11+28], r9d
    jne cl_next
    mov rax, [r11+16]                   ; all four fields match -> hit
    mov rdx, r11                        ; entry pointer; caller checks the plaintext state
    ret
cl_next:
jit_slot_0054::
    db 0Fh, 1Fh, 44h, 00h, 00h, 66h, 0Fh, 1Fh, 44h, 00h, 00h, 90h
    add r11, 32
    dec r10d
    jnz cl_loop
cl_miss:
cl_miss2:
jit_slot_0055::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
    xor eax, eax
    ret
CacheLookup ENDP

; rcx=crc rdx=ilCode r8d=ilSize r9=scratch, rsi/rdi/r13/r15 preserved
; table allocated on first use, a static zero table does not compress
CacheStore PROC
    cmp dword ptr [g_cacheOn], 0
    jz cs_skip                          ; tiered on, nothing is cached, so nothing to store
    push rcx
    push rdx
    push r8
    push r9                             ; 4 pushes = 32 bytes, keeps 16 byte alignment before the following call
    mov rax, [g_pCache]
    test rax, rax
    jnz cs_have
    xor ecx, ecx
    mov edx, (CACHECAP+CACHEPROBE)*32
    mov r8d, 3000h
    mov r9d, 4h
    mov rax, [g_pVirtualAlloc]
    test rax, rax
    jz cs_out
    call rax
    test rax, rax
    jz cs_out
    mov [g_pCache], rax
cs_have:
jit_slot_0056::
    db 0Fh, 1Fh, 44h, 00h, 00h, 90h
    pop r9
    pop r8
    pop rdx
    pop rcx
    mov r11, [g_pCache]
    test r11, r11
    jz cs_ret
    mov r10d, 9E3779B1h
    mov eax, ecx
    imul eax, r10d
    xor eax, r8d
    and eax, CACHECAP-1
    shl rax, 5
    add r11, rax
    mov r10d, CACHEPROBE
cs_loop:
jit_slot_0057::
    db 0Fh, 1Fh, 40h, 00h, 66h, 0Fh, 1Fh, 44h, 00h, 00h
    mov rax, [r11]
    test rax, rax
    jz cs_put
    cmp rax, rdx
    jne cs_next
    cmp dword ptr [r11+8], r8d
    jne cs_next
    cmp dword ptr [r11+12], ecx
    jne cs_next
    jmp cs_put
cs_next:
jit_slot_0058::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    add r11, 32
    dec r10d
    jnz cs_loop
    ret                                 ; probe window full -> no caching, falls back to decrypting on every compile
cs_put:
jit_slot_0059::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    mov [r11], rdx
    mov dword ptr [r11+8], r8d
    mov dword ptr [r11+12], ecx
    mov [r11+16], r9
    mov rax, qword ptr gs:[48h]         ; owning thread id, part of the cache key
    mov dword ptr [r11+28], eax
    mov dword ptr [r11+24], 1           ; state, this scratch holds plaintext
    ; record it for the lazy scrub, tid + il + scratch + len + entry + frame
    mov r10, rax
    imul r10d, r10d, 6D2B79F5h
    shr r10d, 27
    and r10d, 31
    imul r10d, r10d, 30h                ; 48 byte slots
    lea rcx, g_scrubTab
    add rcx, r10
    mov [rcx], rax
    mov [rcx+8], rdx
    mov [rcx+16], r9
    mov dword ptr [rcx+24], r8d
    mov [rcx+32], r11
    mov [rcx+40], rbp                   ; CompileHook frame pointer of this compile
cs_ret:
jit_slot_0060::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 66h, 90h
    ret
cs_skip:
    xor eax, eax
    ret
cs_out:
jit_slot_0061::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    pop r9
    pop r8
    pop rdx
    pop rcx
    ret
CacheStore ENDP
; rcx = runtime ICorJitInfo vtable -> rax = our table or 0, one allocation
; +0000 table (SHIMSLOTS pointers, swapped into the runtime head), +0800 read only copy, callbacks see the real 'this'
BuildShim PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 20h                        ; the CFG offset array is gone, the table is plain data now
    mov r12, rcx                        ; runtime vtable
    mov r15d, 2                         ; fail reason, allocation
    xor ecx, ecx
    mov edx, SHIMTBLSZ*2                ; our table + the runtime copy
    mov r8d, 3000h                      ; MEM_COMMIT|MEM_RESERVE
    mov r9d, 4h                         ; PAGE_READWRITE, never executed, so never RX or RWX
    mov rax, qword ptr [g_pVirtualAlloc]
    test rax, rax
    jz bs_fail
    call rax
    test rax, rax
    jz bs_fail
    mov rbx, rax                        ; our table
    ; publish immediately, a clobbered rbx made the JIT read the null page
    mov qword ptr [g_shimTab], rax
    lea r14, [rax+SHIMTBLSZ]            ; read only copy of the runtime entries
    xor r10d, r10d
bs_copy:
jit_slot_0062::
    db 0Fh, 1Fh, 44h, 00h, 00h, 90h
    mov rax, qword ptr [r12+r10*8]      ; runtime entry
    mov qword ptr [r14+r10*8], rax      ; read only copy, the hooks delegate to it
    mov qword ptr [rbx+r10*8], rax      ; table[i] = the runtime entry itself
    inc r10d
    cmp r10d, SHIMSLOTS
    jb bs_copy
    ; the getMethodInfo slot proves an undecrypted body and can fix it
    ; installed into our own table, the coreclr image is never written
    cmp dword ptr [g_gmiOff], 0
    je bs_gmi_done
    mov ecx, dword ptr [g_gmiOff]
    mov rax, qword ptr [r14+rcx]        ; original getMethodInfo, from the read only copy
    test rax, rax
    jz bs_gmi_done
    mov qword ptr [g_origGmi], rax
    lea rax, GetMethodInfoHook
    mov qword ptr [rbx+rcx], rax        ; our slot -> the hook (its thunk is superseded)
bs_gmi_done:
    ; the gate must exist before the first compile, a lazy gate leaves the
    ; first methods unprotected, which is the window an inline candidate needs
    cmp dword ptr [g_skipCi], 0
    jne bs_gate_done
    cmp dword ptr [g_ciOff], 0
    je bs_gate_done
    cmp qword ptr [g_origCanInline], 0
    jne bs_gate_done
    mov ecx, dword ptr [g_ciOff]
    mov rax, qword ptr [r14+rcx]        ; original canInline, from the copy
    mov qword ptr [g_origCanInline], rax
    lea rax, GetCanInlineHook
    mov qword ptr [rbx+rcx], rax        ; our slot -> the gate
bs_gate_done:
    ; plain data, no executable page, nothing to register with CFG
    push rcx                            ; trace, table published, rdx = slots, r9 = table
    push rdx
    push r8
    push r9
    mov ecx, 16
    mov edx, SHIMSLOTS
    xor r8d, r8d
    mov r9, rbx
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    mov qword ptr [g_baseTab], r14
    mov rax, rbx
    jmp bs_done
bs_fail:
jit_slot_0063::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 44h, 00h, 00h
    push rcx                            ; trace, shim not built, r15d = reason, 1 no CFG API, 2 alloc
    push rdx
    push r8
    push r9
    mov ecx, 15
    mov edx, r15d
    xor r8d, r8d
    xor r9d, r9d
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    xor eax, eax
bs_done:
    add rsp, 20h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
BuildShim ENDP



CompileHook PROC FRAME
    push rbp
    .pushreg rbp
    mov rbp, rsp
    .setframe rbp, 0
    push rbx
    .pushreg rbx
    push rsi
    .pushreg rsi
    push rdi
    .pushreg rdi
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    sub rsp, 78h
    .allocstack 78h
    .endprolog
    mov qword ptr [rsp+48h], 0          ; shim object for this compile (0 = run the runtime object)
    mov qword ptr [rsp+60h], 0          ; scratch pool slot that owns this compile's IL buffer (0 = none)
    mov rcx, rbp                        ; record this compile so the hooks running inside the JIT can tell
    call PoolSetCur                     ; which scratch belongs to it and when it has unwound
    push rcx
    push rdx
    push r9
    mov r11, qword ptr [g_pTrace]
    test r11, r11
    jz snap_done
    mov r10, qword ptr [r8+10h]         ; ILCode as handed to us
    mov qword ptr [r11+SNAP_OFF], r10
    mov ecx, dword ptr [r8+18h]         ; ILCodeSize
    mov dword ptr [r11+SNAP_OFF+8], ecx
    cmp ecx, 64
    jbe snap_n
    mov ecx, 64
snap_n:
    xor eax, eax
snap_loop:
    cmp eax, ecx
    jae snap_done
    mov dl, byte ptr [r10+rax]
    mov byte ptr [r11+rax+SNAP_OFF+12], dl
    inc eax
    jmp snap_loop
snap_done:
    pop r9
    pop rdx
    pop rcx
    push rcx                            ; trace, compile request, ILCode/ILCodeSize as handed to us
    push rdx
    push r8
    push r9
    mov r9, qword ptr [r8+10h]          ; info->ILCode
    mov rdx, qword ptr [r8]             ; info->ftn, forensics, which method is being compiled
    mov r8d, dword ptr [r8+18h]         ; info->ILCodeSize
    mov ecx, 10
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    ; single step, TF detection
    pushfq
    pop rax
    test rax, 100h
    jz ch_ss_ok
    ud2
ch_ss_ok:
    push rcx                            ; trace, TF check passed
    push rdx
    push r8
    push r9
    mov ecx, 11
    xor edx, edx
    xor r8d, r8d
    xor r9d, r9d
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
jit_slot_0064::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 66h, 90h
    cmp dword ptr [g_adFlag], 0         ; set by BootAntheil after AD
    jnz ch_ad_ok
    ud2
ch_ad_ok:
    push rcx                            ; trace, adFlag check passed
    push rdx
    push r8
    push r9
    ; JIT activity counter for the boot's wipe timing, one bump per compile
    ; it cannot live in TraceEv which also fires for non compile events
    mov r11, qword ptr [g_pTrace]
    test r11, r11
    jz ch_no_count
    inc dword ptr [r11+12]
ch_no_count:
    mov ecx, 12
    xor edx, edx
    xor r8d, r8d
    xor r9d, r9d
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
jit_slot_0065::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    xor r15, r15                        ; scratch=0
    mov rbx, rcx                        ; self
    mov r12, rdx                        ; comp
    mov r13, r8                         ; info
    mov r14d, r9d                       ; flags
    ; no foreign object handed to the JIT, coreclr image never written
    mov rax, qword ptr [g_shimTab]
    test rax, rax
    jnz cs_shim_done                    ; already built (or another thread builds it right now)
    mov rcx, qword ptr [r12]            ; runtime ICorJitInfo vtable (read only)
    test rcx, rcx
    jz cs_shim_done
    lock bts dword ptr [g_shimLock], 0
    jc cs_shim_done                     ; another thread is building, this compile runs with the raw object
    mov rcx, qword ptr [r12]
    call BuildShim
    ; read the table back from memory, BuildShim returned a clobbered value
    mov rax, qword ptr [g_shimTab]
    test rax, rax
    jz cs_shim_unlock
    push rcx                            ; trace, shim table built, rdx = slot count, r9 = table
    push rdx
    push r8
    push r9
    mov ecx, 14
    mov edx, SHIMSLOTS
    xor r8d, r8d
    mov r9, rax
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
cs_shim_unlock:
    lock btr dword ptr [g_shimLock], 0
cs_shim_done:
    ; a nested compile sits lower on the stack, an outer compile is left alone
    ; tiered compilation hands the patched pointer back, so this is off there
    cmp dword ptr [g_scrubOn], 0
    jz ls_done
    mov r11, qword ptr gs:[48h]
    mov rax, r11
    imul eax, eax, 6D2B79F5h
    shr eax, 27
    and eax, 31
    imul eax, eax, 30h
    lea r10, g_scrubTab
    add r10, rax
    cmp qword ptr [r10], r11
    jne ls_done                         ; slot owned by another thread
    cmp rbp, [r10+40]                   ; rbp >= owning frame -> that compile already unwound
    jb ls_done
    mov r11, [r10+16]                   ; scratch
    mov rdx, [r10+8]                    ; il, the image still holds the ciphertext
    mov r8d, dword ptr [r10+24]         ; len
    test r11, r11
    jz ls_clear
    test rdx, rdx
    jz ls_clear
    test r8d, r8d
    jz ls_clear
    xor r9d, r9d
ls_loop:
jit_slot_0066::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 66h, 90h
    mov al, byte ptr [rdx+r9]
    mov byte ptr [r11+r9], al
    inc r9d
    cmp r9d, r8d
    jb ls_loop
    mov rax, [r10+32]                   ; cache entry that owns that buffer
    test rax, rax
    jz ls_clear
    mov dword ptr [rax+24], 0           ; state, ciphertext again, a later hit decrypts
ls_clear:
    mov qword ptr [r10], 0
ls_done:


; !! hook the ICorJitInfo canInline slot on the first compile
; its offset varies by runtime and inline analysis reads callee IL from the raw image
; ciphertext parses as garbage so encrypted callees get INLINE_NEVER(-2) and the patch lands in our entry table
gmi_shim:
    cmp dword ptr [g_ciOff], 0
    je gmi_skip
    cmp dword ptr [g_skipCi], 0
    jne gmi_skip                        ; gate left off on request
    mov r10, qword ptr [g_shimTab]
    test r10, r10
    jz gmi_skip                         ; no shim this compile, gate off, coreclr stays untouched
    cmp qword ptr [g_origCanInline], 0
    jne gmi_skip
    mov r11, qword ptr [g_baseTab]
    test r11, r11
    jz gmi_skip
    mov ecx, dword ptr [g_ciOff]
    mov rax, qword ptr [r11+rcx]        ; original canInline, from the read only copy
    mov qword ptr [g_origCanInline], rax
    lea rax, GetCanInlineHook
    mov qword ptr [r10+rcx], rax        ; our slot -> the gate
gmi_skip:
    push rcx                            ; trace, canInline slot block done
    push rdx
    push r8
    push r9
    mov ecx, 13
    xor edx, edx
    xor r8d, r8d
    xor r9d, r9d
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
jit_slot_0067::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 44h, 00h, 00h
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
    mov rax, qword ptr [g_pSigs]        ; sig table handed over by boot, empty means disabled
    test rax, rax
    jz ch_orig
    xor ecx, dword ptr [g_maskLo]       ; crc2 ^ mask32, mask is passed in by boot from a separate domain
    xor edx, edx                        ; i=0
ch_sig:
jit_slot_0068::
    db 0Fh, 1Fh, 40h, 00h, 0Fh, 1Fh, 40h, 00h
    cmp edx, dword ptr [g_sigCount]
    jae ch_miss
    lea r10, [rdx*4]                    ; 4 bytes per entry
    mov r8d, [rax+r10]                  ; entry = crc2^mask
    cmp r8d, ecx
    je ch_payload
    inc edx
    jmp ch_sig
ch_miss:
jit_slot_0069::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    ; with tiered compilation on the runtime caches the ILCode we patched and hands it back on a recompile
    ; that buffer may have been wiped or reused so decrypt this method's body from the image again
    push rcx                            ; the miss trace reports crc^mask, and MapFind reuses ecx
    mov rcx, qword ptr [r13]            ; ftn of the method being compiled
    mov rdx, rsi                        ; ILCode handed over
    mov r8d, edi                        ; its ILCodeSize
    call MapFind                        ; rax=image ILCode (0 = not ours) edx=index r8d=size r9d=crc^mask
    pop rcx
    test rax, rax
    jz ch_miss_trace
    mov rsi, rax                        ; image of the body this scratch belongs to
    mov edi, r8d                        ; its ILCodeSize
    mov ecx, r9d                        ; its crc^mask
    mov dword ptr [rsp+68h], edx        ; its signature table index
    ; trace, recompile of a patched body, decrypted from the image again
    push rcx
    push rdx
    push r8
    push r9
    push rax
    mov r9, rsi                         ; the pointer that came back, i.e. our recycled buffer
    mov r8, rax                         ; image of the body it belongs to
    mov ecx, 21                         ; 21 = matched through the method handle
    test r11d, r11d                     ; 22 = matched through the buffer itself
    jz ch_map_trace
    mov ecx, 22
ch_map_trace:
    call TraceEv
    pop rax
    pop r9
    pop r8
    pop rdx
    pop rcx
    jmp ch_payload
ch_miss_trace:
    push rcx                            ; trace must not disturb the args, the miss path tail jumps
    push rdx
    push r8
    push r9
    mov r9, rsi                         ; trace, the IL we are about to hand to the JIT unchanged
    mov r8d, edi
    mov edx, ecx                        ; crc^mask, same form as the sig table entries
    mov ecx, 2
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    jmp ch_orig

ch_payload:
jit_slot_0070::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
    push rcx                            ; trace, encrypted method recognised
    push rdx
    push r8
    push r9
    mov edx, ecx
    mov ecx, 1
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    mov dword ptr [rsp+58h], ecx        ; cache key, crc2^mask, later derivation overwrites ecx
    mov dword ptr [rsp+68h], edx        ; signature table index, kept for the scratch pool and the map
    ; uKey2 = DeriveKey2(uKey, entry index), formula matches the packer
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]          ; uKey (jit key)
    mov ecx, edx                        ; entry index
    imul rcx, rcx, 27220A95h
    xor r9, rcx                         ; h = uKey ^ (i*C1)
    mov rcx, r9
    shr rcx, 33
    xor r9, rcx                         ; h ^= h>>33
    imul r9, r9, 165667B1h
    mov rcx, r9
    shr rcx, 29
    xor r9, rcx                         ; h ^= h>>29
    imul r9, r9, 27220A95h
    mov rcx, r9
    shr rcx, 32
    xor r9, rcx                         ; uKey2
    mov qword ptr [rsp+50h], r9         ; stash uKey2 (survives VirtualAlloc)
    ; cache lookup, a hit reuses the decrypted scratch, no VirtualAlloc
    mov ecx, dword ptr [rsp+58h]
    mov rdx, rsi                        ; ilCode
    mov r8d, edi                        ; ilSize
    call CacheLookup
    test rax, rax
    jz ch_no_cache
    mov r15, rax
    cmp dword ptr [rdx+24], 0           ; 0 = buffer was scrubbed -> decrypt again
    je ch_have
    jmp ch_set_il                       ; 1 = still plaintext and untouched -> reuse as is
ch_no_cache:
jit_slot_0071::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h
    cmp dword ptr [g_cacheOn], 0
    jne ch_fresh_alloc                  ; cache in use, its buffer is owned by the cache and scrubbed there
    ; no cache (tiered on), the pool hands out a recycled wiped buffer
    ; compile and no buffer of an unwound compile keeps its plaintext
    mov ecx, dword ptr [rsp+58h]        ; crc^mask
    mov rdx, rsi                        ; ilCode
    mov r8d, edi                        ; ilSize
    mov r9, qword ptr [r13]             ; ftn, the map remembers which method owns the buffer
    mov r11d, dword ptr [rsp+68h]       ; signature table index
    call PoolAcquire
    test rax, rax
    jz ch_orig
    mov r15, rax                        ; scratch
    mov qword ptr [rsp+60h], r10        ; owning pool slot (0 = untracked)
    test edx, edx
    jnz ch_set_il                       ; same compile asked again, plaintext and EH are already in place
    jmp ch_have
ch_fresh_alloc:
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
ch_have:
jit_slot_0072::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    ; the cache holds allocations, not plaintext, the scratch is scrubbed once
    ; its compile has unwound, so both passes run again on ciphertext
ch_lock_retry:
jit_slot_0073::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 40h, 00h, 90h
    lock bts dword ptr [g_compLock], 0
    jc ch_lock_wait
    jmp ch_lock_got
ch_lock_wait:
jit_slot_0074::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 66h, 90h
    pause
    test dword ptr [g_compLock], 1
    jnz ch_lock_wait
    jmp ch_lock_retry
ch_lock_got:
jit_slot_0075::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    ; layer2, scratch = ilCode ^ stream(uKey2) = layer1 ciphertext
    ; layer1, scratch = layer1 ^ stream(g_keyA^g_keyB) = plaintext
    ; a wrong variant decrypts to garbage the JIT rejects as InvalidProgram, so the expected variant is tried first
    ; the other three follow on failure, only caller saved registers are touched, the variant comes from [rsp+50h]
    ; variant bytes live in g_pVars, the table is handed over at run time
    mov rax, qword ptr [g_pVars]
    test rax, rax
    jz ch_var_formula
    mov ecx, dword ptr [rsp+68h]        ; signature table index (rcx is reloaded before XorDecrypt below)
    movzx r10d, byte ptr [rax+rcx]
    and r10d, 3
    jmp ch_var_tagged
ch_var_formula:
    mov r10, qword ptr [rsp+50h]        ; uKey2, 64 bit
    mov rax, qword ptr [g_keyA]
    xor rax, qword ptr [g_keyB]         ; reassemble key, 64 bit
    xor r10, rax                        ; uKey2 ^ key
    and r10d, 3                         ; expected variant
ch_var_tagged:
    mov dword ptr [g_varStart], r10d
    mov dword ptr [g_variant], r10d
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+50h]
    call RdtscStart
    mov r10d, 1                         ; layer2 (uKey2, variant nonce)
    call XorDecrypt
    call RdtscCheck
    mov dword ptr [g_variant], 0        ; variant 0 for layer1
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]          ; reassemble key for layer1, 64 bit
    mov r10d, 0                         ; layer1 (jit_key, fixed nonce)
    call XorDecrypt
    mov rcx, r15
    mov edx, edi
    call IlPlausible
    test eax, eax
    jnz ch_vdone
    mov r10d, dword ptr [g_varStart]
    inc r10d
    and r10d, 3
    mov dword ptr [g_variant], r10d
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+50h]
    call RdtscStart
    mov r10d, 1
    call XorDecrypt
    call RdtscCheck
    mov dword ptr [g_variant], 0
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]
    mov r10d, 0
    call XorDecrypt
    mov rcx, r15
    mov edx, edi
    call IlPlausible
    test eax, eax
    jnz ch_vrec1
    mov r10d, dword ptr [g_varStart]
    add r10d, 2
    and r10d, 3
    mov dword ptr [g_variant], r10d
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+50h]
    call RdtscStart
    mov r10d, 1
    call XorDecrypt
    call RdtscCheck
    mov dword ptr [g_variant], 0
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]
    mov r10d, 0
    call XorDecrypt
    mov rcx, r15
    mov edx, edi
    call IlPlausible
    test eax, eax
    jnz ch_vrec2
    mov r10d, dword ptr [g_varStart]
    add r10d, 3
    and r10d, 3
    mov dword ptr [g_variant], r10d
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+50h]
    call RdtscStart
    mov r10d, 1
    call XorDecrypt
    call RdtscCheck
    mov dword ptr [g_variant], 0
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]
    mov r10d, 0
    call XorDecrypt
    mov rcx, r15
    mov edx, edi
    call IlPlausible
    test eax, eax
    jnz ch_vrec3
    ; none of the four looked like IL, restore the expected variant
    mov r10, qword ptr [rsp+50h]
    mov rax, qword ptr [g_keyA]
    xor rax, qword ptr [g_keyB]
    xor r10, rax
    and r10d, 3
    mov dword ptr [g_variant], r10d
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    mov r9, qword ptr [rsp+50h]
    mov r10d, 1
    call XorDecrypt
    mov dword ptr [g_variant], 0
    mov rcx, r15
    mov rdx, r15
    mov r8d, edi
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]
    mov r10d, 0
    call XorDecrypt
    inc dword ptr [g_recoverFail]
    jmp ch_vdone
ch_vrec1:
    inc dword ptr [g_recover]
    jmp ch_vdone
ch_vrec2:
    inc dword ptr [g_recover]
    jmp ch_vdone
ch_vrec3:
    inc dword ptr [g_recover]
ch_vdone:
    lock btr dword ptr [g_compLock], 0
    ; !! fat methods with more sections, EH follows the body
    ; the JIT locates EH by ILCode+ILCodeSize, a missing copy reads OOB
    mov al, byte ptr [rsi-1]            ; header byte0
    and al, 3
    cmp al, 2                           ; tiny? tiny has no EH
    je ch_set_il
    movzx eax, byte ptr [rsi-12]        ; fat header flag byte
    test al, 8                          ; CorILMethod_MoreSects
    jz ch_set_il
    lea rax, [rsi+rdi]                  ; more sections start, raw image
    movzx ecx, byte ptr [rax]           ; kind
    cmp cl, 41h                         ; EH small section?
    je ch_eh_small
    cmp cl, 40h                         ; EH fat section? (0x40,00,00)
    jne ch_set_il
    cmp byte ptr [rax+1], 0             ; fat kind 2nd byte
    jne ch_set_il
    movzx ecx, byte ptr [rax+3]         ; dataSize, 4 byte units, fat
    lea r8d, [rcx*4+4]                  ; total = 4 + dataSize*4
    jmp ch_eh_len
ch_eh_small:
jit_slot_0076::
    db 0Fh, 1Fh, 40h, 00h, 66h, 90h
    movzx ecx, byte ptr [rax+1]         ; dataSize, 2 byte units, small
    lea r8d, [rcx*2+4]                  ; total = 4 + dataSize*2
ch_eh_len:
jit_slot_0077::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    cmp r8d, 1000h                      ; cap at EH slack
    ja ch_set_il
    lea r9, [r15+rdi]                   ; dst = scratch+ilSize
    xor r10d, r10d
ch_cpyeh:
jit_slot_0078::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp r10, r8
    jae ch_set_il
    movzx r11d, byte ptr [rax+r10]
    mov byte ptr [r9+r10], r11b
    inc r10
    jmp ch_cpyeh
ch_set_il:
jit_slot_0079::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h
    ; the pool slot is marked plaintext, a reuse skips both passes
    mov rax, qword ptr [rsp+60h]
    test rax, rax
    jz ch_il_unmarked
    mov dword ptr [rax+POOL_ST_STATE], 1
ch_il_unmarked:
    ; in place mode the decrypted IL goes back into the module image
    ; so info->ILCode stays as the runtime handed it over for the tiering code
    cmp dword ptr [g_inPlace], 0
    jz ch_set_il_heap
    mov rax, [g_pVirtualProtect]
    test rax, rax
    jz ch_set_il_heap
    mov rcx, rsi
    mov edx, edi
    mov r8d, 4                          ; PAGE_READWRITE
    lea r9, [rbp-30h]
    call rax
    test rax, rax
    jz ch_set_il_heap
    xor r10d, r10d
ch_cpy_il:
jit_slot_0080::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp r10, rdi
    jae ch_cp_il_done
    mov al, byte ptr [r15+r10]
    mov byte ptr [rsi+r10], al
    inc r10
    jmp ch_cpy_il
ch_cp_il_done:
jit_slot_0081::
    db 0Fh, 1Fh, 40h, 00h, 0Fh, 1Fh, 00h, 90h
    mov rax, [g_pVirtualProtect]
    mov rcx, rsi
    mov edx, edi
    mov r8d, dword ptr [rbp-30h]
    lea r9, [rbp-30h]
    call rax
    jmp ch_orig
ch_set_il_heap:
jit_slot_0082::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
    mov [r13+10h], r15                  ; !! info->ILCode=scratch, rest filled by CLR, only patch pointer
    ; store in cache, decrypt and EH ready, a recompile on the same key hits
    mov ecx, dword ptr [rsp+58h]
    mov rdx, rsi
    mov r8d, edi
    mov r9, r15
    call CacheStore
    ; the cache hands the buffer back on a recompile, so the map must know it
    mov rcx, qword ptr [r13]            ; ftn
    mov rdx, r15                        ; scratch handed out
    mov r8, rsi                         ; image of the body
    mov r9d, edi
    mov r10d, dword ptr [rsp+68h]
    call MapPut
ch_orig:
jit_slot_0083::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 90h
    push rcx                            ; trace, final IL the JIT will read, vs the one handed to us, rdx vs r8
    push rdx
    push r8
    push r9
    mov rdx, qword ptr [r13+10h]        ; scratch when this compile decrypted, the raw image pointer otherwise
    mov r8, rsi                         ; info->ILCode as handed to us
    mov r9, rdi                         ; info->ILCodeSize
    mov ecx, 18
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    mov rax, [g_orig]                   ; original compileMethod
    test rax, rax
    jz ch_done
    ; tail jumped so its stack args stay where runtime put them, callbacks see the real 'this'
    ; the vtable in the image is untouched, our table sits in the runtime owned object head
    mov r11, qword ptr [g_shimTab]
    test r11, r11
    jz ch_no_swap
    mov qword ptr [r12], r11            ; our table into the runtime object's head
ch_no_swap:
    mov rcx, rbx                        ; this = ICorJitCompiler
    mov rdx, r12                        ; comp = the runtime object, never a proxy
    mov r8, r13
    mov r9d, r14d
    add rsp, 78h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    pop rbp
    jmp rax                             ; tail jump, stack args untouched
ch_done:
jit_slot_0084::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 66h, 90h
    xor eax, eax
    add rsp, 78h
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
; inline analysis reads the raw callee IL so encrypted callees get INLINE_NEVER(-2)
GetCanInlineHook PROC FRAME
    push rbp
    .pushreg rbp
    mov rbp, rsp
    .setframe rbp, 0
    push rbx
    .pushreg rbx
    push rsi
    .pushreg rsi
    push rdi
    .pushreg rdi
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    push r14
    .pushreg r14
    sub rsp, 400h
    .allocstack 400h
    .endprolog
    mov r12, rcx                        ; this is the runtime object itself, our table only lives in its head
    mov r13, rdx                        ; caller
    mov r14, r8                         ; callee
    push rcx
    push rdx
    push r8
    push r9
    mov ecx, 3
    xor edx, edx
    xor r8d, r8d
    xor r9d, r9d
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    mov [rsp+3F8h], r9                  ; restrict out param comes from the caller, never substitute an own buffer
    mov rax, [g_origCanInline]
    test rax, rax
    jz gci_done
    mov rcx, r12                        ; the runtime object, not the shim, calls the real entry point
    mov r9, [rsp+3F8h]
    call rax                            ; orig canInline -> eax

    mov ebx, eax                        ; save original decision
    cmp dword ptr [g_noCiScan], 0
    jne gci_ret                         ; keep the decision, skip the callee scan
    ; encrypted callee check, getMethodInfo slot varies by runtime
    cmp dword ptr [g_gmiOff], 0
    je gci_ret                          ; no slot handed over, keep the JIT decision untouched
    mov rax, [r12]
    test rax, rax
    jz gci_ret
    mov r10d, [g_gmiOff]                ; getMethodInfo slot, varies by version
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
    mov rax, qword ptr [g_origGmi]      ; our own hook now lives in the table we borrow for the compile,
    test rax, rax                       ; so call the runtime entry directly instead of going in circles
    jnz gci_gmi_ready
    mov rax, [r12]
    mov r10d, [g_gmiOff]
    mov rax, [rax+r10]
gci_gmi_ready:
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
    mov rax, qword ptr [g_pSigs]        ; sig table handed over by boot
    test rax, rax
    jz gci_ret
    xor ecx, dword ptr [g_maskLo]       ; crc ^ mask32, mask is passed in by boot from a separate domain
    xor edx, edx
gci_sig:
jit_slot_0085::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp edx, dword ptr [g_sigCount]
    jae gci_ret
    lea r10, [rdx*4]                    ; 4 bytes per entry
    mov r8d, [rax+r10]                  ; entry = crc^mask
    cmp r8d, ecx
    je gci_never
    inc edx
    jmp gci_sig
gci_never:
jit_slot_0086::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    mov ebx, -2                         ; INLINE_NEVER
gci_ret:
jit_slot_0087::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 40h, 00h
    mov eax, ebx
gci_done:
jit_slot_0088::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 66h, 90h
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



; rcx = event code, rdx = crc, r8d = ilSize, r9 = ilCode, one ring record provided by the boot (count at +8)
; rcx = crc^mask, rdx = ilCode, r8d = ilSize, r9d = sig index, r10 = ftn -> rax = plaintext scratch or 0
; decrypts a callee body on demand for getMethodInfo, one request is memoised and a handed out scratch is never overwritten
GmiDecrypt PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 40h
    mov r12d, ecx                       ; crc^mask
    mov r13, rdx                        ; ilCode
    mov r14d, r8d                       ; ilSize
    mov r15d, r9d                       ; sig table index
    mov qword ptr [rsp+30h], r10        ; ftn, needed by the pool to register the buffer
    ; the scratch comes from the pool, recycled and wiped before reuse
    ; a second request for the same body in one compile hits the live slot
    mov ecx, r12d
    mov rdx, r13
    mov r8d, r14d
    mov r9, qword ptr [rsp+30h]
    mov r11d, r15d
    call PoolAcquire
    test rax, rax
    jz gd_fail
    mov rbx, rax                        ; scratch
    mov qword ptr [rsp+28h], r10        ; owning pool slot (0 = untracked)
    test edx, edx
    jnz gd_mark                         ; same body in the same compile, plaintext and EH are already there
    ; uKey2 = DeriveKey2(uKey, index), derivation packer and CompileHook use
    mov r10, qword ptr [g_keyA]
    xor r10, qword ptr [g_keyB]         ; uKey
    mov rcx, r15
    imul rcx, rcx, 27220A95h
    xor r10, rcx                        ; h = uKey ^ (i*C1)
    mov rcx, r10
    shr rcx, 33
    xor r10, rcx
    imul r10, r10, 165667B1h
    mov rcx, r10
    shr rcx, 29
    xor r10, rcx
    imul r10, r10, 27220A95h
    mov rcx, r10
    shr rcx, 32
    xor r10, rcx                        ; uKey2
    mov qword ptr [rsp+20h], r10
gd_lock:
jit_slot_0089::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 66h, 0Fh, 1Fh, 44h, 00h, 00h
    lock bts dword ptr [g_compLock], 0
    jc gd_lock_wait
    jmp gd_lock_got
gd_lock_wait:
    pause
    test dword ptr [g_compLock], 1
    jnz gd_lock_wait
    jmp gd_lock
gd_lock_got:
    mov r10, qword ptr [rsp+20h]
    mov rax, qword ptr [g_keyA]
    xor rax, qword ptr [g_keyB]
    xor r10, rax
    and r10d, 3
    mov dword ptr [g_variant], r10d     ; layer2 nonce variant (global, so it is serialised)
    mov rcx, rbx
    mov rdx, r13
    mov r8d, r14d
    mov r9, qword ptr [rsp+20h]
    mov r10d, 1                         ; layer2
    call XorDecrypt
    mov dword ptr [g_variant], 0
    mov rcx, rbx
    mov rdx, rbx
    mov r8d, r14d
    mov r9, qword ptr [g_keyA]
    xor r9, qword ptr [g_keyB]
    mov r10d, 0                         ; layer1
    call XorDecrypt
    lock btr dword ptr [g_compLock], 0
    ; !! fat methods with more sections, EH follows the body
    ; the JIT locates EH by ILCode+ILCodeSize, a missing copy reads OOB
    mov al, byte ptr [r13-1]
    and al, 3
    cmp al, 2                           ; tiny has no EH
    je gd_mark
    movzx eax, byte ptr [r13-12]        ; fat header flags
    test al, 8                          ; CorILMethod_MoreSects
    jz gd_mark
    lea rax, [r13+r14]
    movzx ecx, byte ptr [rax]
    cmp cl, 41h                         ; small EH section
    je gd_eh_small
    cmp cl, 40h                         ; fat EH section
    jne gd_mark
    cmp byte ptr [rax+1], 0
    jne gd_mark
    movzx ecx, byte ptr [rax+3]
    lea r8d, [rcx*4+4]
    jmp gd_eh_len
gd_eh_small:
    movzx ecx, byte ptr [rax+1]
    lea r8d, [rcx*2+4]
gd_eh_len:
    cmp r8d, 1000h                      ; cap at the slack the allocation reserved
    ja gd_mark
    lea r9, [rbx+r14]                   ; dst = scratch + ilSize
    xor r10d, r10d
gd_eh_copy:
jit_slot_0090::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
    cmp r10, r8
    jae gd_mark
    movzx r11d, byte ptr [rax+r10]
    mov byte ptr [r9+r10], r11b
    inc r10
    jmp gd_eh_copy
gd_mark:
    mov rax, qword ptr [rsp+28h]        ; pool slot that owns this buffer, it now holds plaintext, so a reuse
    test rax, rax                       ; inside the same compile may skip both decrypt passes
    jz gd_ret
    mov dword ptr [rax+POOL_ST_STATE], 1
gd_ret:
    mov rax, rbx
    jmp gd_done
gd_fail:
    xor eax, eax
gd_done:
    add rsp, 40h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
GmiDecrypt ENDP


; rcx = ICorJitInfo (our proxy with the shim on), rdx = method handle
; r8 = CORINFO_METHOD_INFO* r9 = CORINFO_CONTEXT_HANDLE -> al (bool)
; reports every body the JIT reads with whether the image bytes are still ciphertext, ev19 carries rdx = ILCode low32, r8 = ILCodeSize, r9 = 1 when still ciphertext
; swap info->ILCode to hand the consumer decrypted bytes
GetMethodInfoHook PROC FRAME
    push rbx
    .pushreg rbx
    push rsi
    .pushreg rsi
    push rdi
    .pushreg rdi
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    sub rsp, 40h
    .allocstack 40h
    .endprolog
    mov r12, rcx                        ; this is the runtime object itself, the swap puts our table in its head
    mov r13, r8                         ; CORINFO_METHOD_INFO*
    mov qword ptr [rsp+30h], rdx        ; ftn of the callee, still needed when the pointer turns out to be ours
    mov rax, qword ptr [g_origGmi]
    test rax, rax
    jz gmi_hook_fail
    mov rcx, r12
    call rax                            ; rdx (ftn), r8 (info) and r9 (context) pass through untouched
    mov byte ptr [rsp+28h], al          ; keep the bool
    test al, al
    jz gmi_hook_ret
    mov rsi, qword ptr [r13+10h]        ; ILCode handed to the JIT
    mov edi, dword ptr [r13+18h]        ; ILCodeSize
    test rsi, rsi
    jz gmi_hook_ret
    test rdi, rdi
    jz gmi_hook_ret
    mov rcx, rsi
    mov rdx, rdi
    call Crc32Fn                        ; eax = crc over exactly the bytes the JIT will read
    xor eax, dword ptr [g_maskLo]       ; same form as the sig table entries
    mov r11, qword ptr [g_pSigs]
    test r11, r11
    jz gmi_hook_ret
    xor edx, edx
gmi_hook_scan:
jit_slot_0091::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
    cmp edx, dword ptr [g_sigCount]
    jae gmi_hook_miss
    lea r10, [rdx*4]                    ; 4 bytes per entry
    cmp dword ptr [r11+r10], eax
    je gmi_hook_hit
    inc edx
    jmp gmi_hook_scan
gmi_hook_miss:
    ; trace, a body absent from the sig table, passed through to the JIT
    push rcx
    push rdx
    push r8
    push r9
    mov edx, eax                        ; crc^mask
    mov r8d, edi                        ; ILCodeSize
    mov r9, rsi                         ; ILCode
    mov ecx, 20
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
    ; a recompile hands back the ILCode we patched, that buffer may have been wiped or reused since
    ; so decrypt the body that belongs to this method from the image again
    mov rcx, qword ptr [rsp+30h]        ; ftn
    mov rdx, rsi                        ; ILCode handed over
    mov r8d, edi                        ; its ILCodeSize
    call MapFind                        ; rax=image ILCode (0 = not ours) edx=index r8d=size r9d=crc^mask
    test rax, rax
    jz gmi_hook_ret
    mov r10, qword ptr [rsp+30h]        ; ftn
    mov ebx, r11d                       ; 0 = the method handle identified the body, 1 = the buffer did
    mov ecx, r9d                        ; its crc^mask
    mov r9d, edx                        ; sig table index of that body
    mov rdx, rax                        ; its image
    call GmiDecrypt                     ; r8d = ILCodeSize from the map
    test rax, rax
    jz gmi_hook_deny
    mov qword ptr [r13+10h], rax        ; info->ILCode -> plaintext of the body that method really owns
    lea ebx, [rbx+2]                    ; 2 = handled by the map, 3 = handled by the map through the buffer
    jmp gmi_hook_trace
gmi_hook_hit:
    ; the body is still ciphertext so hand the consumer a decrypted scratch
    ; the inliner reads other methods' bodies and the image keeps the ciphertext
    mov ecx, eax                        ; crc^mask
    mov r9d, edx                        ; sig table index (key derivation input)
    mov rdx, rsi                        ; ilCode, ciphertext in the image
    mov r8d, edi                        ; ilSize
    mov r10, qword ptr [rsp+30h]        ; ftn of the callee
    call GmiDecrypt
    test rax, rax
    jz gmi_hook_deny
    mov qword ptr [r13+10h], rax        ; info->ILCode -> plaintext scratch
    mov ebx, 1
    jmp gmi_hook_trace
gmi_hook_deny:
    mov byte ptr [rsp+28h], 0           ; last resort, reporting "no method info" beats handing out ciphertext
    xor ebx, ebx
gmi_hook_trace:
jit_slot_0092::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    push rcx                            ; one record per encrypted body the JIT asked about
    push rdx
    push r8
    push r9
    mov rdx, rsi                        ; ILCode low 32
    mov r8, rdi                         ; ILCodeSize
    mov r9, rbx                         ; 1 = decrypted to a scratch, 0 = denied
    mov ecx, 19
    call TraceEv
    pop r9
    pop r8
    pop rdx
    pop rcx
gmi_hook_ret:
    mov al, byte ptr [rsp+28h]
    add rsp, 40h
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
gmi_hook_fail:
    xor eax, eax
    add rsp, 40h
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
GetMethodInfoHook ENDP



TraceEv PROC
    push rax
    push r10
    push r11
    mov r11, qword ptr [g_pTrace]
    test r11, r11
    jz te_done
    mov eax, dword ptr [g_traceN]
    cmp eax, 8192
    jae te_done
    mov r10d, eax
    shl r10, 4
    add r11, r10
    add r11, 16                         ; records start after the 16 byte header (count lives at +8)
    mov dword ptr [r11], ecx
    mov dword ptr [r11+4], edx
    mov dword ptr [r11+8], r8d
    mov dword ptr [r11+12], r9d
    inc eax
    mov dword ptr [g_traceN], eax
    mov r10, qword ptr [g_pTrace]
    mov dword ptr [r10+8], eax
te_done:
    pop r11
    pop r10
    pop rax
    ret
TraceEv ENDP

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

    ; single step, TF detection
    pushfq
    pop rax
    test rax, 100h
    jz ih_ss_ok
    ud2

ih_ss_ok:
    mov [g_scrubOn], ecx                ; 1 = tiered off, cache + scrub safe
    mov [g_cacheOn], ecx                ; tiered on -> decrypt into a fresh buffer every compile instead
    mov [g_pTrace], rdx                 ; optional trace ring handed over by the boot
jit_slot_0093::
    db 66h, 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h, 90h
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
    ; SetProcessValidCallTargets lives in kernelbase, kernel32 never forwards
    ; GetModuleHandleA first and look the export up in that module directly
    lea rcx, szGetModuleHandle
    call ResolveApi
    test rax, rax
    jz ih_cfg_done
    mov r12, rax                        ; GetModuleHandleA
    lea rcx, szKernelbase
    call r12
    test rax, rax
    jz ih_cfg_done
    mov rcx, rax
    lea rdx, szSetValid
    call FindExportInModule
    mov [g_pSetValid], rax              ; 0 = shim unavailable, BuildShim then leaves it off
    lea rcx, szGetLastError
    call ResolveApi
    mov [g_pLastErr], rax               ; diagnostics only
ih_cfg_done:

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
jit_slot_0094::
    db 0Fh, 1Fh, 40h, 00h, 0Fh, 1Fh, 40h, 00h
    mov eax, 1
    jmp ih_ret
ih_e2:
jit_slot_0095::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h, 0Fh, 1Fh, 40h, 00h
    mov eax, 2
    jmp ih_ret
ih_e3:
jit_slot_0096::
    db 0Fh, 1Fh, 44h, 00h, 00h, 90h
    mov eax, 3
    jmp ih_ret
ih_e4:
jit_slot_0097::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 66h, 90h
    mov eax, 4
    jmp ih_ret
ih_e5:
jit_slot_0098::
    db 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h
    mov eax, 5
    jmp ih_ret
ih_e6:
jit_slot_0099::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 44h, 00h, 00h
    mov eax, 6
    jmp ih_ret
ih_ret:
jit_slot_0100::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
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



; returns the handover table of the six entry points (g_fnTab), index k = export ordinal k+1
; the boot calls this first, binds the setters and arms the hook later
; the table is filled at run time with lea, the image is mapped at any base so link time absolute addresses would be wrong
JitHookTable PROC
    lea rax, InstallJitHook
    mov qword ptr [g_fnTab + 0], rax
    lea rax, SetJitHookKey
    mov qword ptr [g_fnTab + 8], rax
    lea rax, SetSigTable
    mov qword ptr [g_fnTab + 16], rax
    lea rax, VerifyJitHook
    mov qword ptr [g_fnTab + 24], rax
    lea rax, SetAntiDebugFlag
    mov qword ptr [g_fnTab + 32], rax
    lea rax, SetJitSlots
    mov qword ptr [g_fnTab + 40], rax
    lea rax, g_fnTab
    ret
JitHookTable ENDP


SetJitHookKey PROC
    ; key split, g_keyA = key ^ SPLIT, g_keyB = SPLIT
    ; rdx = per run random SPLIT, no fixed constant in the binary
    mov r8, rdx                            ; SPLIT from caller
    mov qword ptr [g_keyB], r8
    mov rax, rcx
    xor rax, r8
    mov qword ptr [g_keyA], rax
    ret
SetJitHookKey ENDP






;rcx=table rdx=count r8d=mask r9=variant array (one byte per entry, 0=no tags)
;entry is 4 bytes, crc2^mask32, the mask comes from a separate boot domain
SetSigTable PROC
    test rcx, rcx
    jz sst_ret
    cmp edx, SIGMAX
    ja sst_ret                          ; unreasonable size -> stay disabled, fail closed
    mov qword ptr [g_pSigs], rcx
    mov dword ptr [g_sigCount], edx
    mov dword ptr [g_maskLo], r8d
    mov qword ptr [g_pVars], r9         ; 0 -> derive the variant instead
sst_ret:
jit_slot_0101::
    db 0Fh, 1Fh, 00h, 0Fh, 1Fh, 00h
    ret
SetSigTable ENDP



; vtable[0] must still be our CompileHook (exact address, not "anything != orig")
; return code names the failing check, 1 no table 2 moved 3 no original 4 slot holds the original again
VerifyJitHook PROC
    mov rax, [g_vtable]
    test rax, rax
    jz vj_c1
    mov rdx, [rax]                      ; vtable[0]
    lea rcx, CompileHook                ; must still point at our hook
    cmp rdx, rcx
    jne vj_c2
    mov rcx, [g_orig]                   ; original compileMethod
    test rcx, rcx
    jz vj_c3
    cmp rdx, rcx                        ; == original -> restored
    je vj_c4
    ; low byte 0 = healthy, the variant retry counters ride in the upper bytes
    ; so the boot can log them without a new export, the 1..4 codes stay
    mov eax, dword ptr [g_recover]
    shl eax, 8
    mov ecx, dword ptr [g_recoverFail]
    shl ecx, 16
    or eax, ecx
    ret
vj_c1:
    mov eax, 1
    ret
vj_c2:
    mov eax, 2
    ret
vj_c3:
    mov eax, 3
    ret
vj_c4:
jit_slot_0102::
    db 66h, 0Fh, 1Fh, 84h, 00h, 00h, 00h, 00h, 00h, 90h
    mov eax, 4
    ret
VerifyJitHook ENDP



; set by BootAntheil once AD() passes
SetAntiDebugFlag PROC
    mov dword ptr [g_adFlag], ecx
    ret
SetAntiDebugFlag ENDP



; set by BootAntheil, rcx = ICorStaticInfo.getMethodInfo vtable offset, rdx = canInline vtable offset
; eax = 1 accepted, 0 rejected with both slots cleared, only 8 byte aligned in range pairs are accepted
SetJitSlots PROC
    mov r9d, edx                        ; control bits are read before the offset is masked out
    and edx, 3FFh                       ; bit15 = leave canInline alone, bit14 = in place, bit13 = no callee
                                        ; scan (offset itself stays <= 0x200)
    test ecx, ecx
    jz sj_bad
    test edx, edx
    jz sj_bad
    test ecx, 7
    jnz sj_bad
    test edx, 7
    jnz sj_bad
    cmp ecx, 200h
    ja sj_bad
    cmp edx, 200h
    ja sj_bad
    mov dword ptr [g_gmiOff], ecx
    mov dword ptr [g_ciOff], edx
    mov dword ptr [g_ciMode], r8d
    test r9d, 8000h
    jz sj_ci
    mov dword ptr [g_skipCi], 1
sj_ci:
    test r9d, 2000h
    jz sj_scan
    mov dword ptr [g_noCiScan], 1
sj_scan:
    test r9d, 4000h
    jz sj_ok
    mov dword ptr [g_inPlace], 1
    mov dword ptr [g_cacheOn], 0        ; the cache hands back heap buffers, in place mode never uses them
    mov dword ptr [g_scrubOn], 0
sj_ok:
    mov eax, 1
    ret
sj_bad:
    mov dword ptr [g_gmiOff], 0
    mov dword ptr [g_ciOff], 0
    xor eax, eax
    ret
SetJitSlots ENDP



; fixed slots, the packer rewrites them in place
jit_slot_dir_n equ 102
jit_slot_dir:
    dd 0C4B1A7E3h, 05E2D90B0h
    dd jit_slot_dir_n
    dd jit_slot_0001 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0002 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0003 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0004 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0005 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0006 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0007 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0008 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0009 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0010 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0011 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0012 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0013 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0014 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0015 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0016 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0017 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0018 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0019 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0020 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0021 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0022 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0023 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0024 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0025 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0026 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0027 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0028 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0029 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0030 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0031 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0032 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0033 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0034 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0035 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0036 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0037 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0038 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0039 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0040 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0041 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0042 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0043 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0044 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0045 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0046 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0047 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0048 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0049 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0050 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0051 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0052 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0053 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0054 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0055 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0056 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0057 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0058 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0059 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0060 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0061 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0062 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0063 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0064 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0065 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0066 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0067 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0068 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0069 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0070 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0071 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0072 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0073 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0074 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0075 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0076 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0077 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0078 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0079 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0080 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0081 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0082 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0083 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0084 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0085 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0086 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0087 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0088 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0089 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0090 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0091 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0092 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0093 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0094 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0095 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0096 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0097 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0098 - jit_slot_dir, 8, 0, 0
    dd jit_slot_0099 - jit_slot_dir, 10, 0, 0
    dd jit_slot_0100 - jit_slot_dir, 12, 0, 0
    dd jit_slot_0101 - jit_slot_dir, 6, 0, 0
    dd jit_slot_0102 - jit_slot_dir, 10, 0, 0

.data

szVirtualAlloc    db "VirtualAlloc",0
szVirtualFree     db "VirtualFree",0
szVirtualProtect  db "VirtualProtect",0
szSetValid        db "SetProcessValidCallTargets",0
szGetModuleHandle db "GetModuleHandleA",0
szGetLastError   db "GetLastError",0
szKernelbase      db "kernelbase.dll",0
szClrJit          db "clrjit",0
szGetJit          db "getJit",0

    align 8

g_orig          dq 0                    ; original compileMethod
g_keyA          dq 0                    ; stream cipher key split A, key ^ SPLIT, 64 bit
g_keyB          dq 0                    ; stream cipher key split B, SPLIT, 64 bit

g_sigCount      dd 0
g_rdtsc_start   dd 0
g_varStart      dd 0                    ; starting candidate variant of the body being compiled, tag or formula

    align 8

g_pSigs         dq 0                    ; sig table pointer handed over by boot, 4 bytes per entry = crc2^mask32
g_pVars         dq 0                    ; variant bytes handed over by the boot, 1 per entry, 0 = derive it instead

    align 8
; handover table, filled at run time by JitHookTable, index k = export k+1
; only JitHookTable and InstallJitHook are exports, the five below go through
g_fnTab         dq 6 dup(0)
g_pCache        dq 0                    ; decrypt cache table, allocated on first use, 32 bytes per entry, ilCode + ilSize + crc after mask + scratch
g_pPool         dq 0                    ; scratch pool, allocated on first use, POOLN in flight slots + the per thread compile table

g_pMap          dq 0                    ; method -> handed out buffer map, allocated on first use, MAPN slots of MAP_SLOT bytes
g_pVirtualAlloc    dq 0
g_pVirtualFree     dq 0
g_pVirtualProtect  dq 0
g_pSetValid        dq 0                 ; SetProcessValidCallTargets makes the shim thunks valid under CFG
g_pLastErr         dq 0                 ; GetLastError (diagnostics for the CFG registration)
g_origGMA          dq 0                 ; reserved, getMethodAttribs original (slot varies by runtime)
g_origGmi dq 0                          ; original ICorJitInfo::getMethodInfo
g_origCanInline    dq 0                 ; original ICorJitInfo::canInline
g_gmiOff           dd 0                 ; ICorStaticInfo.getMethodInfo vtable byte offset, 0 = not set by the boot
g_ciOff            dd 0                 ; ICorStaticInfo.canInline vtable byte offset, 0 = hook stays off
g_skipCi           dd 0                 ; leave the canInline slot untouched (SetJitSlots bit15)
g_noCiScan         dd 0                 ; canInline hook returns the original decision (SetJitSlots bit13)
g_ciMode           dd 0                 ; canInline install mode (SetJitSlots third argument)
g_compLock         dd 0                 ; compile serialization lock
g_recover       dd 0                    ; body decrypts that only passed after trying another opcode variant
g_recoverFail   dd 0                    ; bodies where none of the four candidates looked like IL
                   db 0,0,0             ; align
                   db 0,0,0             ; align

    align 8

g_vtable           dq 0                 ; vtable for VerifyJitHook
g_adFlag           dd 0                 ; set by BootAntheil after AD passes
g_scrubOn          dd 1                 ; 1 = tiered off, 0 = recompiles
g_cacheOn          dd 1                 ; 1 = tiered off, 0 = reallocate
g_inPlace          dd 0                 ; decrypt into the image instead of a heap buffer (SetJitSlots 0x4000)
g_shimLock         dd 0                 ; shim table build lock, taken once by the first compile
g_shimTab          dq 0                 ; our ICorJitInfo table, one forwarding thunk per slot, 0 = not built
g_baseTab          dq 0                 ; read only copy of the runtime table the thunks jump through
g_pTrace           dq 0                 ; boot provided ring, count at +8, then 4 dwords per record
g_traceN           dd 0                 ; records written (capped at 8192)
SNAP_OFF           EQU 16 + 8192*16      ; tail area of the ring, il qword, size dword, 64 IL bytes

g_maskLo           dd 0                 ; sig mask low 32 bits, derived by boot in a separate domain and handed over via SetSigTable
g_variant          dd 0                 ; per method decrypt variant, CompileHook
g_permKey          dq 0                 ; last perm gen key
g_permHi           db 16 dup(0)         ; 4 bit high nibble perm
g_permLo           db 16 dup(0)         ; 4 bit low nibble perm
g_invHi            db 16 dup(0)         ; inverse perm high
g_invLo            db 16 dup(0)         ; inverse perm low

; per thread record of the buffer handed to the JIT, tid + il + scratch + len
g_scrubTab      dq 192 dup(0)

; per thread proxy object, tid + table + realComp + pad, what the JIT gets
; the { table, realComp } pair at offset 8, its first qword is the vtable

END