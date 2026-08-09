; lamarr_stub.asm - Native x64 Lamarr decoder + PE entry
; Clean-room from FORMAT.TXT.  ml64 /c lamarr_stub.asm

EXTERNDEF VirtualAlloc:proc
EXTERNDEF VirtualProtect:proc
EXTERNDEF ExitProcess:proc

M_COM  equ 1000h
M_RES  equ 2000h
P_EXRW equ 40h

L_DEF  equ 012h
L_1B   equ 0FFh + L_DEF
L_2B   equ 0FFFFh + L_1B

.code

NibRead MACRO
    LOCAL nb,nd
    test r12b, 1
    jz   nb
    mov  al, [rsi]
    shr  al, 4
    mov  ah, [rsi+1]
    shl  ah, 4
    or   al, ah
    inc  rsi
    jmp  nd
nb: lodsb
nd:
ENDM

Read20 MACRO
    LOCAL rl
    mov  eax, dword ptr [rsi]
    test r12b, 1
    jz   rl
    shr  eax, 4
rl: and  eax, 0FFFFFh
    inc  rsi
ENDM

lamarr_decode PROC EXPORT
    push rdi
    push rsi
    mov  rax, rdx
    mov  edx, [rax]
    xchg rdi, rcx
    xchg rsi, r8
    mov  ecx, r9d
    call lamarr_core
    mov  [rax], edx
    pop  rsi
    pop  rdi
    ret
lamarr_decode ENDP

lamarr_core PROC
    push rbx
    push rbp
    push r12
    push r13
    push r14
    push r15
    mov  r13, rcx
    mov  r14, rdx
    mov  r15, rdi
    xor  r12d, r12d
    lodsb
    stosb
    mov  r11d, 1
tl: mov  eax, r13d
    sub  eax, r12d
    cmp  rsi, rax
    jae  done
    NibRead
    mov  bl, 8
bitlp: cmp  rsi, r13
    jae  done
    cmp  r11d, r14d
    jae  done
    shl  al, 1
    jc   m
    NibRead
    stosb
    inc  r11d
    jmp  nb
m:  Read20
    cmp  r11d, 0881h
    jae  ld
    mov  ecx, eax
    shr  ecx, 1
    test al, 1
    jz   s0
    add  rsi, r12
    xor  r12b, 1
    and  ecx, 07FFh
    add  ecx, 081h
    jmp  gd
s0: and  ecx, 07Fh
    inc  ecx
    jmp  gd
ld: mov  ecx, eax
    shr  ecx, 2
    mov  edx, eax
    and  edx, 3
    cmp  edx, 0
    je   l00
    cmp  edx, 1
    je   l01
    cmp  edx, 2
    je   l10
    add  rsi, r12
    inc  rsi
    xor  r12b, 1
    and  ecx, 03FFFFh
    add  ecx, 04441h
    jmp  gd
l10:inc  rsi
    and  ecx, 03FFFh
    add  ecx, 0441h
    jmp  gd
l01:add  rsi, r12
    xor  r12b, 1
    and  ecx, 03FFh
    add  ecx, 041h
    jmp  gd
l00:and  ecx, 03Fh
    inc  ecx
gd: mov  r10d, ecx
    mov  dx, [rsi]
    test r12b, 1
    jz   ua
    shr  edx, 4
ua: and  edx, 0FFFh
    mov  r9d, edx
    add  rsi, r12
    xor  r12b, 1
    mov  eax, r9d
    and  eax, 0Fh
    cmp  eax, 0Fh
    jne  ls
    inc  rsi
    cmp  r9d, 0FFFh
    jne  lm
    test r12b, 1
    jz   un
    mov  eax, dword ptr [rsi]
    shr  eax, 4
    and  eax, 0FFFFh
    jmp  ud
un: movzx eax, word ptr [rsi]
ud: add  eax, L_1B
    add  rsi, 2
    mov  r9d, eax
    cmp  r9d, L_2B
    jne  lc
    test r12b, 1
    jz   cn0
    movzx ecx, byte ptr [rsi-4]
    and  ecx, 0FCh
    shl  ecx, 5
    inc  rsi
    xor  r12d, r12d
    jmp  cc
cn0:movzx ecx, word ptr [rsi-5]
    and  ecx, 0FC0h
    shl  ecx, 1
cc: and  al, 07Fh
    add  ecx, eax
    add  ecx, 4
    shl  ecx, 1
    mov  r9d, ecx
    rep  movs dword ptr [rdi], dword ptr [rsi]
    shl  r9d, 2
    add  r11d, r9d
    jmp  nt
ls: mov  r9d, eax
    add  r9d, 3
    jmp  lc
lm: shr  r9d, 4
    add  r9d, 012h
lc: cmp  r11d, r10d
    jb   ed
    mov  eax, r11d
    add  eax, r9d
    cmp  eax, r14d
    ja   eo
    mov  ecx, r9d
    lea  rbx, [r15+r11]
    sub  rbx, r10
copylp: mov  al, [rbx]
    stosb
    inc  rbx
    dec  ecx
    jnz  copylp
    add  r11d, r9d
nb: dec  bl
    jnz  bitlp
nt: jmp  tl
ed: mov  eax, 104h
    jmp  ex
eo: mov  eax, 111h
    jmp  ex
done:mov  rdx, r11
    xor  eax, eax
ex: pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbp
    pop  rbx
    ret
lamarr_core ENDP

StubEntry PROC FRAME
    push rbp
    .pushreg rbp
    mov  rbp, rsp
    .setframe rbp, 0
    push rbx
    .pushreg rbx
    push r12
    .pushreg r12
    push r13
    .pushreg r13
    push r14
    .pushreg r14
    push r15
    .pushreg r15
    push rdi
    .pushreg rdi
    push rsi
    .pushreg rsi
    sub  rsp, 40
    .allocstack 40
    .endprolog
    mov  r12, rcx
    mov  eax, [r12+3Ch]
    lea  r13, [r12+rax]
    cmp  word ptr [r13], 4550h
    jne  fail
    movzx ecx, word ptr [r13+14h]
    lea  r14, [r13+18h+rcx]
    movzx ebx, word ptr [r13+6]
sl: cmp  ebx, 0
    je   fail
    cmp  dword ptr [r14], 'zdl.'
    jne  nxs
    cmp  dword ptr [r14+4], 'ata'
    je   fnd
nxs:add  r14, 40
    dec  ebx
    jmp  sl
fnd:mov  r15d, [r14+12]
    add  r15, r12
    mov  ebx, [r14+8]
    lea  rax, [r13+18h+38h]
    mov  r13d, [rax]
    lea  rax, [rax-20h]
    mov  r14, [rax]
    xor  ecx, ecx
    mov  edx, M_COM or M_RES
    mov  r8d, r13d
    xor  r9d, r9d
    mov  [rsp+32], rcx
    call VirtualAlloc
    test rax, rax
    jz   fail
    mov  rdi, rax
    mov  eax, [r12+3Ch]
    lea  rcx, [r12+rax+18h+3Ch]
    mov  ecx, [rcx]
    mov  rsi, r12
    rep  movsb
    mov  rsi, r15
    mov  ecx, ebx
    push rdi
    mov  edx, r13d
    call lamarr_core
    test eax, eax
    pop  rdi
    jnz  fail
    mov  rax, rdi
    sub  rax, r14
    jz   skr
    mov  eax, [r12+3Ch]
    lea  rcx, [r12+rax+18h]
    movzx edx, word ptr [rcx-18h+14h]
    lea  rsi, [rcx+rdx]
    movzx ecx, word ptr [rcx-18h+6]
sr: cmp  dword ptr [rsi], 'ler.'
    jne  nr
    cmp  dword ptr [rsi+4], 'co'
    je   fr
nr: add  rsi, 40
    dec  ecx
    jnz  sr
    jmp  skr
fr: mov  esi, [rsi+12]
    add  rsi, rdi
    mov  ecx, [rsi-12+8]
rk: cmp  ecx, 8
    jb   skr
    mov  r8d, [rsi]
    mov  r9d, [rsi+4]
    sub  r9d, 8
    shr  r9d, 1
    add  rsi, 8
    sub  ecx, r9d
    sub  ecx, r9d
    sub  ecx, 8
rl: mov  dx, [rsi]
    mov  r10w, dx
    and  r10w, 0FFFh
    shr  dx, 12
    cmp  dx, 0Ah
    jne  rn
    add  r10d, r8d
    add  [rdi+r10], rax
rn: add  rsi, 2
    dec  r9d
    jnz  rl
    jmp  rk
skr:mov  [rsp+32], rsi
    lea  r9, [rsp+32]
    mov  r8d, P_EXRW
    mov  edx, r13d
    mov  rcx, rdi
    call VirtualProtect
    mov  eax, [r12+3Ch]
    lea  rcx, [r12+rax+18h+10h]
    mov  eax, [rcx]
    add  rax, rdi
    jmp  rax
fail:mov  ecx, 1
    call ExitProcess
StubEntry ENDP

END