.code

StubEntry PROC FRAME
    push rbp
    .pushreg rbp
    mov rbp, rsp
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
    sub rsp, 40
    .allocstack 40
    .endprolog
    ; jump straight to hostfxr_main, skip apphost mapping
    jmp hostfxr_main_direct
StubEntry ENDP



; !! call hostfxr_main_bundle_startupinfo directly
; host_path = GetModuleFileNameW(NULL)
; dotnet_root = gDotnetRootW
; app_path = gAppPathW, header_offset = gHeaderOff
hostfxr_main_direct PROC
    sub rsp, 800h
    lea rcx, szGetModuleFileNameW       ; GetModuleFileNameW(NULL)
    call ResolveApi
    test rax, rax
    jz hmd_fail
    mov r12, rax
    lea rdx, [rsp+40h]                  ; buf
    mov r8d, 512                        ; bufsize
    xor ecx, ecx
    call r12
    test rax, rax
    jz hmd_fail

    ; !! build app_path = <host dir>\<app name> at runtime (exe may be relocated)
    lea rdi, gAppPathW
    lea rsi, [rsp+40h]                  ; host_path from GetModuleFileNameW
    call StrCpyW                        ; gAppPathW = host_path
    lea rdi, gAppPathW
    xor r9, r9                          ; r9 = last '\' + 2 (filename start)
bap_scan:
    ;%poly:junk
    ;%poly:end
    movzx ecx, word ptr [rdi]
    test ecx, ecx
    jz bap_got
    cmp ecx, 5Ch                        ; '\'
    jne bap_next
    lea r9, [rdi+2]
bap_next:
    ;%poly:junk
    ;%poly:end
    add rdi, 2
    jmp bap_scan
bap_got:
    ;%poly:junk
    ;%poly:end
    test r9, r9
    jz bap_plain
    mov rdi, r9
bap_plain:
    ;%poly:junk
    ;%poly:end
    lea rsi, gAppNameW
    call StrCpyW                        ; overwrite filename with main DLL name

    call ResolveDotnetRoot              ; find dotnet root
    call EnumFxrBestMatch               ; find hostfxr

    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz hmd_fail
    mov r12, rax
    lea rcx, gHostfxrA                  ; hostfxr.dll path
    call r12
    test rax, rax
    jz hmd_fail
    mov r13, rax                        ; r13 = hostfxr

    lea rcx, szGetProcAddress           ; get hostfxr_main_bundle
    call ResolveApi
    test rax, rax
    jz hmd_fail
    mov r12, rax
    mov rcx, r13
    lea rdx, szHostfxrMainBundle
    call r12
    test rax, rax
    jz hmd_fail
    mov r14, rax

    ; argv comes from GetCommandLineW + CommandLineToArgvW
    ; fallback to argc=1 on failure
    lea rcx, szGetCommandLineW
    call ResolveApi                     ; kernel32 direct export
    test rax, rax
    jz sc_argc1
    mov r15, rax
    call r15
    test rax, rax
    jz sc_argc1
    mov r13, rax
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz sc_argc1
    mov r12, rax
    lea rcx, szShell32
    call r12                            ; LoadLibraryA("shell32.dll")
    test rax, rax
    jz sc_argc1
    mov rbx, rax
    lea rcx, szGetProcAddress
    call ResolveApi
    test rax, rax
    jz sc_argc1
    mov r12, rax
    mov rcx, rbx
    lea rdx, szCommandLineToArgvW
    call r12                            ; GetProcAddress(shell32, "CommandLineToArgvW")
    test rax, rax
    jz sc_argc1
    mov dword ptr [rsp+510h], 0
    lea rdx, [rsp+510h]
    mov rcx, r13
    call rax                            ; CommandLineToArgvW(cmdline, &argc) -> argv
    test rax, rax
    jz sc_argc1
    mov r15, rax
    jmp sc_go
sc_argc1:
    ;%poly:junk
    ;%poly:end
    ; !!! keep argv away from host_path buffer at rsp+40h !!!
    lea rax, [rsp+40h]
    mov [rsp+500h], rax                 ; argv[0] = host_path
    mov qword ptr [rsp+508h], 0         ; argv[1] = NULL
    mov dword ptr [rsp+510h], 1         ; argc = 1
    lea r15, [rsp+500h]                 ; argv
sc_go:
    ;%poly:junk
    ;%poly:end
    ; call hostfxr_main_bundle_startupinfo
    mov ecx, [rsp+510h]                 ; argc
    mov rdx, r15                        ; argv
    lea r8, [rsp+40h]                   ; host_path
    lea r9, gDotnetRootW                ; dotnet_root
    lea rax, gAppPathW
    mov [rsp+20h], rax                  ; app_path
    mov rax, qword ptr [gHeaderOff]
    mov [rsp+28h], rax                  ; header_offset
    call r14
    mov r12d, eax                       ; exit code
    mov dword ptr [gExitCode], eax
    test eax, eax
    jz hxp_exit
    mov ecx, eax                        ; a failed host call is reported with the host's own status
    call FailReport
    mov r12d, dword ptr [gExitCode]
hxp_exit:
    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz hmd_dead
    mov ecx, r12d
    call rax
hmd_fail:
    ;%poly:junk
    ;%poly:end
    call FailReportNoHost               ; setup failed before the host was reached
    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz hmd_dead
    mov ecx, 3                          ; distinct from the host statuses, so a CLI run can tell the two apart
    call rax
hmd_dead:
    ;%poly:junk
    ;%poly:end
    ud2                                 ; :D
hostfxr_main_direct ENDP



; !! resolve dotnet root + hostfxr path
; dotnet root comes from the env, then the registry, then the default
; hostfxr comes from scanning <root>\host\fxr, preferring a gPrefMajor match

ResolveDotnetRoot PROC
    push rbx
    push r12
    sub rsp, 28h
    lea rcx, szGetEnvironmentVariableW  ; try DOTNET_ROOT env
    call ResolveApi
    test rax, rax
    jz rdr_reg
    mov rbx, rax
    lea rcx, szEnvDotnetRootW
    lea rdx, gDotnetRootW
    mov r8d, 260                        ; bufsize
    call rbx
    test rax, rax
    jnz rdr_done
rdr_reg:
    ;%poly:junk
    ;%poly:end
    lea rcx, szLoadLibraryA             ; registry fallback
    call ResolveApi
    test rax, rax
    jz rdr_default
    mov rbx, rax
    lea rcx, szAdvapi32
    call rbx
    test rax, rax
    jz rdr_default

    lea rcx, szRegOpenKeyExW
    lea rdx, szAdvapi32
    call ResolveApiIn
    test rax, rax
    jz rdr_default
    mov rbx, rax
    mov rcx, 80000002h                  ; HKLM
    lea rdx, szRegSubKeyW
    xor r8d, r8d                        ; ulOptions
    mov r9d, 20019h                     ; KEY_READ | KEY_WOW64_64KEY
    lea rax, [rsp+20h]
    mov [rsp+20h], rax                  ; phkResult
    call rbx
    test eax, eax
    jnz rdr_default
    mov r12, [rsp+20h]                  ; hKey

    lea rcx, szRegQueryValueExW
    lea rdx, szAdvapi32
    call ResolveApiIn
    test rax, rax
    jz rdr_default
    mov rbx, rax
    mov rcx, r12                        ; hKey
    lea rdx, szRegValueW                ; "InstallLocation"
    xor r8d, r8d                        ; lpReserved
    xor r9d, r9d                        ; lpType
    lea rax, gDotnetRootW
    mov [rsp+20h], rax                  ; lpData
    lea rax, [rsp+30h]
    mov [rsp+28h], rax                  ; lpcbData
    mov dword ptr [rsp+30h], 1024       ; bufsize
    call rbx
    test eax, eax
    jnz rdr_default
    jmp rdr_done
rdr_default:
    ;%poly:junk
    ;%poly:end
    lea rsi, szDefaultRootW             ; C:\Program Files\dotnet
    lea rdi, gDotnetRootW
    call StrCpyW
rdr_done:
    ;%poly:junk
    ;%poly:end
    add rsp, 28h
    pop r12
    pop rbx
    ret
ResolveDotnetRoot ENDP



EnumFxrBestMatch PROC
    push rbx
    push rbp
    push r12
    push r13
    push r14
    push r15
    sub rsp, 28h

    mov dword ptr [gFall], 0            ; reset bests
    mov dword ptr [gFall+4], 0
    mov dword ptr [gFall+8], 0
    mov dword ptr [gBest], 0
    mov dword ptr [gBest+4], 0
    mov dword ptr [gBest+8], 0

    lea rsi, gDotnetRootW               ; root + "\host"
    lea rdi, gFxrDirW
    call StrCpyW
    lea rsi, szHostDirW
    lea rdi, gFxrDirW
    call StrCatW

    lea rsi, gFxrDirW                   ; + "\fxr\*"
    lea rdi, gFxrSearchW
    call StrCpyW
    lea rsi, szFxrNameW
    lea rdi, gFxrSearchW
    call StrCatW
    lea rsi, szBackslashW
    lea rdi, gFxrSearchW
    call StrCatW
    lea rsi, szStarW
    lea rdi, gFxrSearchW
    call StrCatW

    lea rcx, szFindFirstFileW           ; FindFirstFileW
    call ResolveApi
    test rax, rax
    jz efb_fail
    mov rbx, rax
    lea rcx, gFxrSearchW
    lea rdx, gFindData
    call rbx
    cmp rax, -1
    je efb_fail                         ; wtf
    mov r12, rax                        ; hFind

efb_loop:
    ;%poly:junk
    ;%poly:end
    mov eax, dword ptr [gFindData]
    test eax, 10h                       ; FILE_ATTRIBUTE_DIRECTORY
    jz efb_next

    lea rsi, gFindData+2Ch              ; skip "." and ".."
    lea rdi, szDotW
    call StrEqW
    test eax, eax
    jnz efb_next
    lea rsi, gFindData+2Ch
    lea rdi, szDotDotW
    call StrEqW
    test eax, eax
    jnz efb_next

    lea rcx, gFindData+2Ch              ; parse version
    call ParseVerW
    mov r13d, eax                       ; major
    mov r14d, edx                       ; minor
    mov r15d, r8d                       ; patch

    lea rbx, gFall                      ; track highest as fallback
    lea rsi, gFindData+2Ch
    call UpdateBest

    mov eax, dword ptr [gPrefMajor]     ; check pref major
    test eax, eax
    jz efb_next
    cmp r13d, eax
    jne efb_next
    lea rbx, gBest
    lea rsi, gFindData+2Ch
    call UpdateBest

efb_next:
    ;%poly:junk
    ;%poly:end
    lea rcx, szFindNextFileW
    call ResolveApi
    test rax, rax
    jz efb_close
    mov rbx, rax
    mov rcx, r12
    lea rdx, gFindData
    call rbx
    test rax, rax
    jnz efb_loop

efb_close:
    ;%poly:junk
    ;%poly:end
    lea rcx, szFindClose
    call ResolveApi
    test rax, rax
    jz efb_build
    mov rcx, r12
    call rax

efb_build:
    ;%poly:junk
    ;%poly:end
    ; full path <root>\host\fxr\<ver>\hostfxr.dll
    lea rsi, gDotnetRootW
    lea rdi, gHostfxrPathW
    call StrCpyW
    lea rsi, szHostDirW
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, szFxrNameW
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, szBackslashW
    lea rdi, gHostfxrPathW
    call StrCatW

    mov eax, dword ptr [gBest]
    test eax, eax
    jnz efb_use_best
    lea rsi, gFall+0Ch                  ; use fallback
    jmp efb_use
efb_use_best:
    ;%poly:junk
    ;%poly:end
    lea rsi, gBest+0Ch                  ; use pref matched
efb_use:
    ;%poly:junk
    ;%poly:end
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, szHostfxrDllW
    lea rdi, gHostfxrPathW
    call StrCatW

    lea rsi, gHostfxrPathW              ; wide -> ansi
    lea rdi, gHostfxrA
    call WideToAnsi

efb_done:
    ;%poly:junk
    ;%poly:end
    add rsp, 28h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbp
    pop rbx
    ret

efb_fail:
    ;%poly:junk
    ;%poly:end
    ; !! cannot find fxr dir, try <root>\hostfxr.dll
    ; if that fails too, whatever, just crash
    lea rsi, gDotnetRootW
    lea rdi, gHostfxrPathW
    call StrCpyW
    lea rsi, szHostfxrDllW
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, gHostfxrPathW
    lea rdi, gHostfxrA
    call WideToAnsi

    add rsp, 28h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbp
    pop rbx
    ret
EnumFxrBestMatch ENDP



; rbx = storage (maj,min,pat + name at +0Ch)
; r13/r14/r15 = major/minor/patch, rsi = name
; update if version is higher
UpdateBest PROC
    push rsi
    push rdi
    push rbx
    mov rax, rbx
    mov rcx, rsi

    mov ebx, [rax]                      ; compare major
    cmp r13d, ebx
    jg ub_new
    jl ub_none

    mov ebx, [rax+4]                    ; compare minor
    cmp r14d, ebx
    jg ub_new
    jl ub_none

    mov ebx, [rax+8]                    ; compare patch
    cmp r15d, ebx
    jle ub_none                         ; <= means no update

ub_new:
    ;%poly:junk
    ;%poly:end
    mov [rax], r13d
    mov [rax+4], r14d
    mov [rax+8], r15d
    lea rdi, [rax+0Ch]
    mov rsi, rcx
    call StrCpyW
ub_none:
    ;%poly:junk
    ;%poly:end
    pop rbx
    pop rdi
    pop rsi
    ret
UpdateBest ENDP



; wide strcpy from rsi to rdi
StrCpyW PROC
    push rsi
    push rdi
scw_l:
    ;%poly:junk
    ;%poly:end
    mov ax, [rsi]
    mov [rdi], ax
    test ax, ax
    jz scw_d
    add rsi, 2
    add rdi, 2
    jmp scw_l
scw_d:
    ;%poly:junk
    ;%poly:end
    pop rdi
    pop rsi
    ret
StrCpyW ENDP



; wide strcat appends rsi to rdi
StrCatW PROC
    push rsi
    push rdi
scat_l:
    ;%poly:junk
    ;%poly:end
    cmp word ptr [rdi], 0
    je scat_f
    add rdi, 2
    jmp scat_l
scat_f:
    ;%poly:junk
    ;%poly:end
    call StrCpyW
    pop rdi
    pop rsi
    ret
StrCatW ENDP



; wide strcmp, rsi vs rdi, eax = 1 if equal
StrEqW PROC
    push rsi
    push rdi
seq_l:
    ;%poly:junk
    ;%poly:end
    mov ax, [rsi]
    mov dx, [rdi]
    cmp ax, dx
    jne seq_no
    test ax, ax
    jz seq_yes
    add rsi, 2
    add rdi, 2
    jmp seq_l
seq_yes:
    ;%poly:junk
    ;%poly:end
    mov eax, 1
    jmp seq_d
seq_no:
    ;%poly:junk
    ;%poly:end
    xor eax, eax
seq_d:
    ;%poly:junk
    ;%poly:end
    pop rdi
    pop rsi
    ret
StrEqW ENDP



; parse "10.0.1" -> eax.edx.r8d
ParseVerW PROC
    push rbx
    push rsi
    mov rsi, rcx
    call ParseNumW
    mov ebx, eax                        ; major
    xor edx, edx
    xor r8d, r8d

    cmp word ptr [rsi], '.'
    jne pv_d
    add rsi, 2
    call ParseNumW
    mov edx, eax                        ; minor

    cmp word ptr [rsi], '.'
    jne pv_d
    add rsi, 2
    call ParseNumW
    mov r8d, eax                        ; patch

pv_d:
    ;%poly:junk
    ;%poly:end
    mov eax, ebx
    pop rsi
    pop rbx
    ret
ParseVerW ENDP



; parse decimal from wide str
ParseNumW PROC
    push rbx
    xor eax, eax
    mov ebx, 10
pn_l:
    ;%poly:junk
    ;%poly:end
    movzx ecx, word ptr [rsi]
    sub ecx, '0'
    cmp ecx, 9                          ; not a digit?
    ja pn_d
    imul eax, ebx
    add eax, ecx
    add rsi, 2
    jmp pn_l
pn_d:
    ;%poly:junk
    ;%poly:end
    pop rbx
    ret
ParseNumW ENDP



; wide -> ansi
WideToAnsi PROC
    push rsi
    push rdi
wta_l:
    ;%poly:junk
    ;%poly:end
    movzx eax, word ptr [rsi]
    test eax, eax
    jz wta_d
    mov [rdi], al
    add rsi, 2
    inc rdi
    jmp wta_l
wta_d:
    ;%poly:junk
    ;%poly:end
    mov byte ptr [rdi], 0
    pop rdi
    pop rsi
    ret
WideToAnsi ENDP



ResolveApi PROC
    push rbx
    push rsi
    push rdi
    push r12

    mov rax, gs:[60h]                   ; PEB
    mov rax, [rax+18h]                  ; PEB_LDR_DATA
    lea rbx, [rax+20h]                  ; InLoadOrderModuleList
    mov rax, [rbx]
kl: 
    ;%poly:junk
    ;%poly:end
    cmp rax, rbx
    je rfail
    mov rdx, [rax+50h]                  ; module base
    mov r8, [rdx]                       ; first 8 bytes of DOS header
    mov r9, [rdx+8]
    mov r10, 006E00720065006Bh          ; "kern"
    mov r11, 00320033006C0065h          ; "el32"
    cmp r8, r10
    jne kup
    cmp r9, r11
    je got
kup:
    ;%poly:junk
    ;%poly:end
    mov r10, 004E00520045004Bh          ; "KERN"
    mov r11, 00320033004C0045h          ; "EL32"
    cmp r8, r10
    jne nxt
    cmp r9, r11
    je got
nxt:
    ;%poly:junk
    ;%poly:end
    mov rax, [rax]
    jmp kl
got:
    ;%poly:junk
    ;%poly:end
    mov rdx, [rax+20h]                  ; kernel32 base
    mov rdi, rcx
    jmp ra_exp
ResolveApi ENDP



ResolveApiIn PROC
    push rbx
    push rsi
    push rdi
    push r12

    mov r12, rcx
    mov rdi, rdx
    xor r9d, r9d
ra_nl:
    ;%poly:junk
    ;%poly:end
    mov al, [rdi+r9]
    test al, al
    jz ra_nl2
    inc r9
    jmp ra_nl
ra_nl2:
    ;%poly:junk
    ;%poly:end
    mov rax, gs:[60h]                   ; PEB
    mov rax, [rax+18h]                  ; PEB_LDR_DATA
    lea rbx, [rax+20h]                  ; InLoadOrderModuleList
    mov rax, [rbx]
ra_wl:
    ;%poly:junk
    ;%poly:end
    cmp rax, rbx
    je rfail
    mov rsi, [rax+50h]                  ; module base
    xor r8d, r8d
ra_wm:
    ;%poly:junk
    ;%poly:end
    cmp r8, r9
    jae ra_wg
    movzx r10d, byte ptr [rdi+r8]
    movzx r11d, word ptr [rsi+r8*2]
    cmp r10d, 61h                       ; ansi lowercase
    jb ra_u1
    sub r10d, 20h
ra_u1:
    ;%poly:junk
    ;%poly:end
    cmp r11d, 61h                       ; wide lowercase
    jb ra_u2
    sub r11d, 20h
ra_u2:
    ;%poly:junk
    ;%poly:end
    cmp r10d, r11d
    jne ra_wn
    inc r8
    jmp ra_wm
ra_wn:
    ;%poly:junk
    ;%poly:end
    mov rax, [rax]
    jmp ra_wl
ra_wg:
    ;%poly:junk
    ;%poly:end
    mov rdx, [rax+20h]                  ; module base
    mov rdi, r12
    jmp ra_exp
ResolveApiIn ENDP


; shared export lookup, rdx = module base, rdi = name
; returns rax = func addr or 0
ra_exp:
    ;%poly:junk
    ;%poly:end
    mov eax, [rdx+3Ch]                  ; PE header
    lea r8, [rdx+rax]
    mov eax, [r8+88h]                   ; export dir RVA
    test eax, eax
    jz rfail
    add rax, rdx
    mov r8, rax

    mov ebx, [r8+18h]                   ; num names
    test ebx, ebx
    jz rfail
    mov esi, [r8+20h]                   ; name ptrs RVA
    add rsi, rdx
    mov r11d, [r8+24h]                  ; ordinals RVA
    add r11, rdx
    xor r10d, r10d

nml:
    ;%poly:junk
    ;%poly:end
    cmp r10d, ebx
    jae rfail
    mov eax, [rsi]
    lea r9, [rdx+rax]                   ; export name
    mov rcx, rdi
cmpl:
    ;%poly:junk
    ;%poly:end
    mov al, [rcx]
    test al, al
    jnz c2
    cmp byte ptr [r9], 0                ; make sure both end here
    je mok
    jmp mnx
c2:
    ;%poly:junk
    ;%poly:end
    cmp al, [r9]
    jne mnx
    inc rcx
    inc r9
    jmp cmpl
mnx:
    ;%poly:junk
    ;%poly:end
    add rsi, 4
    inc r10d
    jmp nml

mok:
    ;%poly:junk
    ;%poly:end
    movzx eax, word ptr [r11+r10*2]     ; ordinal
    mov ecx, [r8+1Ch]                   ; func ptrs RVA
    add rcx, rdx
    mov eax, [rcx+rax*4]                ; func RVA

    ; !! forwarder, the function RVA in the export dir is a "DLL.Func" string
    mov r9d, [rdx+3Ch]
    lea r9, [rdx+r9+18h+70h]            ; export dir range
    mov r10d, [r9]
    mov r9d, [r9+4]
    cmp eax, r10d
    jb rfok
    add r9d, r10d
    cmp eax, r9d
    ja rfok

    lea rdi, [rdx+rax]                  ; forwarder string
    mov rsi, rdi
fdl:
    ;%poly:junk
    ;%poly:end
    mov al, [rsi]
    inc rsi
    cmp al, '.'
    jne fdl
    mov r12, rsi
    sub r12, rdi
    sub r12, 1                          ; dll name length

    mov rax, gs:[60h]                   ; find forwarder dll
    mov rax, [rax+18h]
    lea rbx, [rax+20h]
    mov rax, [rbx]
fwl:
    ;%poly:junk
    ;%poly:end
    cmp rax, rbx
    je rfail
    mov r8, [rax+50h]                   ; module base
    xor r9d, r9d
fmc:
    ;%poly:junk
    ;%poly:end
    cmp r9, r12
    jae fgot
    movzx r10d, byte ptr [rdi+r9]
    movzx r11d, word ptr [r8+r9*2]
    cmp r10d, 61h
    jb fup1
    sub r10d, 20h
fup1:
    ;%poly:junk
    ;%poly:end
    cmp r11d, 61h
    jb fup2
    sub r11d, 20h
fup2:
    ;%poly:junk
    ;%poly:end
    cmp r10d, r11d
    jne fnext
    inc r9
    jmp fmc
fnext:
    ;%poly:junk
    ;%poly:end
    mov rax, [rax]
    jmp fwl
fgot:
    ;%poly:junk
    ;%poly:end
    mov rdx, [rax+20h]                  ; forwarder dll base
    mov rdi, rsi                        ; func name

    mov eax, [rdx+3Ch]                  ; look up in forwarder dll
    lea r8, [rdx+rax]
    mov eax, [r8+88h]
    test eax, eax
    jz rfail
    add rax, rdx
    mov r8, rax
    mov ebx, [r8+18h]
    test ebx, ebx
    jz rfail
    mov esi, [r8+20h]
    add rsi, rdx
    mov r11d, [r8+24h]
    add r11, rdx
    xor r10d, r10d
    jmp nml

rfok:
    ;%poly:junk
    ;%poly:end
    add rax, rdx                        ; va = base + rva
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
rfail:
    ;%poly:junk
    ;%poly:end
    xor eax, eax
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret



szExitProcess             db "ExitProcess",0
szLoadLibraryA            db "LoadLibraryA",0
szGetProcAddress          db "GetProcAddress",0
szGetModuleFileNameW      db "GetModuleFileNameW",0
szHostfxrMainBundle       db "hostfxr_main_bundle_startupinfo",0
szGetEnvironmentVariableW db "GetEnvironmentVariableW",0
szFindFirstFileW          db "FindFirstFileW",0
szFindNextFileW           db "FindNextFileW",0
szFindClose               db "FindClose",0
szAdvapi32                db "advapi32.dll",0
szRegOpenKeyExW           db "RegOpenKeyExW",0
szRegQueryValueExW        db "RegQueryValueExW",0
szGetCommandLineW         db "GetCommandLineW",0
szShell32                 db "shell32.dll",0
szCommandLineToArgvW      db "CommandLineToArgvW",0

    align 8

szEnvDotnetRootW dw 'D','O','T','N','E','T','_','R','O','O','T',0
szRegSubKeyW     dw 'S','O','F','T','W','A','R','E','\','d','o','t','n','e','t','\','S','e','t','u','p','\','I','n','s','t','a','l','l','e','d','V','e','r','s','i','o','n','s','\','x','6','4',0
szRegValueW      dw 'I','n','s','t','a','l','l','L','o','c','a','t','i','o','n',0
szHostDirW       dw '\','h','o','s','t',0
szFxrNameW       dw '\','f','x','r',0
szHostfxrDllW    dw '\','h','o','s','t','f','x','r','.','d','l','l',0
szStarW          dw '*',0
szDotW           dw '.',0
szDotDotW        dw '.','.',0
szBackslashW     dw '\',0
szDefaultRootW   dw 'C',':','\','P','r','o','g','r','a','m',' ','F','i','l','e','s','\','d','o','t','n','e','t',0

    align 8

gDotnetRootW  db 520 dup(0)             ; dotnet root (wide)
gHostfxrPathW db 520 dup(0)             ; hostfxr.dll full path (wide)
gHostfxrA     db 520 dup(0)             ; ANSI for LoadLibraryA
gFxrDirW      db 520 dup(0)             ; <root>\host
gFxrSearchW   db 520 dup(0)             ; <root>\host\fxr\*
gFindData     db 640 dup(0)             ; WIN32_FIND_DATAW
gFall         db 12 dup(0), 520 dup(0)  ; fallback, maj + min + patch + name
gBest         db 12 dup(0), 520 dup(0)  ; pref matched, maj + min + patch + name

    align 8

gAppPathW     db 520 dup(0)             ; app.dll path, built at runtime = <host dir>\<app name>
gAppNameW     db "##APPNAME##",0        ; main DLL file name (packer patches)
              db 256 dup(0)

    align 8

gPrefMajor    db "##PREFMAJ##",0

    align 8

gHeaderOff    dq 01122334455667788h

    align 8

; placed after the strings so the packer markers above keep their offsets
; and outside both scramble ranges, so the literals below survive
; report buffers are VirtualAlloc'd on failure only, freed on success
szSetErrWriter   db "hostfxr_set_error_writer",0
szGetStdHandle   db "GetStdHandle",0
szWriteFile      db "WriteFile",0
szUser32         db "user32.dll",0
szMessageBoxW    db "MessageBoxW",0
szShellExecuteW  db "ShellExecuteW",0
szGetEnvA        db "GetEnvironmentVariableA",0
szVirtualAlloc   db "VirtualAlloc",0
szEnvNoGuiA      db "DOTNET_DISABLE_GUI_ERRORS",0
szOpenW          dw 'o','p','e','n',0
szInsGenericA    db "You must install or update .NET to run this application.",0
szInsDesktopA    db "You must install .NET Desktop Runtime to run this application.",0
szLearnA         db 13,10,"Learn more:",13,10,"https://aka.ms/dotnet/app-launch-failed",13,10,13,10,0
szAskDownloadA   db "Would you like to download it now?",0
szDesktopFwA     db "Microsoft.WindowsDesktop.App",0
szUrlPrefixA     db "https://aka.ms/dotnet-core-applaunch?framework=",0
szGuiSuffixA     db "&gui=true",0
szCrLfA          db 13,10,0
szFwHeadA        db "Framework: 'Microsoft.NETCore.App', version '",0
szFwTailA        db ".0.0' (x64)",0
szLearnLinkA     db "Learn more:",13,10,"https://aka.ms/dotnet/app-launch-failed",13,10,13,10,0
szDownLinkA      db "Download link:",13,10,0
szUrlFwA         db "Microsoft.NETCore.App",0
szUrlVerA        db "&framework_version=",0
szUrlTailA       db ".0.0&arch=x64&rid=win-x64&os=win10&gui=true",0
szNoHostA        db "You must install or update .NET to run this application.",13,10,13,10,"The .NET runtime could not be located.",13,10,13,10,"Learn more:",13,10,"https://aka.ms/dotnet/app-launch-failed",13,10,13,10,0

    align 8

; the packer keeps only stub .text and payload .rdata, reporting buffers
; cannot live in a data section, they are allocated on the failure path
; (VirtualAlloc), only these pointers sit in .text
; nothing is allocated unless a launch fails
REP_OFF_ERRW   equ 0                    ; wide capture of the host text (ERR_BUF_MAX chars)
REP_OFF_ANI    equ 32000                ; stderr mirror scratch
REP_OFF_CAP    equ 56000                ; ansi copy of the capture
REP_OFF_LOG    equ 80000                ; event log message buffer
REP_OFF_DLG    equ 104000               ; dialog text
REP_OFF_URL    equ 128000               ; download url
REP_OFF_TMP    equ 129152               ; env var scratch
REP_OFF_WLOG   equ 129280               ; wide event log message
REP_OFF_WDLG   equ 177280               ; wide dialog text
REP_OFF_WURL   equ 225280               ; wide url
REP_OFF_EXEP   equ 229376               ; own path / name, wide and ansi
REP_OFF_EXEN   equ 229936
REP_OFF_EXEPA  equ 230496
REP_OFF_EXENA  equ 231056
REP_OFF_WNAME   equ 231616              ; wide basename of our own image (dialog caption)
REP_TOTAL      equ 232176

gRepBase      dq 0
gErrBufP      dq 0
gAnsiBufP     dq 0
gAnsiCapP     dq 0
gAnsiLogP     dq 0
gAnsiDlgP     dq 0
gAnsiUrlP     dq 0
gAnsiTmpP     dq 0
gWideLogP     dq 0
gWideDlgP     dq 0
gWideUrlP     dq 0
gExePathP     dq 0
gExeNameP     dq 0
gExePathAP    dq 0
gExeNameAP    dq 0
gWideExeNameP dq 0
gEvtArg       dq 0                      ; single pointer array for ReportEventW
gErrUsedW     dd 0                      ; chars captured (0 = none)
gAnsiStat     db 192 dup(0)             ; static ansi scratch (major digits, env var values)
gGetProcAddr  dq 0                      ; cached GetProcAddress
gApi1         dq 0                      ; staging storage for resolved pointers
gApi2         dq 0                      ; staging storage for resolved pointers


gExitCode  dd 0                         ; exit status kept across the report call

StrLenA PROC
    xor eax, eax
sla_l:
    cmp byte ptr [rcx+rax], 0
    je sla_d
    inc rax
    jmp sla_l
sla_d:
    ret
StrLenA ENDP



StrCpyA PROC
    push rsi
    push rdi
sca_l:
    mov al, byte ptr [rsi]
    mov byte ptr [rdi], al
    test al, al
    jz sca_d
    inc rsi
    inc rdi
    jmp sca_l
sca_d:
    pop rdi
    pop rsi
    ret
StrCpyA ENDP



StrCatA PROC
    push rsi
    push rdi
scb_find:
    cmp byte ptr [rdi], 0
    je scb_cp
    inc rdi
    jmp scb_find
scb_cp:
    mov al, byte ptr [rsi]
    mov byte ptr [rdi], al
    test al, al
    jz scb_d
    inc rsi
    inc rdi
    jmp scb_cp
scb_d:
    pop rdi
    pop rsi
    ret
StrCatA ENDP



AnsiToWide PROC
    push rsi
    push rdi
atw_l:
    movzx eax, byte ptr [rsi]
    mov word ptr [rdi], ax
    test eax, eax
    jz atw_d
    inc rsi
    add rdi, 2
    jmp atw_l
atw_d:
    pop rdi
    pop rsi
    ret
AnsiToWide ENDP



EnsureRepBufs PROC
    push rbx
    sub rsp, 20h
    cmp qword ptr [gRepBase], 0
    jne erb_done
    lea rcx, szVirtualAlloc
    call ResolveApi
    test rax, rax
    jz erb_done
    xor ecx, ecx
    mov edx, REP_TOTAL
    mov r8d, 3000h                      ; MEM_COMMIT | MEM_RESERVE
    mov r9d, 4h                         ; PAGE_READWRITE
    call rax
    test rax, rax
    jz erb_done
    mov qword ptr [gRepBase], rax
    mov rbx, rax
    lea rax, [rbx+REP_OFF_ERRW]
    mov qword ptr [gErrBufP], rax
    lea rax, [rbx+REP_OFF_ANI]
    mov qword ptr [gAnsiBufP], rax
    lea rax, [rbx+REP_OFF_CAP]
    mov qword ptr [gAnsiCapP], rax
    lea rax, [rbx+REP_OFF_LOG]
    mov qword ptr [gAnsiLogP], rax
    lea rax, [rbx+REP_OFF_DLG]
    mov qword ptr [gAnsiDlgP], rax
    lea rax, [rbx+REP_OFF_URL]
    mov qword ptr [gAnsiUrlP], rax
    lea rax, [rbx+REP_OFF_TMP]
    mov qword ptr [gAnsiTmpP], rax
    lea rax, [rbx+REP_OFF_WLOG]
    mov qword ptr [gWideLogP], rax
    lea rax, [rbx+REP_OFF_WDLG]
    mov qword ptr [gWideDlgP], rax
    lea rax, [rbx+REP_OFF_WURL]
    mov qword ptr [gWideUrlP], rax
    lea rax, [rbx+REP_OFF_EXEP]
    mov qword ptr [gExePathP], rax
    lea rax, [rbx+REP_OFF_EXEN]
    mov qword ptr [gExeNameP], rax
    lea rax, [rbx+REP_OFF_EXEPA]
    mov qword ptr [gExePathAP], rax
    lea rax, [rbx+REP_OFF_EXENA]
    mov qword ptr [gExeNameAP], rax
    lea rax, [rbx+REP_OFF_WNAME]
    mov qword ptr [gWideExeNameP], rax
erb_done:
    add rsp, 20h
    pop rbx
    ret
EnsureRepBufs ENDP



FailReport PROC
    push rbx
    push rdi
    push rsi
    sub rsp, 20h
    mov ebx, ecx
    call EnsureRepBufs
    cmp qword ptr [gAnsiDlgP], 0
    je fr_done                          ; no buffers, keep the host status instead of faulting
    call BuildFailText                  ; official wording, framework line, links, download url
    call ShowFailDialogIfGui
fr_done:
    add rsp, 20h
    pop rsi
    pop rdi
    pop rbx
    ret
FailReport ENDP



FailReportNoHost PROC
    sub rsp, 28h
    mov ecx, 80008096h                  ; FrameworkMissingFailure, same status the host uses
    call FailReport
    call WriteDlgToStdErr               ; console images only see this line, the host never ran
    add rsp, 28h
    ret
FailReportNoHost ENDP



BuildFailText PROC
    push rdi
    push rsi
    sub rsp, 28h
    mov rdi, qword ptr [gAnsiUrlP]
    test rdi, rdi
    jz bft_text
    lea rsi, szUrlPrefixA
    call StrCpyA
    lea rsi, szUrlFwA
    call StrCatA
    lea rsi, szUrlVerA
    call StrCatA
    call FormatMajorA
    lea rsi, gAnsiStat
    call StrCatA
    lea rsi, szUrlTailA
    call StrCatA
bft_text:
    mov rdi, qword ptr [gAnsiDlgP]
    test rdi, rdi
    jz bft_done
    lea rsi, szInsGenericA
    call StrCpyA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szFwHeadA
    call StrCatA
    call FormatMajorA
    lea rsi, gAnsiStat
    call StrCatA
    lea rsi, szFwTailA
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szLearnLinkA
    call StrCatA
    lea rsi, szDownLinkA
    call StrCatA
    mov rsi, qword ptr [gAnsiUrlP]
    test rsi, rsi
    jz bft_ask
    call StrCatA
bft_ask:
    mov rdi, qword ptr [gAnsiDlgP]
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szAskDownloadA
    call StrCatA
bft_done:
    add rsp, 28h
    pop rsi
    pop rdi
    ret
BuildFailText ENDP



FormatMajorA PROC
    push rsi
    push rdi
    mov eax, dword ptr [gPrefMajor]
    lea rdi, gAnsiStat
    cmp eax, 100
    jb fma_ten
    xor edx, edx
    mov ecx, 100
    div ecx
    add al, '0'
    mov byte ptr [rdi], al
    inc rdi
    mov eax, edx
fma_ten:
    xor edx, edx
    mov ecx, 10
    div ecx
    test eax, eax
    jz fma_one
    add al, '0'
    mov byte ptr [rdi], al
    inc rdi
fma_one:
    mov al, dl
    add al, '0'
    mov byte ptr [rdi], al
    mov byte ptr [rdi+1], 0
    pop rdi
    pop rsi
    ret
FormatMajorA ENDP



GetExeNames PROC
    push rsi
    push rdi
    sub rsp, 28h
    lea rcx, szGetModuleFileNameW
    call ResolveApi
    test rax, rax
    jz gen_done
    xor ecx, ecx
    mov rdx, qword ptr [gExePathP]
    mov r8d, 260
    call rax
    test rax, rax
    jz gen_done
    mov rsi, qword ptr [gExePathP]
    mov rdi, qword ptr [gExePathAP]
    call WideToAnsi
    mov rsi, qword ptr [gExePathAP]     ; name = after the last backslash
    mov rdi, qword ptr [gExeNameAP]
    xor rdx, rdx
gen_scan:
    mov al, byte ptr [rsi]
    test al, al
    jz gen_copy
    cmp al, 5Ch
    jne gen_next
    lea rdx, [rsi+1]
gen_next:
    inc rsi
    jmp gen_scan
gen_copy:
    test rdx, rdx
    jz gen_whole
    mov rsi, rdx
gen_whole:
    lea rsi, [rsi]
    mov rdi, qword ptr [gExeNameAP]
    call StrCpyA
gen_done:
    add rsp, 28h
    pop rdi
    pop rsi
    ret
GetExeNames ENDP



IsGuiProcess PROC
    mov rax, gs:[60h]                   ; PEB
    mov rax, [rax+10h]                  ; ImageBaseAddress
    mov edx, dword ptr [rax+3Ch]        ; e_lfanew
    movzx eax, word ptr [rax+rdx+5Ch]   ; NT headers + OptionalHeader.Subsystem
    cmp eax, 2
    sete al
    movzx eax, al
    ret
IsGuiProcess ENDP



ShowFailDialogIfGui PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 20h
    call IsGuiProcess
    test eax, eax
    jz sfd_done
    lea rcx, szEnvNoGuiA
    mov rdx, qword ptr [gAnsiTmpP]
    mov r8d, 8
    lea rcx, szGetEnvA
    call ResolveApi                     ; kernel32 direct export, GetEnvironmentVariableA
    test rax, rax
    jz sfd_build
    lea rcx, szEnvNoGuiA
    mov rdx, qword ptr [gAnsiTmpP]
    mov r8d, 8
    call rax
    test rax, rax
    jz sfd_build
    ; the check is "value parses to 1", 0, true and empty keep the dialog
    mov rsi, qword ptr [gAnsiTmpP]
    xor edx, edx
env_scan:
    mov al, byte ptr [rsi]
    test al, al
    jz env_cmp
    cmp al, 20h
    jne env_digit
    test edx, edx
    jnz env_bad
    inc rsi
    jmp env_scan
env_digit:
    sub al, '0'
    cmp al, 9
    ja env_bad
    imul edx, edx, 10
    movzx eax, al
    add edx, eax
    inc rsi
    jmp env_scan
env_cmp:
    cmp edx, 1
    jne env_bad
    jmp sfd_done                        ; DOTNET_DISABLE_GUI_ERRORS=1 skips only the dialog
env_bad:
sfd_build:
    call GetExeNames                    ; fills gExeNameP/gExePathP (do this before resolving/using volatile regs)
    mov rsi, qword ptr [gAnsiDlgP]
    mov rdi, qword ptr [gWideDlgP]
    call AnsiToWide
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz sfd_done
    lea rcx, szUser32
    call rax
    test rax, rax
    jz sfd_done
    mov rbx, rax                        ; the user32 module handle
    mov rcx, rbx
    lea rdx, szMessageBoxW
    call GetProcAddrOf
    test rax, rax
    jz sfd_done
    mov qword ptr [gApi1], rax          ; hold MessageBoxW across the caption helper below
    mov rsi, qword ptr [gExePathP]
    mov rdi, qword ptr [gWideExeNameP]
    call WideBaseNameOf                 ; official caption == the application file name
    xor ecx, ecx                        ; hwndOwner
    mov rdx, qword ptr [gWideDlgP]
    mov r8, qword ptr [gWideExeNameP]
    mov r9d, 14h                        ; MB_ICONERROR | MB_YESNO
    mov rax, qword ptr [gApi1]
    call rax
    cmp eax, 6                          ; IDYES
    jne sfd_done
    call OpenDownloadUrl
sfd_done:
    add rsp, 20h
    pop rdi
    pop rsi
    pop rbx
    ret
ShowFailDialogIfGui ENDP

; a console image gets the same report on stderr, the host was never reached
WriteDlgToStdErr PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 40h
    call IsGuiProcess
    test eax, eax
    jnz wds_done
    mov rsi, qword ptr [gAnsiDlgP]
    test rsi, rsi
    jz wds_done
    lea rcx, szGetStdHandle
    call ResolveApi
    test rax, rax
    jz wds_done
    mov ecx, -12                        ; STD_ERROR_HANDLE
    call rax
    test rax, rax
    jz wds_done
    mov rbx, rax
    lea rcx, szWriteFile
    call ResolveApi
    test rax, rax
    jz wds_done
    mov rdi, rax                        ; resolved first, ResolveApi clobbers r8, and r8 carries the length
    mov rcx, rsi
    call StrLenA
    mov r8d, eax
    mov rcx, rbx
    mov rdx, rsi
    lea r9, [rsp+30h]
    mov dword ptr [rsp+30h], 0
    mov qword ptr [rsp+20h], 0          ; lpOverlapped, the 5th argument of WriteFile
    call rdi
wds_done:
    add rsp, 40h
    pop rdi
    pop rsi
    pop rbx
    ret
WriteDlgToStdErr ENDP



OpenDownloadUrl PROC
    push rsi
    push rdi
    sub rsp, 28h
    mov rsi, qword ptr [gAnsiUrlP]
    mov rdi, qword ptr [gWideUrlP]
    call AnsiToWide
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz oud_done
    lea rcx, szShell32
    call rax
    test rax, rax
    jz oud_done
    mov rsi, rax
    mov rcx, rsi
    lea rdx, szShellExecuteW
    call GetProcAddrOf
    test rax, rax
    jz oud_done
    xor ecx, ecx                        ; hwnd
    lea rdx, szOpenW
    mov r8, qword ptr [gWideUrlP]
    xor r9d, r9d                        ; lpParameters
    mov qword ptr [rsp+20h], 0          ; lpDirectory
    mov qword ptr [rsp+28h], 1          ; nShowCmd = SW_SHOWNORMAL
    call rax
oud_done:
    add rsp, 28h
    pop rdi
    pop rsi
    ret
OpenDownloadUrl ENDP



GetProcAddrOf PROC
    push rbx
    push rsi
    sub rsp, 28h
    mov rbx, rcx
    mov rsi, rdx
    mov rax, qword ptr [gGetProcAddr]
    test rax, rax
    jnz gpa_have
    lea rcx, szGetProcAddress
    call ResolveApi
    test rax, rax
    jz gpa_done
    mov qword ptr [gGetProcAddr], rax
gpa_have:
    mov rcx, rbx
    mov rdx, rsi
    call rax
gpa_done:
    add rsp, 28h
    pop rsi
    pop rbx
    ret
GetProcAddrOf ENDP



WideBaseNameOf PROC
    push rsi
    push rdi
    xor edx, edx                        ; offset just past the last backslash
    xor ecx, ecx
wbn_scan:
    mov ax, word ptr [rsi+rcx*2]
    test ax, ax
    jz wbn_move
    cmp ax, 5Ch
    jne wbn_next
    lea rdx, [rcx+1]
wbn_next:
    inc rcx
    jmp wbn_scan
wbn_move:
    lea rsi, [rsi+rdx*2]
wbn_copy:
    mov ax, word ptr [rsi]
    mov word ptr [rdi], ax
    test ax, ax
    jz wbn_done
    add rsi, 2
    add rdi, 2
    jmp wbn_copy
wbn_done:
    pop rdi
    pop rsi
    ret
WideBaseNameOf ENDP

END