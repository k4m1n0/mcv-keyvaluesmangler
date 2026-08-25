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
    ; ?? it is not meant to stop anyone from reverse engineering
    ; but a honeypot like this is fun
    lea rax, StubEntry
    movzx ecx, byte ptr [rax]
    movzx edx, byte ptr [rax+1]
    movzx r8d, byte ptr [rax+2]
    xor ecx, 55h                        ; push rbp
    xor edx, 48h                        ; REX.W prefix
    xor r8d, 8Bh                        ; mov rbp,rsp
    or ecx, edx
    or ecx, r8d
    test ecx, ecx
    jz se_default
    ; debugger detected - "malicious" revenge path
    mov ecx, 1
    cmp ecx, 6
    ja se_default
    lea rcx, se_table
    movsxd rdx, dword ptr [rcx+rdx*4]
    add rdx, rcx
    jmp rdx
se_table:
    dd se_c0 - se_table
    dd se_c1 - se_table
    dd se_c2 - se_table
    dd se_c3 - se_table
    dd se_c4 - se_table
    dd se_c5 - se_table
    dd se_c6 - se_table
se_c0:
    lea rcx, szHostfxrMainBundle
    call ResolveApi
    test rax, rax
    jz se_default
    mov rbx, rax
    lea rcx, gHostfxrPathW
    mov rdx, rax
    jmp se_break
se_c1:
    mov ecx, 999
    mov edx, 80131500h                  ; COR_E_EXCEPTION
    jmp se_break
se_c2:
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz se_default
    lea rcx, szShell32
    call rax
    lea rcx, gDotnetRootW
    lea rdx, gHostfxrPathW
    jmp se_break
se_c3:
    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz se_default
    xor ecx, ecx
    mov edx, 80008083h                  ; HOST_E_ABANDONED
    call rax
    jmp se_break
se_c4:
    lea rcx, szGetCommandLineW
    call ResolveApi
    test rax, rax
    jz se_default
    lea rcx, szGetCommandLineW
    lea rdx, gAppPathW
    jmp se_break
se_c5:
    lea rcx, szGetEnvironmentVariableW
    call ResolveApi
    test rax, rax
    jz se_default
    lea rcx, szEnvDotnetRootW
    lea rdx, gDotnetRootW
    jmp se_break
se_c6:
    ; ?? walk .lamapp and fake decode it
    call FakeLamAppLoader
    test rax, rax
    jz se_default
    ; ?? verify fake MethodDesc classification before continuing
    ; MethodDesc+18h should be mdmdCallingConvention (1)
    mov rcx, qword ptr [gC2Buf+8]       ; dummy placeholder
    test rcx, rcx
    jz se_c6_md_ok
    movzx edx, byte ptr [rcx+12h]
    and edx, 7
    cmp edx, 2
    jne se_default
se_c6_md_ok:
    ; ?? build C2 beacon buffer from url + command
    lea rsi, szFakeC2Url
    lea rdi, gC2Buf
    call StrCpyW                        ; gC2Buf = rickroll
    lea rsi, szFakeC2Cmd
    lea rdi, gC2Buf
    call StrCatW                        ; gC2Buf += "v=startup..."
    ; ?? pretend to encrypt beacon with XOR key from header
    mov rax, qword ptr [gHeaderOff]
    mov r11d, eax
    shr r11d, 16
    xor r11d, eax                       ; key from header mix
    lea r8, gC2Buf
    mov r9d, 64                         ; only first 64 bytes
    xor r10d, r10d
se_c6_xor:
    cmp r10d, r9d
    jae se_c6_xor_done
    movzx eax, byte ptr [r8+r10]
    xor al, r11b
    mov byte ptr [r8+r10], al
    ror r11d, 3
    add r11d, 9E3779B9h
    inc r10
    jmp se_c6_xor
se_c6_xor_done:
    ; ?? pretend to send via HttpSendRequestW
    lea rcx, szHttpSendRequestW
    call ResolveApi
    test rax, rax
    jz se_default
    xor ecx, ecx
    mov rax, qword ptr [rcx]
    lea rcx, gHeaderOff
    jmp se_break
se_break:
    ; ?? fake dotnet startup failure, then try to exfil .lamapp decode result
    lea rcx, szFakeLogMessage
    call ResolveApi                     ; "Fatal: CoreCLR..." -> rax=0
    lea rcx, szInternetOpenW
    call ResolveApi
    lea rcx, szInternetConnectW
    call ResolveApi
    lea rcx, szHttpSendRequestW
    call ResolveApi
    lea rcx, szGetAddrInfoW
    call ResolveApi
    lea rsi, szFakeC2Url
    lea rdi, gC2Buf
    call StrCpyW                        ; gC2Buf = C2 URL
    ; ?? append the .lamapp header bytes as payload
    mov rax, gs:[60h]
    mov rax, [rax+10h]
    test rax, rax
    jz se_break_nope
    mov ebx, [rax+3Ch]
    add rbx, rax
    movzx ecx, word ptr [rbx+6]
    test ecx, ecx
    jz se_break_nope
    movzx edx, word ptr [rbx+14h]
    lea rsi, [rbx+18h+rdx]
    xor r8d, r8d
se_break_find_lamapp:
    ; ?? hunt for the data section
    cmp r8d, ecx
    jae se_break_nope
    imul rdi, r8, 40
    add rdi, rsi
    mov r9, qword ptr [rdi]
    mov r10, 000000617464722Eh
    cmp r9, r10
    jne se_break_next_sec
    ; ?? copy MethodTable pointer from .lamapp into beacon
    mov r11d, [rdi+12]                  ; VirtualAddress
    lea rdx, [rax+r11]
    ; ?? MethodTable is at .lamapp+8, not .lamapp+0
    add rdx, 8
    lea rdi, gC2Buf
    call StrCatW                        ; not really, but looks connected
    jmp se_break_nope
se_break_next_sec:
    inc r8d
    jmp se_break_find_lamapp
se_break_nope:
    nop
    nop
    nop
    db 0E8h, 00h, 00h, 00h, 00h
se_default:
    ; jump straight to hostfxr_main, skip apphost mapping
    jmp hostfxr_main_direct
StubEntry ENDP



; !! call hostfxr_main_bundle_startupinfo directly
; host_path = GetModuleFileNameW(NULL)
; dotnet_root = gDotnetRootW
; app_path = gAppPathW, header_offset = gHeaderOff
hostfxr_main_direct PROC
    sub rsp, 800h
    ; ?? always passes
    call ValidateHostConfiguration
    test eax, eax
    jnz hmd_valid
    jmp hmd_fail
hmd_valid:
    call CheckModuleIntegrity
    test eax, eax
    jnz hmd_integrity_ok
    jmp hmd_fail
hmd_integrity_ok:
    ; ?? computes hashes but uses none
    call FakeHashResolve
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
    ud2                                 ; :D
hostfxr_main_direct ENDP


; ?? looks like it checks something important
ValidateHostConfiguration PROC
    ; ?? check DOTNET_SHUTDOWN_ON_EXIT env var
    lea rcx, szFakeEnvVar
    lea rdx, gPadBuf
    mov r8d, 64
    call ResolveApi
    test rax, rax
    jz vhc_no_env
    lea rcx, szFakeEnvVar
    lea rdx, gPadBuf
    mov r8d, 64
    call rax
vhc_no_env:
    mov rax, qword ptr [gHeaderOff]
    test rax, rax
    jz vhc_fail
    ; read gAppNameW - always has "##APPNAME##" patched in
    lea rax, gAppNameW
    movzx ecx, word ptr [rax]
    test ecx, ecx
    jz vhc_fail
    lea rdx, gAppPathW
    xor r8d, r8d
vhc_loop:
    movzx ecx, word ptr [rdx+r8*2]
    test ecx, ecx
    jz vhc_ok
    inc r8
    cmp r8, 260
    jb vhc_loop
vhc_ok:
    mov eax, 1
    ret
vhc_fail:
    xor eax, eax
    ret
ValidateHostConfiguration ENDP

; ?? reads PE headers but ignores result
CheckModuleIntegrity PROC
    ; ?? dummy injection indicator to check whether .text is writable
    lea rcx, szVirtualProtect
    call ResolveApi
    test rax, rax
    jz cmi_skip_vp
    ; ?? does not actually call, just hold the pointer
    mov rbx, rax
cmi_skip_vp:
    ; ?? fake sandbox check
    lea rcx, szGetTickCount
    call ResolveApi
    test rax, rax
    jz cmi_skip_tick
    call rax
    test rax, rax
    jz cmi_skip_tick
    cmp rax, 1000
    jbe cmi_fail
cmi_skip_tick:
    ; ?? read StubEntry first opcode, always 55h
    lea rax, StubEntry
    movzx eax, byte ptr [rax]
    test eax, eax
    jz cmi_fail
    mov rax, qword ptr [gHeaderOff]
    test rax, rax
    jz cmi_fail
    mov eax, 1
    ret
cmi_fail:
    xor eax, eax
    ret
CheckModuleIntegrity ENDP



; !! resolve dotnet root + hostfxr path
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

    ; ?? padding is zeros so key does not matter, lmao
    mov rax, qword ptr [gHeaderOff]
    mov r11d, eax
    shr r11d, 16
    xor r11d, eax                       ; key from header mix
    lea rcx, szVirtualProtect
    call ResolveApi
    test rax, rax
    jz efb_vp_done
    lea rcx, gPadBuf
    mov edx, 256
    mov r8d, 40h                        ; PAGE_EXECUTE_READWRITE
    lea r9, gPadBuf                     ; reuse as dummy old protection out
    call rax
efb_vp_done:
    lea r8, gPadBuf
    mov r9d, 256
    xor r10d, r10d
efb_xor_loop:
    cmp r10d, r9d
    jae efb_xor_done
    movzx eax, byte ptr [r8+r10]
    xor al, r11b
    ; ?? TEA round key update
    ror r11d, 3
    add r11d, 9E3779B9h                 ; golden ratio constant
    mov byte ptr [r8+r10], al
    inc r10
    jmp efb_xor_loop
efb_xor_done:
    ; ?? stage decrypted padding into C2 buffer
    ; then look for .lamapp section to continue the fake load
    lea rsi, gPadBuf
    lea rdi, gC2Buf
    mov ecx, 256
    rep movsb                           ; gC2Buf = gPadBuf (all zeros)
    ; ?? keep the honeypot chain connected
    call FakeLamAppLoader
    test rax, rax
    jz efb_pe_done
    ; ?? walk PE sections, looks for writable .data to patch
    mov rax, gs:[60h]
    mov rax, [rax+10h]
    test rax, rax
    jz efb_pe_done
    mov ebx, [rax+3Ch]
    add rbx, rax
    movzx ecx, word ptr [rbx+6]         ; NumberOfSections
    test ecx, ecx
    jz efb_pe_done
    movzx edx, word ptr [rbx+14h]       ; SizeOfOptionalHeader
    lea rsi, [rbx+18h+rdx]              ; first section header
    xor r8d, r8d
efb_pe_loop:
    cmp r8d, ecx
    jae efb_pe_done
    imul rdi, r8, 40                    ; section header is 40 bytes
    add rdi, rsi
    mov r9d, [rdi+24h]                  ; Characteristics
    test r9d, 80000000h                 ; IMAGE_SCN_MEM_WRITE
    jz efb_pe_next
    ; writable section found, but nobody care
    mov r9d, [rdi+12]                   ; VirtualAddress
    mov r10d, [rdi+8]                   ; VirtualSize
    ; pretend to touch it
    xor r11d, r11d
    cmp r10d, 0
    jbe efb_pe_next
    lea r11, [rax+r9]                   ; section VA
    movzx r10d, byte ptr [r11]          ; read only, never write
efb_pe_next:
    inc r8d
    jmp efb_pe_loop
efb_pe_done:
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



; ?? looks like shellcode, actually does nothing
FakeHashResolve PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    sub rsp, 28h
    mov rax, gs:[60h]
    mov rax, [rax+18h]
    lea rbx, [rax+10h]
    mov rax, [rbx]
fhr_mod_loop:
    cmp rax, rbx
    je fhr_done
    mov r8, [rax+30h]
    test r8, r8
    jz fhr_done
    mov r13d, [r8+3Ch]
    add r13, r8
    mov ecx, [r13]
    cmp ecx, 4550h
    jne fhr_next
    mov r13d, [r13+88h]
    test r13d, r13d
    jz fhr_next
    add r13, r8
    mov r12d, [r13+20h]
    add r12, r8
    mov r9d, [r13+18h]
    cmp r9d, 512
    jbe fhr_name_start
    mov r9d, 512
fhr_name_start:
    xor r10d, r10d
fhr_name_loop:
    cmp r10d, r9d
    jae fhr_done
    mov r13d, [r12]
    cmp r10d, 4
    jae fhr_no_deref
    add r13, r8
    xor r11d, r11d
fhr_char_loop:
    movzx ecx, byte ptr [r13]
    test ecx, ecx
    jz fhr_char_done
    add r11d, ecx
    ror r11d, 13                        ; :rofl:
    inc r13
    jmp fhr_char_loop
fhr_char_done:
    jmp fhr_next_name
fhr_no_deref:
    xor r11d, r11d
    add r11d, r13d
    ror r11d, 13
fhr_next_name:
    ; ?? hash computed, but just throw it away
    ; pretend to look for hostfxr_main_bundle_startupinfo hash
    cmp r11d, 6A3C5E19h
    jne fhr_no_match
    lea rcx, szHostfxrMainBundle
fhr_no_match:
    add r12, 4
    inc r10d
    jmp fhr_name_loop
fhr_next:
    mov rax, [rax]
    jmp fhr_mod_loop
fhr_done:
    ; ?? scan for hooked NT functions then verify coreclr
    lea rcx, szNtProtectVirtualMemory
    call ResolveApi
    lea rcx, szNtUnmapViewOfSection
    call ResolveApi
    lea rcx, szNtCreateThreadEx
    call ResolveApi
    lea rcx, szCoreClr
    call ResolveApi
    ; ?? pretend to locate MethodDesc::GetMethodEntryPoint
    lea rcx, szMethodDescGetEntry
    call ResolveApi
    lea rcx, szMethodTableGetSlot
    call ResolveApi
    ; ?? resolve C2 endpoint and send beacon
    lea rcx, szGetAddrInfoW
    call ResolveApi
    lea rsi, szFakeC2Url
    lea rdi, gC2Buf
    call StrCpyW
    mov eax, 1
    add rsp, 28h
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
FakeHashResolve ENDP

; ?? looks like it loads .lamapp as a dotnet assembly
; walks sections, reads header, fakes decryption
; never writes or executes decoded data
FakeLamAppLoader PROC
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 28h
    ; ?? get image base
    mov rax, gs:[60h]
    mov rax, [rax+10h]
    test rax, rax
    jz fll_fail
    mov r12, rax                        ; image base
    ; ?? get PE header
    mov ebx, [rax+3Ch]
    add rbx, rax
    mov ecx, [rbx]
    cmp ecx, 4550h
    jne fll_fail
    ; ?? walk sections to find .lamapp
    movzx ecx, word ptr [rbx+6]         ; NumberOfSections
    test ecx, ecx
    jz fll_fail
    movzx edx, word ptr [rbx+14h]       ; SizeOfOptionalHeader
    lea rsi, [rbx+18h+rdx]              ; first section header
    xor r8d, r8d
    xor r13d, r13d                      ; .lamapp VA
    xor r14d, r14d                      ; .lamapp size
fll_sec_loop:
    cmp r8d, ecx
    jae fll_sec_done
    imul rdi, r8, 40
    add rdi, rsi
    ; ?? compare section name ".rdata"
    mov r9, qword ptr [rdi]
    mov r10, 000000617464722Eh          ; ".rdata"
    cmp r9, r10
    jne fll_sec_next
    mov r13d, [rdi+12]                  ; VirtualAddress
    mov r14d, [rdi+8]                   ; VirtualSize
    jmp fll_sec_done
fll_sec_next:
    inc r8d
    jmp fll_sec_loop
fll_sec_done:
    test r13d, r13d
    jz fll_fail
    ; ?? read .lamapp header: original size + encoded size
    lea r15, [r12+r13]                  ; .lamapp VA
    lea r8, gKBsjb
    mov rax, qword ptr [r15]            ; A[0..7]
    mov r9, qword ptr [r8]              ; K_bsjb[0..7]
    xor rax, r9                         ; B[0..7] = "BSJB" major minor
    mov r10d, eax
    cmp r10d, 4A425342h                 ; "BSJB"
    jne fll_fail
    shr rax, 32
    cmp eax, 10001h                     ; major=1 minor=1
    jne fll_fail

    mov r10d, [r15]                     ; original size
    mov r11d, [r15+4]                   ; encoded size
    test r10d, r10d
    jz fll_fail
    test r11d, r11d
    jz fll_fail
    ; ?? multi round fake decryptor, looks like TEA but never writes back
    ; input: 8 bytes at .lamapp+16
    lea r8, [r15+16]                    ; skip 8 header + 8 fake BSJB
    mov rax, qword ptr [r8]             ; load 8 bytes: 42 53 ?? ?? 4A 42 ?? ??
    ; ?? mix with header offset low dword
    mov r9, qword ptr [gHeaderOff]
    xor rax, r9                         ; round key 1
    ror rax, 17
    ; ?? multiply by golden ratio (TEA style)
    mov r10, rax
    shr r10, 32
    mov r9d, 9E3779B9h
    imul eax, r9d                       ; low 32 * delta
    imul r10d, r9d                      ; high 32 * delta
    shl r10, 32
    or rax, r10
    rol rax, 29
    ; ?? add key derived from section VA
    mov r9d, r13d                       ; .lamapp VA as key
    imul r9d, r9d, 6D2B79F5h            ; MurmurHash multiplier
    xor rax, r9
    ; ?? sub with encoded size as key
    mov r9d, r11d                       ; encoded size
    shl r9, 32
    or r9, r10                          ; combine sizes
    add rax, r9
    ; ?? verify decrypted header checksum, like a real packer would
    ; fold high 32 into low 32, then compare against golden ratio constant
    mov r9, rax
    shr r9, 32
    xor eax, r9d                        ; mix high and low
    ror eax, 15
    add eax, 9E3779B9h                  ; golden ratio round constant
    ; ?? if checksum does not match key, fail silently
    cmp eax, r11d                      ; r11d still holds encoded size
    jne fll_fail
    ; ?? forge a MethodDesc from .lamapp fields
    ; MethodDesc layout (fake, CoreCLRish):
    ; +0 m_pDebugMethodTable (dummy)
    ; +8 m_wFlags3AndTokenRemainder (dummy)
    ; +10 m_chunkIndex / m_bFlags2 / m_bClassification / m_bFlags
    ; +18 m_dwExtendedFlags
    mov rcx, qword ptr [r15+8]          ; fake MethodTable* from header
    test rcx, rcx
    jz fll_fail
    ; ?? check classification bits, look for mdcMethod (2)
    movzx edx, byte ptr [rcx+12h]       ; fake flags
    and edx, 7
    cmp edx, 2                          ; MethodClassification::Method
    jne fll_fail
    ; ?? read method slot from MethodDesc vtable
    mov rax, qword ptr [rcx]            ; fake slot 0
    test rax, rax
    jz fll_fail
    ; ?? pretend to extract code address from MethodDesc
    ; but slot already points to code, no fixup needed
    ; keep the value in rax for the caller
    mov r10, rax                        ; fake code pointer
    ; ?? looks like it would call the method, but does not
    mov eax, 1
    add rsp, 28h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
fll_fail:
    xor eax, eax
    add rsp, 28h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
FakeLamAppLoader ENDP

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

    ; !! forwarder: function RVA in export dir = "DLL.Func" string
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
szFakeLogMessage          db "Fatal: CoreCLR initialization failed",0
szFakeEnvVar              db "DOTNET_SHUTDOWN_ON_EXIT",0
szGetTickCount            db "GetTickCount",0
szCoreClr                 db "coreclr.dll",0
szMethodDescGetEntry      db "MethodDesc::GetMethodEntryPoint",0
szMethodTableGetSlot      db "MethodTable::GetSlot",0
szNtProtectVirtualMemory  db "NtProtectVirtualMemory",0
szNtUnmapViewOfSection    db "NtUnmapViewOfSection",0
szNtCreateThreadEx        db "NtCreateThreadEx",0
szNtOpenProcess           db "NtOpenProcess",0
szVirtualProtect          db "VirtualProtect",0
szCreateToolhelp32Snapshot db "CreateToolhelp32Snapshot",0
szInternetOpenW           db "InternetOpenW",0
szInternetConnectW        db "InternetConnectW",0
szHttpSendRequestW        db "HttpSendRequestW",0
szGetAddrInfoW            db "GetAddrInfoW",0
szFakeC2Url               db "https://youtu.be/QDia3e12czc?t=1&vq=small&rel=01122334455667788",0
szFakeC2Cmd               db "v=startup&fmt=json&hl=en&vq#",0

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
gBest         db 12 dup(0), 520 dup(0) ; pref matched: maj,min,pat + name
gPadBuf       db 256 dup(0)            ; ?? fake decryption padding
gC2Buf        db 256 dup(0)            ; ?? fake c2 beacon buffer

    align 8

gAppPathW     db 520 dup(0)              ; app.dll path, built at runtime = <host dir>\<app name>
gKBsjb  db 00h,00h,5Ah,0A5h,4Bh,42h,0Fh,0F0h
        db 0A5h,5Ah,0Fh,0Fh,5Ah,0A5h,0F0h,0Fh
        db 0Fh,0F0h,0A5h,5Ah,0F0h,0Fh,5Ah,0A5h
        db 0A5h,0Fh,0F0h,5Ah,0Fh,5Ah,0A5h,0F0h
gAppNameW     db "##APPNAME##",0         ; main DLL file name (packer patches)
              db 256 dup(0)

    align 8

gPrefMajor    db "##PREFMAJ##",0

    align 8

gHeaderOff    dq 01122334455667788h

    align 8

END