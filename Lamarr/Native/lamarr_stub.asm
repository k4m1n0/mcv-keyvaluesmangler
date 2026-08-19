M_COM  equ 1000h
M_RES  equ 2000h
P_EXRW equ 40h
P_RW   equ 4

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



lamarr_core PROC
    push rbx
    push rbp
    push r12
    push r13
    push r14
    push r15
    sub  rsp, 8              ; bit counter, not bl (rbx used by copy)
    mov  r13, rcx
    add  r13, rsi
    mov  r14, rdx
    mov  r15, rdi
    xor  r12d, r12d
    lodsb
    stosb
    mov  r11d, 1
tl: mov  rax, r13
    sub  rax, r12
    cmp  rsi, rax
    jae  done
    ; tag in rbp, not al (al used by literal)
    test r12b, 1
    jz   t0
    mov  al, [rsi]
    shr  al, 4
    mov  ah, [rsi+1]
    shl  ah, 4
    or   al, ah
    inc  rsi
    jmp  t1
t0: lodsb
t1: shl  rax, 56         ; shl rbp,1 yields CF in bit7->0 order
    mov  rbp, rax
    mov  byte ptr [rsp], 8
bitlp:
    mov  rax, r13
    sub  rax, r12
    cmp  rsi, rax
    jae  done
    cmp  r11d, r14d
    jae  done
    shl  rbp, 1
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
l10:
    inc  rsi
    and  ecx, 03FFFh
    add  ecx, 0441h
    jmp  gd
l01:
    add  rsi, r12
    xor  r12b, 1
    and  ecx, 03FFh
    add  ecx, 041h
    jmp  gd
l00:
    and  ecx, 03Fh
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
cn0:
    movzx ecx, word ptr [rsi-5]
    and  ecx, 0FC0h
    shl  ecx, 1
cc: mov  rax, rbp         ; chunk length low bits in tag
    shr  rax, 57          ; rbp already shifted once
    and  al, 07Fh
    add  ecx, eax
    add  ecx, 4
    shl  ecx, 1
    mov  r9d, ecx
    ; bounds: outPos + chunkBytes <= dstLen and rsi + chunkBytes <= srcEnd
    shl  r9, 2             ; r9 = chunk byte count
    mov  eax, r11d
    add  eax, r9d
    cmp  eax, r14d
    ja   eo
    mov  rax, r13
    sub  rax, rsi
    cmp  rax, r9
    jb   eo
    rep  movs dword ptr [rdi], dword ptr [rsi]
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
copylp:
    mov  al, [rbx]
    stosb
    inc  rbx
    dec  ecx
    jnz  copylp
    add  r11d, r9d
nb: dec  byte ptr [rsp]
    jnz  bitlp
nt: jmp  tl
ed: mov  eax, 104h
    jmp  ex
eo: mov  eax, 111h
    jmp  ex
done:
    mov  rdx, r11
    xor  eax, eax
ex: add  rsp, 8
    pop  r15
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
    ; x64 entry: rcx != module base
    lea  rcx, szGetModuleHandleW
    call ResolveApi
    test rax, rax
    jz   fail
    xor  ecx, ecx
    call rax
    test rax, rax
    jz   fail
    mov  r12, rax
    mov  eax, [r12+3Ch]
    lea  r13, [r12+rax]
    cmp  word ptr [r13], 4550h
    jne  fail
    movzx ecx, word ptr [r13+14h]
    lea  r14, [r13+18h+rcx]
    movzx ebx, word ptr [r13+6]
sl: cmp  ebx, 0
    je   fail
    cmp  dword ptr [r14], 'mal.'
    jne  nxs
    cmp  dword ptr [r14+4], 00727261h
    je   fnd
nxs:
    add  r14, 40
    dec  ebx
    jmp  sl
fnd:
    mov  r15d, [r14+12]
    add  r15, r12
    mov  ebx, [r14+8]
    lea  rax, [r13+18h+38h]
    mov  r13d, [rax]            ; SizeOfImage covers decompressed apphost
    lea  rcx, szVirtualAlloc
    call ResolveApi
    test rax, rax
    jz   fail
    xor  ecx, ecx
    mov  edx, r13d
    mov  r8d, M_COM or M_RES
    mov  r9d, P_RW
    mov  [rsp+32], rcx
    call rax
    test rax, rax
    jz   fail
    mov  rdi, rax
    mov  rsi, r15
    mov  ecx, ebx
    push rdi
    mov  edx, r13d
    call lamarr_core
    test eax, eax
    pop  rdi
    jnz  fail
    mov  eax, [rdi+3Ch]
    mov  r14, [rdi+rax+18h+18h]
    mov  rax, rdi
    sub  rax, r14
    mov  r15, rax
    jz   impres
    mov  eax, [rdi+3Ch]
    lea  rcx, [rdi+rax+18h]
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
    jmp  impres
fr: mov  ecx, [rsi+8]
    mov  esi, [rsi+12]
    add  rsi, rdi
rk: cmp  ecx, 8
    jb   impres
    mov  r8d, [rsi]
    mov  r9d, [rsi+4]
    sub  r9d, 8
    shr  r9d, 1
    add  rsi, 8
    sub  ecx, r9d
    sub  ecx, r9d
    sub  ecx, 8
rl: movzx r10d, word ptr [rsi]  ; zero-extend: mov r10w leaves bits 12-31 stale
    mov  r11d, r10d
    shr  r11d, 12
    and  r10d, 0FFFh
    cmp  r11d, 0Ah
    jne  rn
    add  r10d, r8d
    add  [rdi+r10], r15
rn: add  rsi, 2
    dec  r9d
    jnz  rl
    jmp  rk
impres:
    lea  rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz   fail
    mov  rbx, rax
    lea  rcx, szGetProcAddress
    call ResolveApi
    test rax, rax
    jz   fail
    mov  r14, rax
    lea  rcx, szVirtualProtect
    call ResolveApi
    test rax, rax
    jz   fail
    mov  r15, rax
    mov  rcx, rdi
    mov  rdx, rbx
    mov  r8,  r14
    call ResolveImports
skr:
    mov  [rsp+32], rsi
    lea  r9, [rsp+32]
    mov  r8d, P_EXRW
    mov  edx, r13d
    mov  rcx, rdi
    call r15
    test rax, rax
    jz   fail
    ; record apphost image bounds for the VEH filter
    lea  rax, vehBase
    mov  [rax], rdi
    mov  [rax+8], r13d
    ; VEH installed here (before RegisterModule/TlsInit) so faults in
    ; those stages are covered too; ~50% heap corruption on exit without it
    lea  rcx, szAddVectoredExceptionHandler
    call ResolveApi
    test rax, rax
    jz   fail
    xor  ecx, ecx
    lea  rdx, VectoredHandler
    call rax
    mov  [rsp+38h], rdi
    call RegisterModule
    test rax, rax
    jz   fail
    mov  rdi, [rsp+38h]
    ; manual map: _tls_index uninitialized, UCRT reads garbage
    mov  [rsp+38h], rdi
    call TlsInit
    test rax, rax
    jz   fail
    mov  rdi, [rsp+38h]
    ; register .pdata for exception unwind
    lea  rcx, szRtlAddFunctionTable
    lea  rdx, szNtdll
    call ResolveApiIn
    test rax, rax
    jz   fail
    mov  rbx, rax
    mov  eax, [rdi+3Ch]
    mov  r10d, [rdi+rax+18h+70h+18h]
    mov  r11d, [rdi+rax+18h+70h+18h+4]
    add  r10, rdi
    xor  edx, edx
    mov  eax, r11d
    mov  ecx, 12
    div  ecx
    mov  rcx, r10
    mov  edx, eax
    mov  r8,  rdi
    call rbx
    mov  eax, [rdi+3Ch]
    lea  rcx, [rdi+rax+18h+10h]
    mov  eax, [rcx]
    add  rax, rdi
    sub  rsp, 8             ; align: entry expects rsp%16=8 (call convention)
    jmp  rax
fail:
    lea  rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz   fexit
    mov  ecx, 1
    call rax
fexit:
    mov  eax, 1
    add  rsp, 40
    pop  rsi
    pop  rdi
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbx
    pop  rbp
    ret
StubEntry ENDP



; Only swallow access violations / heap-corruption raised OUTSIDE the
; apphost image (the exit-time cleanup faults), and at most a bounded
; number of times. Genuine faults inside the apphost (or unrelated
; exceptions like breakpoints, stack overflow) fall through to the
; normal handler and crash cleanly instead of spinning on a re-executed
; faulting instruction

VectoredHandler PROC
    test rcx, rcx
    jz   vh_search
    mov  rax, [rcx]          ; rax = PEXCEPTION_RECORD
    test rax, rax
    jz   vh_search
    mov  ecx, [rax]          ; ExceptionCode
    cmp  ecx, 0C0000005h     ; EXCEPTION_ACCESS_VIOLATION
    je   vh_chk
    cmp  ecx, 0C0000374h     ; STATUS_HEAP_CORRUPTION (exit-time cleanup)
    jne  vh_search
vh_chk:
    lea  r8, vehBase
    mov  r9, [r8]            ; apphost base
    test r9, r9
    jz   vh_search           ; bounds not recorded: never swallow
    mov  rdx, [rax+10h]      ; ExceptionAddress
    cmp  rdx, r9
    jb   vh_swallow          ; below image: outside -> swallow
    mov  r10, [r8+8]         ; image size
    test r10, r10
    jz   vh_search
    add  r9, r10
    cmp  rdx, r9
    jae  vh_swallow          ; above image end: outside -> swallow
    ; inside the apphost image: real bug, let it crash
vh_search:
    xor  eax, eax            ; EXCEPTION_CONTINUE_SEARCH
    ret
vh_swallow:
    mov  rax, [r8+10h]       ; swallow counter
    cmp  rax, 8
    jae  vh_search           ; too many: give up, crash normally
    inc  rax
    mov  [r8+10h], rax
    or   rax, -1             ; EXCEPTION_CONTINUE_EXECUTION
    ret
VectoredHandler ENDP

    align 8
vehBase  dq 0                ; decompressed apphost image base
vehSize  dq 0                ; image size (bytes)
vehCount dq 0                ; swallows so far (bounded)



RegisterModule PROC
    push rbp
    push rbx
    push r12
    push r13
    push r14
    push r15
    sub  rsp, 28h

    mov  rbp, rdi
    mov  r14d, r13d

    lea  rcx, szVirtualAlloc
    call ResolveApi
    test rax, rax
    jz   rm_fail
    xor  ecx, ecx
    mov  edx, 400h
    mov  r8d, M_COM or M_RES
    mov  r9d, P_RW
    mov  [rsp+32], rcx
    call rax
    test rax, rax
    jz   rm_fail
    mov  r15, rax
    mov  rdi, rax
    xor  eax, eax
    mov  ecx, 80h
    rep  stosq

    mov  [r15+30h], rbp
    mov  [r15+40h], r14d
    mov  eax, [rbp+3Ch]
    lea  rcx, [rbp+rax+18h+10h]
    mov  eax, [rcx]
    add  rax, rbp
    mov  [r15+38h], rax
    mov  eax, [rbp+3Ch]
    mov  eax, [rbp+rax+8]
    mov  [r15+80h], eax

    lea  rcx, szGetModuleFileNameW
    call ResolveApi
    test rax, rax
    jz   rm_fail
    mov  rbx, rax
    xor  ecx, ecx
    lea  rdx, [r15+100h]
    mov  r8d, 260
    call rbx
    test rax, rax
    jz   rm_fail
    mov  r12d, eax
    lea  rsi, [r15+100h]
    xor  edx, edx
    dec  edx
    xor  ecx, ecx
rm_ps:
    cmp  ecx, r12d
    jae  rm_pd
    movzx r8d, word ptr [rsi+rcx*2]
    cmp  r8d, 5Ch
    jne  rm_pn
    mov  edx, ecx
rm_pn:
    inc  ecx
    jmp  rm_ps
rm_pd:
    mov  eax, r12d
    add  eax, eax
    mov  [r15+48h], ax
    mov  word ptr [r15+4Ah], 208h
    lea  rax, [r15+100h]
    mov  [r15+50h], rax
    test edx, edx
    js   rm_uf
    mov  eax, r12d
    sub  eax, edx
    sub  eax, 1
    mov  [r15+58h], ax
    add  eax, eax
    mov  [r15+5Ah], ax
    lea  rax, [r15+100h+rdx*2+2]
    mov  [r15+60h], rax
    jmp  rm_ls
rm_uf:
    mov  eax, r12d
    add  eax, eax
    mov  [r15+58h], ax
    mov  word ptr [r15+5Ah], 208h
    lea  rax, [r15+100h]
    mov  [r15+60h], rax
rm_ls:
    mov  rax, gs:[60h]
    mov  rax, [rax+18h]
    mov  r13, rax
    mov  rax, [r13+10h]
    mov  [r15+00h], rax
    lea  rcx, [r13+10h]
    mov  [r15+08h], rcx
    mov  [rax+08h], r15
    mov  [r13+10h], r15
    mov  rax, [r13+20h]
    mov  [r15+10h], rax
    lea  rcx, [r13+20h]
    mov  [r15+18h], rcx
    lea  rdx, [r15+10h]
    mov  [rax+08h], rdx
    mov  [r13+20h], rdx
    mov  rax, [r13+30h]
    mov  [r15+20h], rax
    lea  rcx, [r13+30h]
    mov  [r15+28h], rcx
    lea  rdx, [r15+20h]
    mov  [rax+08h], rdx
    mov  [r13+30h], rdx
    ; self-loop: TLS callback runner dereferences these
    lea  rax, [r15+98h]
    mov  [r15+98h], rax
    mov  [r15+0A0h], rax
    lea  rax, [r15+0A8h]
    mov  [r15+0A8h], rax
    mov  [r15+0B0h], rax
    lea  rax, [r15+0B8h]
    mov  [r15+0B8h], rax
    mov  [r15+0C0h], rax

    mov  eax, 1
    jmp  rm_dn
rm_fail:
    xor  eax, eax
rm_dn:
    add  rsp, 28h
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbx
    pop  rbp
    ret
RegisterModule ENDP



TlsInit PROC
    push rbx
    push r12
    push r13
    push r14
    push r15
    sub  rsp, 30h
    mov  r12, rdi
    mov  eax, [r12+3Ch]
    mov  r13d, [r12+rax+18h+70h+48h]
    test r13d, r13d
    jz   tl_fail
    add  r13, r12
    ; TLS dir VA fields fixed by .reloc
    lea  rcx, szTlsAlloc
    call ResolveApi
    test rax, rax
    jz   tl_fail
    mov  [rsp+28h], rax
    mov  rax, [rsp+28h]
    call rax
    test eax, eax
    jz   tl_fail
    mov  r14d, eax
    mov  rax, [r13+10h]
    mov  [rax], r14d
    mov  eax, [r13+8]
    sub  eax, [r13]
    add  eax, [r13+20h]
    mov  [rsp+20h], eax
    lea  rcx, szVirtualAlloc
    call ResolveApi
    test rax, rax
    jz   tl_fail
    mov  edx, [rsp+20h]
    xor  ecx, ecx
    mov  r8d, M_COM or M_RES
    mov  r9d, P_RW
    mov  [rsp+32], rcx
    call rax
    test rax, rax
    jz   tl_fail
    mov  r15, rax
    mov  rsi, [r13]
    mov  rdi, r15
    mov  ecx, [r13+8]
    sub  ecx, [r13]
    rep  movsb
    lea  rcx, szTlsSetValue
    call ResolveApi
    test rax, rax
    jz   tl_fail
    mov  ecx, r14d
    mov  rdx, r15
    call rax
    test rax, rax
    jz   tl_fail
    mov  eax, 1
    jmp  tl_dn
tl_fail:
    xor  eax, eax
tl_dn:
    add  rsp, 30h
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbx
    ret
TlsInit ENDP



ResolveApi PROC
    push rbx
    push rsi
    push rdi
    push r12

    mov  rax, gs:[60h]
    mov  rax, [rax+18h]
    lea  rbx, [rax+20h]
    mov  rax, [rbx]
kl: cmp  rax, rbx
    je   rfail
    mov  rdx, [rax+50h]
    mov  r8,  [rdx]
    mov  r9,  [rdx+8]
    mov  r10, 006E00720065006Bh
    mov  r11, 00320033006C0065h
    cmp  r8, r10
    jne  kup
    cmp  r9, r11
    je   got
kup:
    mov  r10, 004E00520045004Bh
    mov  r11, 00320033004C0045h
    cmp  r8, r10
    jne  nxt
    cmp  r9, r11
    je   got
nxt:
    mov  rax, [rax]
    jmp  kl
got:
    mov  rdx, [rax+20h]

    mov  rdi, rcx
    jmp  ra_exp
ResolveApi ENDP



ResolveApiIn PROC
    push rbx
    push rsi
    push rdi
    push r12

    mov  r12, rcx
    mov  rdi, rdx
    xor  r9d, r9d
ra_nl:
    mov  al, [rdi+r9]
    test al, al
    jz   ra_nl2
    inc  r9
    jmp  ra_nl
ra_nl2:
    mov  rax, gs:[60h]
    mov  rax, [rax+18h]
    lea  rbx, [rax+20h]
    mov  rax, [rbx]
ra_wl:
    cmp  rax, rbx
    je   rfail
    mov  rsi, [rax+50h]
    xor  r8d, r8d
ra_wm:
    cmp  r8, r9
    jae  ra_wg
    movzx r10d, byte ptr [rdi+r8]
    movzx r11d, word ptr [rsi+r8*2]
    cmp  r10d, 61h
    jb   ra_u1
    sub  r10d, 20h
ra_u1:
    cmp  r11d, 61h
    jb   ra_u2
    sub  r11d, 20h
ra_u2:
    cmp  r10d, r11d
    jne  ra_wn
    inc  r8
    jmp  ra_wm
ra_wn:
    mov  rax, [rax]
    jmp  ra_wl
ra_wg:
    mov  rdx, [rax+20h]
    mov  rdi, r12
    jmp  ra_exp
ResolveApiIn ENDP



ra_exp:
    mov  eax, [rdx+3Ch]
    lea  r8,  [rdx+rax]
    mov  eax, [r8+88h]
    test eax, eax
    jz   rfail
    add  rax, rdx
    mov  r8,  rax
    mov  ebx, [r8+18h]
    test ebx, ebx
    jz   rfail
    mov  esi, [r8+20h]
    add  rsi, rdx
    mov  r11d, [r8+24h]
    add  r11, rdx
    xor  r10d, r10d
nml:
    cmp  r10d, ebx
    jae  rfail
    mov  eax, [rsi]
    lea  r9,  [rdx+rax]
    mov  rcx, rdi
cmpl:
    mov  al, [rcx]
    test al, al
    jnz  c2
    cmp  byte ptr [r9], 0       ; both must end (prevent VirtualAllocEx false match)
    je   mok
    jmp  mnx
c2: cmp  al, [r9]
    jne  mnx
    inc  rcx
    inc  r9
    jmp  cmpl
mnx:
    add  rsi, 4
    inc  r10d
    jmp  nml
mok:
    movzx eax, word ptr [r11+r10*2]
    mov  ecx, [r8+1Ch]
    add  rcx, rdx
    mov  eax, [rcx+rax*4]

    ; forwarder: function RVA in export dir = "DLL.Func" string
    mov  r9d, [rdx+3Ch]
    lea  r9, [rdx+r9+18h+70h]
    mov  r10d, [r9]
    mov  r9d, [r9+4]
    cmp  eax, r10d
    jb   rfok
    add  r9d, r10d
    cmp  eax, r9d
    ja   rfok

    lea  rdi, [rdx+rax]
    mov  rsi, rdi
fdl:
    mov  al, [rsi]
    inc  rsi
    cmp  al, '.'
    jne  fdl
    mov  r12, rsi
    sub  r12, rdi
    sub  r12, 1

    mov  rax, gs:[60h]
    mov  rax, [rax+18h]
    lea  rbx, [rax+20h]
    mov  rax, [rbx]
fwl:
    cmp  rax, rbx
    je   rfail
    mov  r8, [rax+50h]
    xor  r9d, r9d
fmc:
    cmp  r9, r12
    jae  fgot
    movzx r10d, byte ptr [rdi+r9]
    movzx r11d, word ptr [r8+r9*2]
    cmp  r10d, 61h
    jb   fup1
    sub  r10d, 20h
fup1:
    cmp  r11d, 61h
    jb   fup2
    sub  r11d, 20h
fup2:
    cmp  r10d, r11d
    jne  fnext
    inc  r9
    jmp  fmc
fnext:
    mov  rax, [rax]
    jmp  fwl
fgot:
    mov  rdx, [rax+20h]
    mov  rdi, rsi

    mov  eax, [rdx+3Ch]
    lea  r8, [rdx+rax]
    mov  eax, [r8+88h]
    test eax, eax
    jz   rfail
    add  rax, rdx
    mov  r8, rax
    mov  ebx, [r8+18h]
    test ebx, ebx
    jz   rfail
    mov  esi, [r8+20h]
    add  rsi, rdx
    mov  r11d, [r8+24h]
    add  r11, rdx
    xor  r10d, r10d
    jmp  nml

rfok:
    add  rax, rdx
    pop  r12
    pop  rdi
    pop  rsi
    pop  rbx
    ret
rfail:
    xor  eax, eax
    pop  r12
    pop  rdi
    pop  rsi
    pop  rbx
    ret



szVirtualAlloc   db "VirtualAlloc",0
szTlsAlloc       db "TlsAlloc",0
szTlsSetValue    db "TlsSetValue",0
szVirtualProtect db "VirtualProtect",0
szExitProcess    db "ExitProcess",0
szLoadLibraryA   db "LoadLibraryA",0
szGetProcAddress db "GetProcAddress",0
szGetModuleHandleW db "GetModuleHandleW",0
szGetModuleFileNameW db "GetModuleFileNameW",0
szRtlAddFunctionTable db "RtlAddFunctionTable",0
szAddVectoredExceptionHandler db "AddVectoredExceptionHandler",0
szNtdll          db "ntdll",0



ResolveImports PROC
    push rbp
    push rsi
    push rdi
    push rbx
    push r12
    push r13
    push r14
    push r15
    sub  rsp, 28h             ; home space below pushed regs
    mov  rdi, rcx
    mov  r12, rdx
    mov  r13, r8
    mov  eax, [rdi+3Ch]
    lea  rax, [rdi+rax]
    mov  eax, [rax+90h]
    test eax, eax
    jz   rdun
    add  rax, rdi
    mov  rsi, rax
rdp:
    mov  eax, [rsi+0Ch]
    test eax, eax
    jz   rdun
    mov  ecx, [rsi]
    mov  edx, [rsi+10h]
    test ecx, ecx
    jnz  rlu
    mov  ecx, edx
rlu:
    lea  r14, [rdi+rcx]
    lea  r15, [rdi+rdx]
    lea  rcx, [rdi+rax]
    call r12
    test rax, rax
    jz   rsk
    mov  rbx, rax
rfl:
    mov  rax, [r14]
    test rax, rax
    jz   rsk
    mov  rcx, rbx
    test rax, rax
    js   ordl
    lea  rdx, [rdi+rax+2]
    jmp  rgp
ordl:
    and  eax, 0FFFFh
    mov  rdx, rax
rgp:
    call r13
    test rax, rax
    jz   rnx
    mov  [r15], rax
rnx:
    add  r14, 8
    add  r15, 8
    jmp  rfl
rsk:
    add  rsi, 20
    jmp  rdp
rdun:
    add  rsp, 28h
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbx
    pop  rdi
    pop  rsi
    pop  rbp
    ret
ResolveImports ENDP

END