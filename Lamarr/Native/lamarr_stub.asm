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



; call hostfxr_main_bundle_startupinfo directly
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

    ; build app_path = <host dir>\<app name> at runtime (exe may be relocated)
    lea rdi, gAppPathW
    lea rsi, [rsp+40h]                  ; host_path from GetModuleFileNameW
    call StrCpyW                        ; gAppPathW = host_path
    lea rdi, gAppPathW
    xor r9, r9                          ; r9 = last '\' + 2 (filename start)
bap_scan:
    movzx ecx, word ptr [rdi]
    test ecx, ecx
    jz bap_got
    cmp ecx, 5Ch                        ; '\'
    jne bap_next
    lea r9, [rdi+2]
bap_next:
    add rdi, 2
    jmp bap_scan
bap_got:
    test r9, r9
    jz bap_plain
    mov rdi, r9
bap_plain:
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
    mov r13, rax                        ; r12 = hostfxr

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

    ; argv from command line: GetCommandLineW + CommandLineToArgvW
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
    ; !!! keep argv away from host_path buffer at rsp+40h !!!
    lea rax, [rsp+40h]
    mov [rsp+500h], rax                 ; argv[0] = host_path
    mov qword ptr [rsp+508h], 0         ; argv[1] = NULL
    mov dword ptr [rsp+510h], 1         ; argc = 1
    lea r15, [rsp+500h]                 ; argv
sc_go:
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

    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz hmd_dead
    mov ecx, r12d
    call rax
hmd_fail:
    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz hmd_dead
    mov ecx, 3
    call rax
hmd_dead:
    ud2                                 ; app_path
hostfxr_main_direct ENDP



; resolve dotnet root + hostfxr path
; root: env -> registry -> default
; fxr:  scan <root>\host\fxr, prefer gPrefMajor match

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
    lea rcx, szLoadLibraryA             ; fallback: registry
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
    mov r9d, 20019h                     ; KEY_READ
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
    lea rsi, szDefaultRootW             ; C:\Program Files\dotnet
    lea rdi, gDotnetRootW
    call StrCpyW
rdr_done:
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
    lea rcx, szFindClose
    call ResolveApi
    test rax, rax
    jz efb_build
    mov rcx, r12
    call rax

efb_build:
    ; build full path: <root>\host\fxr\<ver>\hostfxr.dll
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
    lea rsi, gBest+0Ch                  ; use pref-matched
efb_use:
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, szHostfxrDllW
    lea rdi, gHostfxrPathW
    call StrCatW

    lea rsi, gHostfxrPathW              ; wide -> ansi
    lea rdi, gHostfxrA
    call WideToAnsi

efb_done:
    add rsp, 28h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbp
    pop rbx
    ret

efb_fail:
    ; cannot find fxr dir, try <root>\hostfxr.dll
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
    mov [rax], r13d
    mov [rax+4], r14d
    mov [rax+8], r15d
    lea rdi, [rax+0Ch]
    mov rsi, rcx
    call StrCpyW
ub_none:
    pop rbx
    pop rdi
    pop rsi
    ret
UpdateBest ENDP



; wide strcpy: rsi -> rdi
StrCpyW PROC
    push rsi
    push rdi
scw_l:
    mov ax, [rsi]
    mov [rdi], ax
    test ax, ax
    jz scw_d
    add rsi, 2
    add rdi, 2
    jmp scw_l
scw_d:
    pop rdi
    pop rsi
    ret
StrCpyW ENDP



; wide strcat: append rsi to rdi
StrCatW PROC
    push rsi
    push rdi
scat_l:
    cmp word ptr [rdi], 0
    je scat_f
    add rdi, 2
    jmp scat_l
scat_f:
    call StrCpyW
    pop rdi
    pop rsi
    ret
StrCatW ENDP



; wide strcmp: rsi vs rdi, eax = 1 if equal
StrEqW PROC
    push rsi
    push rdi
seq_l:
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
    mov eax, 1
    jmp seq_d
seq_no:
    xor eax, eax
seq_d:
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
    movzx ecx, word ptr [rsi]
    sub ecx, '0'
    cmp ecx, 9                          ; not a digit?
    ja pn_d
    imul eax, ebx
    add eax, ecx
    add rsi, 2
    jmp pn_l
pn_d:
    pop rbx
    ret
ParseNumW ENDP



; wide -> ansi
WideToAnsi PROC
    push rsi
    push rdi
wta_l:
    movzx eax, word ptr [rsi]
    test eax, eax
    jz wta_d
    mov [rdi], al
    add rsi, 2
    inc rdi
    jmp wta_l
wta_d:
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
    mov r10, 004E00520045004Bh          ; "KERN"
    mov r11, 00320033004C0045h          ; "EL32"
    cmp r8, r10
    jne nxt
    cmp r9, r11
    je got
nxt:
    mov rax, [rax]
    jmp kl
got:
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
    mov al, [rdi+r9]
    test al, al
    jz ra_nl2
    inc r9
    jmp ra_nl
ra_nl2:
    mov rax, gs:[60h]                   ; PEB
    mov rax, [rax+18h]                  ; PEB_LDR_DATA
    lea rbx, [rax+20h]                  ; InLoadOrderModuleList
    mov rax, [rbx]
ra_wl:
    cmp rax, rbx
    je rfail
    mov rsi, [rax+50h]                  ; module base
    xor r8d, r8d
ra_wm:
    cmp r8, r9
    jae ra_wg
    movzx r10d, byte ptr [rdi+r8]
    movzx r11d, word ptr [rsi+r8*2]
    cmp r10d, 61h                       ; ansi lowercase
    jb ra_u1
    sub r10d, 20h
ra_u1:
    cmp r11d, 61h                       ; wide lowercase
    jb ra_u2
    sub r11d, 20h
ra_u2:
    cmp r10d, r11d
    jne ra_wn
    inc r8
    jmp ra_wm
ra_wn:
    mov rax, [rax]
    jmp ra_wl
ra_wg:
    mov rdx, [rax+20h]                  ; module base
    mov rdi, r12
    jmp ra_exp
ResolveApiIn ENDP


; shared export lookup: rdx = module base, rdi = name
; returns rax = func addr or 0
ra_exp:
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
    cmp r10d, ebx
    jae rfail
    mov eax, [rsi]
    lea r9, [rdx+rax]                   ; export name
    mov rcx, rdi
cmpl:
    mov al, [rcx]
    test al, al
    jnz c2
    cmp byte ptr [r9], 0                ; make sure both end here
    je mok
    jmp mnx
c2:
    cmp al, [r9]
    jne mnx
    inc rcx
    inc r9
    jmp cmpl
mnx:
    add rsi, 4
    inc r10d
    jmp nml

mok:
    movzx eax, word ptr [r11+r10*2]     ; ordinal
    mov ecx, [r8+1Ch]                   ; func ptrs RVA
    add rcx, rdx
    mov eax, [rcx+rax*4]                ; func RVA

    ; forwarder: function RVA in export dir = "DLL.Func" string
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
    cmp rax, rbx
    je rfail
    mov r8, [rax+50h]                   ; module base
    xor r9d, r9d
fmc:
    cmp r9, r12
    jae fgot
    movzx r10d, byte ptr [rdi+r9]
    movzx r11d, word ptr [r8+r9*2]
    cmp r10d, 61h
    jb fup1
    sub r10d, 20h
fup1:
    cmp r11d, 61h
    jb fup2
    sub r11d, 20h
fup2:
    cmp r10d, r11d
    jne fnext
    inc r9
    jmp fmc
fnext:
    mov rax, [rax]
    jmp fwl
fgot:
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
    add rax, rdx                        ; va = base + rva
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
rfail:
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

gDotnetRootW  db 520 dup(0)            ; dotnet root (wide)
gHostfxrPathW db 520 dup(0)            ; hostfxr.dll full path (wide)
gHostfxrA     db 520 dup(0)            ; ANSI for LoadLibraryA
gFxrDirW      db 520 dup(0)            ; <root>\host
gFxrSearchW   db 520 dup(0)            ; <root>\host\fxr\*
gFindData     db 640 dup(0)            ; WIN32_FIND_DATAW
gFall         db 12 dup(0), 520 dup(0) ; fallback: maj,min,pat + name
gBest         db 12 dup(0), 520 dup(0) ; pref-matched: maj,min,pat + name

    align 8

gAppPathW     db 520 dup(0)               ; app.dll path, built at runtime = <host dir>\<app name>
gAppNameW     db "##APPNAME##",0          ; main DLL file name (packer patches)
              db 256 dup(0)

    align 8

gPrefMajor    db "##PREFMAJ##",0

    align 8

gHeaderOff    dq 01122334455667788h

    align 8

END