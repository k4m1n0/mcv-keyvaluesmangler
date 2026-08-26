.DATA

g_orig          dq 0                    ; original compileMethod
g_key           dd 0                    ; stream cipher key
g_sigCount      dd 0
g_decryptCount  dd 0
g_rdtsc_start   dd 0
ALIGN 8
g_sigs          dd 4096 dup(0)          ; CRC32 table of encrypted method bodies

g_pVirtualAlloc     dq 0
g_pVirtualFree      dq 0
g_pVirtualProtect   dq 0
g_origGMA           dq 0                ; original ICorJitInfo::getMethodAttribs (slot1)
g_origCanInline     dq 0                ; original ICorJitInfo::canInline (slot6)
g_compLock          dd 0                ; compile serialization lock (reserved, unused)

.CODE

PUBLIC g_orig, g_key, g_sigs, g_sigCount, g_decryptCount
PUBLIC InstallJitHook, SetJitHookKey, AddPayloadSig, GetJitHookDecryptCount

szVirtualAlloc     db 'VirtualAlloc',0
szVirtualFree      db 'VirtualFree',0
szVirtualProtect   db 'VirtualProtect',0
szClrJit           db 'clrjit',0
szGetJit           db 'getJit',0

; ResolveApi: rcx=ansi apiName -> rax=addr (kernel32, handles forwarders)
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
    ; shared export lookup: rdx=module base, rdi=name (ansi)
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
; Crc32Fn: rcx=ptr rdx=len -> eax
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

; XorDecrypt: rcx=dst rdx=src r8d=len, uses g_key
; !! length-preserving stream cipher, matches EncryptTool: s=s*0x9E3779B1+0x9747B28C take s>>24
XorDecrypt PROC
    test r8d, r8d
    jz xd_done
    mov eax, dword ptr [g_key]          ; s=key
    xor r9, r9                          ; i=0
xd_loop:
    mov r11d, 9E3779B1h
    imul eax, r11d
    add eax, 9747B28Ch
    mov r10d, eax
    shr r10d, 24
    movzx r11d, byte ptr [rdx+r9]
    xor r11d, r10d
    mov byte ptr [rcx+r9], r11b
    inc r9
    cmp r9, r8
    jb xd_loop
xd_done:
    ret
XorDecrypt ENDP

; single-step slowdown detection via RDTSC
RdtscStart PROC
    push rax
    push rdx
    rdtsc
    mov dword ptr [g_rdtsc_start], eax
    pop rdx
    pop rax
    ret
RdtscStart ENDP
RdtscCheck PROC
    push rax
    push rdx
    rdtsc
    sub eax, dword ptr [g_rdtsc_start]
    cmp eax, 400000000
    jb rc_ok
    ud2
rc_ok:
    pop rdx
    pop rax
    ret
RdtscCheck ENDP

; FindClrJit: -> rax = clrjit DllBase or 0 (PEB walk)
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
    mov r11, 00740069h                  ; "it"  wide LE
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

; FindExportInModule: rcx=base rdx=ansiName -> rax=addr or 0
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

; CompileHook: vtable slot0 replacement
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

    xor r15, r15                        ; scratch=0
    mov rbx, rcx                        ; self
    mov r12, rdx                        ; comp
    mov r13, r8                         ; info
    mov r14d, r9d                       ; flags


    ; !! hook ICorJitInfo vtable slot6 (canInline) on first compile
    ; JIT inline analysis reads callee IL from raw image (ciphertext)
    ; -> parses garbage -> broken codegen -> crash
    ; return INLINE_NEVER(-2) for encrypted callee to block inline IL reads
    mov rax, [r12]                      ; ICorJitInfo vtable
    test rax, rax
    jz gmi_skip
    cmp qword ptr [g_origCanInline], 0
    jne gmi_skip
    mov rcx, [rax+30h]                  ; slot6 = canInline
    xor eax, eax
    lock cmpxchg qword ptr [g_origCanInline], rcx
    test rax, rax
    jnz gmi_skip
    mov rax, [g_pVirtualProtect]
    test rax, rax
    jz gmi_skip
    mov rcx, [r12]
    lea rcx, [rcx+30h]
    mov edx, 8
    mov r8d, 4                          ; PAGE_READWRITE
    lea r9, [rbp-30h]
    call rax
    test rax, rax
    jz gmi_skip
    mov rcx, [r12]
    lea rax, GetCanInlineHook
    mov [rcx+30h], rax                  ; vtable[6]=GetCanInlineHook
    mov rax, [g_pVirtualProtect]
    mov rcx, [r12]
    lea rcx, [rcx+30h]
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
    mov ecx, eax                        ; ciphertext CRC
    lea rax, g_sigs                     ; sig table
    xor edx, edx                        ; i=0
ch_sig:
    cmp edx, dword ptr [g_sigCount]
    jae ch_orig
    mov r8d, [rax+rdx*4]
    cmp r8d, ecx
    je ch_payload
    inc edx
    jmp ch_sig

ch_payload:
    lock inc dword ptr [g_decryptCount]
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
    mov rcx, r15
    mov rdx, rsi
    mov r8d, edi
    call RdtscStart
    call XorDecrypt
    call RdtscCheck
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

; GetCanInlineHook: ICorJitInfo vtable slot6 replacement
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
    mov rax, [rax+20h]                  ; slot4 getMethodInfo
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
    mov rax, [rax+20h]
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
    xor edx, edx
gci_sig:
    cmp edx, dword ptr [g_sigCount]
    jae gci_ret
    mov r8d, [rax+rdx*4]
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

; InstallJitHook: PEB find clrjit, getJit, replace vtable slot0
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
    mov dword ptr [g_key], ecx
    ret
SetJitHookKey ENDP

AddPayloadSig PROC
    mov eax, dword ptr [g_sigCount]
    cmp eax, 4096
    jae sig_full
    lea r8, g_sigs
    mov [r8+rax*4], ecx
    inc dword ptr [g_sigCount]
sig_full:
    ret
AddPayloadSig ENDP

GetJitHookDecryptCount PROC
    mov eax, dword ptr [g_decryptCount]
    ret
GetJitHookDecryptCount ENDP

END