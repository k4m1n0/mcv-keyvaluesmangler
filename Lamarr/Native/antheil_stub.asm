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
    ; decrypt API strings (disk cipher -> mem plain)
    lea rdi, gStrStart
    add rdi, 8
    lea rcx, gStrEnd
    sub rcx, rdi
    mov esi, dword ptr [gStrKey]
str_dec_loop:
    test rcx, rcx
    jz str_dec_done
    mov al, byte ptr [rdi]
    xor al, sil
    mov byte ptr [rdi], al
    imul esi, esi, 13h
    add esi, 5Ah
    inc rdi
    dec rcx
    jmp str_dec_loop
str_dec_done:
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
    ; debugger detected, "malicious" revenge path
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
; host_path = GetModuleFileNameW(NULL) dotnet_root = gDotnetRootW app_path = gAppPathW header_offset = gHeaderOff
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
    push rax                            ; ?? extra push, recovered by add
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
    add rsp, 8
    lea rsi, gAppNameW
    call StrCpyW                        ; overwrite filename with main DLL name

    call ResolveDotnetRoot
    call EnumFxrBestMatch

    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz hmd_fail
    mov r12, rax
    lea rcx, gHostfxrA
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

    ; argv from GetCommandLineW + CommandLineToArgvW argc=1 on failure
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
    test eax, eax
    jz hmd_exit                         ; the payload ran (or exited through its own path)
    ; the host could not start the payload report it the way the official apphost does before the process vanishes
    mov ecx, r12d
    call FailReport
hmd_exit:
    lea rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz hmd_dead
    mov ecx, r12d
    call rax
hmd_fail:
    ; no hostfxr or no .NET root leaves no host text so use the official "install .NET" wording
    call FailReportNoHost
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
    ; read gAppNameW, always has "##APPNAME##" patched in
    lea rax, gAppNameW
    movzx ecx, word ptr [rax]
    test ecx, ecx
    jz vhc_fail
    lea rdx, gAppPathW
    xor r8d, r8d
    push rax                            ; ?? extra push, recovered by add
vhc_loop:
    movzx ecx, word ptr [rdx+r8*2]
    test ecx, ecx
    jz vhc_ok
    inc r8
    cmp r8, 260
    jb vhc_loop
vhc_ok:
    add rsp, 8
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
; dotnet root comes from the env then the registry then the default hostfxr from scanning <root>\host\fxr preferring a gPrefMajor match
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
    lea rcx, szLoadLibraryA
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
    mov edx, eax
    shr r11d, 16
    xor r11d, edx                       ; key from header mix (edx holds low32)
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
    push rax                            ; ?? extra push, recovered by add
    mov r15d, r9d
efb_xor_loop:
    test r15d, r15d
    jz efb_xor_done
    dec r15d
    movzx eax, byte ptr [r8+r15]
    xor al, r11b
    mov byte ptr [r8+r15], al
    rol r11d, 29                        ; ror 3, other form
    add r11d, 9E3779B9h                 ; golden ratio constant
    jmp efb_xor_loop
efb_xor_done:
    add rsp, 8
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
    mov dword ptr [gFall], 0
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

    lea rcx, gFindData+2Ch
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
    lea rsi, gBest+0Ch                  ; use pref matched
efb_use:
    lea rdi, gHostfxrPathW
    call StrCatW
    lea rsi, szHostfxrDllW
    lea rdi, gHostfxrPathW
    call StrCatW

    lea rsi, gHostfxrPathW
    lea rdi, gHostfxrA
    call WideToAnsi

    call TierGate                       ; tiered policy for the runtime we are about to load
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

; nothing is forced here the scan only fills gTierVer and restores every touched reg
; r13d/r14d/r15d = picked hostfxr version
TierGate PROC
    push rbx
    push rbp
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 8                          ; keep the stack 16 byte aligned for the calls below
    ; the version that matters is the highest stable framework under shared\Microsoft.NETCore.App which is what rollForward=LatestMajor resolves
    mov dword ptr [gTierVer], 0
    mov dword ptr [gTierVer+4], 0
    mov dword ptr [gTierVer+8], 0
    lea rsi, gDotnetRootW
    lea rdi, gFxrSearchW
    call StrCpyW
    lea rsi, szSharedCoreAppW
    lea rdi, gFxrSearchW
    call StrCatW
    lea rsi, szBackslashW
    lea rdi, gFxrSearchW
    call StrCatW
    lea rsi, szStarW
    lea rdi, gFxrSearchW
    call StrCatW
    lea rcx, szFindFirstFileW
    call ResolveApi
    test rax, rax
    jz tg_done
    mov rbx, rax
    lea rcx, gFxrSearchW
    lea rdx, gFindData
    call rbx
    cmp rax, -1
    je tg_done
    mov qword ptr [gTierFind], rax
tg_loop:
    mov eax, dword ptr [gFindData]
    test eax, 10h                       ; directories only
    jz tg_next
    movzx eax, word ptr [gFindData+2Ch] ; name must start with a digit
    cmp ax, '0'
    jb tg_next
    cmp ax, '9'
    ja tg_next
    lea rcx, gFindData+2Ch              ; no dash means a stable release
tg_prl:
    movzx eax, word ptr [rcx]
    test eax, eax
    jz tg_ver_ok
    cmp ax, '-'
    je tg_next
    add rcx, 2
    jmp tg_prl
tg_ver_ok:
    lea rcx, gFindData+2Ch
    call ParseVerW                      ; eax=major edx=minor r8d=patch
    mov r13d, eax
    mov r14d, edx
    mov r15d, r8d
    ; pref major bucket falls back to the highest stable overall when no major matches
    cmp r13d, dword ptr [gPrefMajor]
    jne tg_high
    mov eax, dword ptr [gTierBest]
    test eax, eax
    jz tg_bnew
    cmp r13d, eax
    ja tg_bnew
    jb tg_high
    mov eax, dword ptr [gTierBest+4]
    cmp r14d, eax
    ja tg_bnew
    jb tg_high
    mov eax, dword ptr [gTierBest+8]
    cmp r15d, eax
    jbe tg_high
tg_bnew:
    mov dword ptr [gTierBest], r13d
    mov dword ptr [gTierBest+4], r14d
    mov dword ptr [gTierBest+8], r15d
tg_high:
    mov eax, dword ptr [gTierVer]       ; keep the highest triple
    cmp r13d, eax
    ja tg_new
    jb tg_next
    mov eax, dword ptr [gTierVer+4]
    cmp r14d, eax
    ja tg_new
    jb tg_next
    mov eax, dword ptr [gTierVer+8]
    cmp r15d, eax
    jbe tg_next
tg_new:
    mov dword ptr [gTierVer], r13d
    mov dword ptr [gTierVer+4], r14d
    mov dword ptr [gTierVer+8], r15d
tg_next:
    lea rcx, szFindNextFileW
    call ResolveApi
    test rax, rax
    jz tg_close
    mov rbx, rax
    mov rcx, qword ptr [gTierFind]
    lea rdx, gFindData
    call rbx
    test rax, rax
    jnz tg_loop
tg_close:
    lea rcx, szFindClose
    call ResolveApi
    test rax, rax
    jz tg_decide
    mov rcx, qword ptr [gTierFind]
    call rax
tg_decide:
    ; the version the stub settled on newest of the PREFMAJ major or the newest stable overall
    lea rsi, gTierVer
    cmp dword ptr [gTierBest], 0
    je tg_pick_copy
    lea rsi, gTierBest
tg_pick_copy:
    mov eax, dword ptr [rsi]
    mov dword ptr [gTierPick], eax
    mov eax, dword ptr [rsi+4]
    mov dword ptr [gTierPick+4], eax
    mov eax, dword ptr [rsi+8]
    mov dword ptr [gTierPick+8], eax
    mov r13d, dword ptr [gTierPick]
    mov r14d, dword ptr [gTierPick+4]
    mov r15d, dword ptr [gTierPick+8]
    ; tiered compilation is left as the runtime resolved it only an explicit operator setting or RtcTiered=off turns it off
tg_done:
    add rsp, 8
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
TierGate ENDP



; rbx = storage (maj,min,pat + name at +0Ch) r13/r14/r15 = version rsi = name
; update when the version is higher
UpdateBest PROC
    push rsi
    push rdi
    push rbx
    mov rax, rbx
    mov rcx, rsi

    mov ebx, [rax]
    cmp r13d, ebx
    jg ub_new
    jl ub_none

    mov ebx, [rax+4]
    cmp r14d, ebx
    jg ub_new
    jl ub_none

    mov ebx, [rax+8]
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



; wide strcpy from rsi to rdi
StrCpyW PROC
    push rsi
    push rdi
    push rax                            ; ?? extra push, recovered by add
scw_l:
    mov ax, [rsi]
    mov [rdi], ax
    test ax, ax
    jz scw_d
    add rsi, 2
    add rdi, 2
    jmp scw_l
scw_d:
    add rsp, 8
    pop rdi
    pop rsi
    ret
StrCpyW ENDP



; wide strcat appends rsi to rdi
StrCatW PROC
    push rsi
    push rdi
astub_slot_0001::
    db 0Fh, 1Fh, 44h, 00h, 00h, 0Fh, 1Fh, 00h
;%poly:junk
scat_l:
    cmp word ptr [rdi], 0
    je scat_f
    add rdi, 2
    jmp scat_l
;%poly:end
scat_f:
    call StrCpyW
    pop rdi
    pop rsi
    ret
StrCatW ENDP



; wide strcmp, rsi vs rdi, eax = 1 if equal
StrEqW PROC
    push rsi
    push rdi
    push rax                            ; ?? extra push, recovered by add
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
;%poly:opseq
    mov eax, 1
;%poly:alt
    mov r11d, 1
    mov eax, r11d
;%poly:end
    jmp seq_d
seq_no:
    xor eax, eax
seq_d:
    add rsp, 8
    pop rdi
    pop rsi
    ret
StrEqW ENDP



; parse "10.0.1" -> eax.edx.r8d
ParseVerW PROC
    push rbx
    push rsi
;%poly:opseq
    mov rsi, rcx
    call ParseNumW
    mov ebx, eax                        ; major
    xor edx, edx
    xor r8d, r8d
;%poly:alt
    mov rsi, rcx
    xor edx, edx
    xor r8d, r8d
    call ParseNumW
    mov ebx, eax                        ; major
;%poly:end

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
    push rax                            ; ?? extra push, recovered by add
    xor eax, eax
    mov ebx, 10
pn_l:
    movzx ecx, word ptr [rsi]
    sub ecx, '0'
    cmp ecx, 9                          ; not a digit?
    ja pn_d
;%poly:opseq
    imul eax, ebx
    add eax, ecx
;%poly:alt
    mov r11d, ecx
    imul eax, ebx
    add eax, r11d
;%poly:end
    add rsi, 2
    jmp pn_l
pn_d:
    add rsp, 8                          ; recover extra push via add
    pop rbx
    ret
ParseNumW ENDP



; wide -> ansi
WideToAnsi PROC
    push rsi
    push rdi
    push rax                            ; ?? extra push, recovered by add
astub_slot_0002::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 0Fh, 1Fh, 00h
;%poly:junk
wta_l:
    movzx eax, word ptr [rsi]
    test eax, eax
    jz wta_d
    mov [rdi], al
    add rsi, 2
    inc rdi
    jmp wta_l
;%poly:end
wta_d:
    mov byte ptr [rdi], 0
    add rsp, 8
    pop rdi
    pop rsi
    ret
WideToAnsi ENDP



; mirrors the official apphost and forwards the host text to stderr event log 1023 and the GUI download dialog buffers allocated once on the failure path
ERR_BUF_MAX  equ 16000                  ; wide chars captured from the host
ANSI_BUF_MAX equ 24000

;%triage:begin
; dev only, LAMARR_LAUNCH_STAGE appends a stage digit and LAMARR_LAUNCH_ERR=<path> dumps the captured text
;%triage:end



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
;%poly:opseq
    mov r12d, [r13+20h]
    add r12, r8
    mov r9d, [r13+18h]
    cmp r9d, 512
    jbe fhr_name_start
    mov r9d, 512
;%poly:alt
    mov r9d, [r13+18h]
    mov r12d, [r13+20h]
    add r12, r8
    cmp r9d, 512
    jbe fhr_name_start
    mov r9d, 512
;%poly:end
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
astub_slot_0003::
    db 0Fh, 1Fh, 80h, 00h, 00h, 00h, 00h, 90h
;%poly:junk
    add r11d, ecx
    ror r11d, 13                        ; :rofl:
    inc r13
;%poly:end
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
; walks sections and reads the header but never writes or executes decoded data
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
;%poly:opseq
    mov ebx, [rax+3Ch]
    add rbx, rax
    mov ecx, [rbx]
    cmp ecx, 4550h
    jne fll_fail
;%poly:alt
    mov ebx, [rax+3Ch]
    lea rbx, [rax+rbx]
    mov ecx, [rbx]
    cmp ecx, 4550h
    jne fll_fail
;%poly:end
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
    ; ?? scan for the obfuscated BSJB header
    lea r15, [r12+r13]                  ; .rdata VA start
    lea r8, gKBsjb
    mov r9, qword ptr [r8]              ; K_bsjb[0..7]
    mov eax, r14d                       ; scan limit = .rdata virtual size
    test eax, eax
    jz fll_fail
    add rax, r15
    mov r14, rax                        ; reuse r14 as scan end
fll_scan:
    cmp r15, r14
    jae fll_fail
    mov rax, qword ptr [r15]            ; A[0..7]
;%poly:opseq
    xor rax, r9                         ; B[0..7] = "BSJB" major minor
    mov r10d, eax
    cmp r10d, 4A425342h                 ; "BSJB"
    jne fll_scan_next
;%poly:alt
    xor rax, r9
    cmp eax, 4A425342h                  ; "BSJB"
    mov r10d, eax
    jne fll_scan_next
;%poly:end
    shr rax, 32
    cmp eax, 10001h                     ; major=1 minor=1
    je fll_scan_found
fll_scan_next:
    add r15, 8
    jmp fll_scan
fll_scan_found:
    mov r10d, [r15]                     ; original size
    mov r11d, [r15+4]                   ; encoded size
    test r10d, r10d
    jz fll_fail
    test r11d, r11d
    jz fll_fail
    ; ?? multi round fake decryptor, looks like TEA but never writes back
    ; input is 8 bytes at .lamapp+16
;%poly:regset
    lea r8, [r15+16]                    ; skip 8 header + 8 fake BSJB
    mov rax, qword ptr [r8]             ; load 8 bytes, 42 53 ?? ?? 4A 42 ?? ??
    ; ?? mix with header offset low dword
    mov r9, qword ptr [gHeaderOff]
    xor rax, r9                         ; round key 1
;%poly:alt
    lea r10, [r15+16]
    mov rax, qword ptr [r10]
    ; ?? mix with header offset low dword
    mov r11, qword ptr [gHeaderOff]
    xor rax, r11
;%poly:end
    ror rax, 17
    ; ?? multiply by golden ratio (TEA style)
;%poly:opseq
    mov r10, rax
    shr r10, 32
    mov r9d, 9E3779B9h
    imul eax, r9d                       ; low 32 * delta
    imul r10d, r9d                      ; high 32 * delta
    shl r10, 32
    or rax, r10
    rol rax, 29
;%poly:alt
    mov r10, rax
    shr r10, 32
    mov r9d, 9E3779B9h
    imul r10d, r9d                      ; high first
    shl r10, 32
    imul eax, r9d                       ; low 32 * delta
    or rax, r10
    rol rax, 29
;%poly:end
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
;%poly:opseq
    mov r9, rax
    shr r9, 32
    xor eax, r9d                        ; mix high and low
    ror eax, 15
    add eax, 9E3779B9h                  ; golden ratio round constant
;%poly:alt
    mov r9, rax
    mov r10d, eax
    shr r9, 32
    xor r10d, r9d
    rol r10d, 17
    add r10d, 9E3779B9h
    mov eax, r10d
;%poly:alt
    mov r11, rax
    mov r10d, eax
    shr r11, 32
    xor r10d, r11d
    rol r10d, 17
    add r10d, 9E3779B9h
    mov eax, r10d
;%poly:end
    ; ?? if checksum does not match key, fail silently
    cmp eax, r11d                       ; r11d still holds encoded size
    jne fll_fail
    ; ?? forge a MethodDesc from .lamapp fields
    ; fake MethodDesc CoreCLR offsets
    ; +0 m_pDebugMethodTable +8 m_wFlags3AndTokenRemainder +10 m_chunkIndex/m_bFlags2/m_bClassification/m_bFlags +18 m_dwExtendedFlags
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
    ; but slot already points to code so no fixup is needed and rax stays for the caller
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



; rcx = module handle rdx = ansi export name -> rax = function or 0 resolved from kernel32 once and cached
; the name match includes the module tail so a bare "user32.dll" never matches
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


; shared export lookup rdx = module base rdi = name -> rax = func addr or 0
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



gStrStart                 db "##STRST##"

    align 8

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
; RtcTiered=off sets TieredCompilation=false in the runtimeconfig
szSharedCoreAppW  dw 5Ch,'s','h','a','r','e','d',5Ch,'M','i','c','r','o','s','o','f','t','.','N','E','T','C','o','r','e','.','A','p','p',0
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

gStrEnd                   db "##STREN##"

    align 8

gDotnetRootW  db 520 dup(0)             ; dotnet root (wide)
gHostfxrPathW db 520 dup(0)             ; hostfxr.dll full path (wide)
gHostfxrA     db 520 dup(0)             ; ANSI for LoadLibraryA
gFxrDirW      db 520 dup(0)             ; <root>\host
gFxrSearchW   db 520 dup(0)             ; <root>\host\fxr\*
gFindData     db 640 dup(0)             ; WIN32_FIND_DATAW
gTierFind     dq 0                      ; find handle used by TierGate
gTierVer      dd 0,0,0                  ; highest framework version seen by TierGate
gTierBest     dd 0,0,0                  ; newest framework inside the PREFMAJ major (same rule as gBest)
gTierPick     dd 0,0,0                  ; gTierBest when present, else gTierVer
gFall         db 12 dup(0), 128 dup(0)  ; fallback, maj + min + patch + name
gBest         db 12 dup(0), 128 dup(0)  ; pref matched, maj + min + patch + name
gPadBuf       db 256 dup(0)             ; ?? fake decryption padding
gC2Buf        db 256 dup(0)             ; ?? fake c2 beacon buffer

    align 8

gAppPathW     db 520 dup(0)             ; app.dll path, built at runtime = <host dir>\<app name>
gKBsjb        db "##KBSJB##"
              db 24 dup(0)
    align 8
gStrKey       dq 0C0DE5EEDC0DE5EEDh
gAppNameW     db "##APPNAME##",0        ; main DLL file name (packer patches)
              db 256 dup(0)

    align 8

gPrefMajor    db "##PREFMAJ##",0

    align 8

gHeaderOff    dq 01122334455667788h

    align 8

; placed after every patched marker and outside both scramble ranges so the literals survive and the .bss buffers cost no file size
szWriteFile      db "WriteFile",0
szRegisterEventSourceW db "RegisterEventSourceW",0
szReportEventW   db "ReportEventW",0
szDeregisterEventSourceW db "DeregisterEventSourceW",0
szUser32         db "user32.dll",0
szMessageBoxW    db "MessageBoxW",0
szShellExecuteW  db "ShellExecuteW",0
szGetEnvA        db "GetEnvironmentVariableA",0
szVirtualAlloc   db "VirtualAlloc",0
szEnvNoGuiA      db "DOTNET_DISABLE_GUI_ERRORS",0
;%triage:begin
szLaunchErrEnv   db "LAMARR_LAUNCH_ERR",0
szLaunchStageEnv db "LAMARR_LAUNCH_STAGE",0
szCreateFileA    db "CreateFileA",0
szCloseHandle    db "CloseHandle",0
;%triage:end
szNetRuntimeW    dw '.','N','E','T',' ','R','u','n','t','i','m','e',0
szOpenW          dw 'o','p','e','n',0
szLogIntroA      db "Description: A .NET application failed.",13,10,0
szLogAppA        db "Application: ",0
szLogPathA       db "Path: ",0
szLogMsgA        db "Message: ",0
szInsGenericA    db "You must install or update .NET to run this application.",0
szInsDesktopA    db "You must install .NET Desktop Runtime to run this application.",0
szLearnA         db 13,10,"Learn more:",13,10,"https://aka.ms/dotnet/app-launch-failed",13,10,13,10,0
szAskDownloadA   db "Would you like to download it now?",0
szDesktopFwA     db "Microsoft.WindowsDesktop.App",0
szUrlPrefixA     db "https://aka.ms/dotnet-core-applaunch?framework=",0
szGuiSuffixA     db "&gui=true",0
szCrLfA          db 13,10,0
szLfA            db 10,0
szDownLinkLfA    db "Download link:",10,0
szFwHeadA        db "Framework: 'Microsoft.NETCore.App', version '",0
szFwTailA        db ".0.0' (x64)",0
szLearnLinkA     db "Learn more:",13,10,"https://aka.ms/dotnet/app-launch-failed",13,10,13,10,0
szDownLinkA      db "Download link:",13,10,0
szUrlFwA         db "Microsoft.NETCore.App",0
szUrlVerA        db "&framework_version=",0
szUrlTailA       db ".0.0&arch=x64&rid=win-x64&os=win10&gui=true",0
; visual styles need the shell manifest, the dialog is comctl32 v6
szGetWindowsDirectoryW db "GetWindowsDirectoryW",0
szCreateActCtxW  db "CreateActCtxW",0
szActivateActCtx db "ActivateActCtx",0
szComctl32A      db "comctl32.dll",0
szTaskDialogA    db "TaskDialogIndirect",0
szShellManifestW dw '\','W','i','n','d','o','w','s','S','h','e','l','l','.','M','a','n','i','f','e','s','t',0
szTdReqA         db "Required: 'Microsoft.NETCore.App', version '",0
; the dialog markup separates its lines with a bare LF, the report text
szLearnHeadA     db "Learn more:",10,0
szLearnUrlA      db "https://aka.ms/dotnet/app-launch-failed",0
szHrefPreA       db "<A HREF=""",0
szHrefMidA       db """>",0
szHrefEndA       db "</A>",0
szDownloadBtnA   db "Download it now",0

    align 8

; the packer keeps only stub .text plus payload .rdata so the reporting buffers are VirtualAlloc'd on failure and nothing is allocated unless a launch fails
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
REP_OFF_TDANSI equ 233472               ; task dialog content (ansi)
REP_OFF_TDWIDE equ 236544               ; task dialog content (wide)
REP_OFF_WINS   equ 239616               ; wide main instruction
REP_OFF_WBTN   equ 240896               ; wide label of the custom button
REP_OFF_WDIR   equ 242176               ; wide <windir>
REP_OFF_MANIF  equ 243200               ; wide WindowsShell manifest path
REP_OFF_ACT    equ 244736               ; ACTCTXW, 0x38
REP_OFF_TDCFG  equ 244864               ; TASKDIALOGCONFIG, its button array and the out dword
REP_OFF_COOKIE equ 245184               ; ActivateActCtx cookie
REP_TOTAL      equ 245264

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
gAnsiTdP      dq 0                      ; task dialog content (ansi, then converted)
gWideTdP      dq 0
gWideInsP     dq 0                      ; wide main instruction
gWideBtnP     dq 0                      ; wide custom button label
gWinDirP      dq 0
gManifP       dq 0
gActP         dq 0                      ; ACTCTXW
gTdCfgP       dq 0                      ; TASKDIALOGCONFIG block
gActCookie    dq 0
gTdHr         dd 0                      ; task dialog hresult
gEvtArg       dq 0                      ; single pointer array for ReportEventW
gErrUsedW     dd 0                      ; chars captured (0 = none)
gAnsiStat     db 192 dup(0)             ; static ansi scratch (major digits, env var values)
gGetProcAddr  dq 0                      ; cached GetProcAddress
gApi1         dq 0                      ; staging storage for resolved pointers
gApi2         dq 0                      ; staging storage for resolved pointers


; rcx = ansi string -> rax = length
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



; rsi = ansi src -> rdi = ansi dst (copies the terminator too)
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



; rsi = ansi src appended to the ansi string at rdi
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

; like StrCatA but doubles every '&' ('&' is the accelerator character in task dialog text)
; rsi = ansi haystack rdi = ansi needle -> rax = first match or 0
StrCatAmpA PROC
    push rsi
    push rdi
sca_find:
    cmp byte ptr [rdi], 0
    je sca_cp
    inc rdi
    jmp sca_find
sca_cp:
    mov al, byte ptr [rsi]
    test al, al
    jz sca_d
    mov byte ptr [rdi], al
    inc rdi
    cmp al, '&'
    jne sca_next
    mov byte ptr [rdi], al
    inc rdi
sca_next:
    inc rsi
    jmp sca_cp
sca_d:
    mov byte ptr [rdi], 0
    pop rdi
    pop rsi
    ret
StrCatAmpA ENDP



; rsi = ansi src -> rdi = wide dst (terminator included), literals stay ascii
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



; allocates the reporting buffers once (failure path only)
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
    lea rax, [rbx+REP_OFF_TDANSI]
    mov qword ptr [gAnsiTdP], rax
    lea rax, [rbx+REP_OFF_TDWIDE]
    mov qword ptr [gWideTdP], rax
    lea rax, [rbx+REP_OFF_WINS]
    mov qword ptr [gWideInsP], rax
    lea rax, [rbx+REP_OFF_WBTN]
    mov qword ptr [gWideBtnP], rax
    lea rax, [rbx+REP_OFF_WDIR]
    mov qword ptr [gWinDirP], rax
    lea rax, [rbx+REP_OFF_MANIF]
    mov qword ptr [gManifP], rax
    lea rax, [rbx+REP_OFF_ACT]
    mov qword ptr [gActP], rax
    lea rax, [rbx+REP_OFF_TDCFG]
    mov qword ptr [gTdCfgP], rax
    lea rax, [rbx+REP_OFF_COOKIE]
    mov qword ptr [gActCookie], rax
erb_done:
    add rsp, 20h
    pop rbx
    ret
EnsureRepBufs ENDP



; ecx = host status, writes the event log entry, then the GUI dialog
FailReport PROC
    push rbx
    push rdi
    push rsi
    sub rsp, 20h
    mov ebx, ecx
;%triage:begin
    mov ecx, 1                          ; FailReport entered
    call TriageStage
;%triage:end
    call EnsureRepBufs
;%triage:begin
    mov ecx, 2                          ; buffers ensured
    call TriageStage
;%triage:end
    cmp qword ptr [gAnsiDlgP], 0
    je fr_done                          ; no buffers, keep the host status instead of faulting
    call BuildFailText                  ; official wording, framework line, links, download url
;%triage:begin
    mov ecx, 3                          ; text built
    call TriageStage
;%triage:end
    call WriteEventLog                  ; official event log entry (source .NET Runtime, id 1023)
;%triage:begin
    mov ecx, 4                          ; event log written
    call TriageStage
;%triage:end
;%triage:begin
    ; dev only, LAMARR_LAUNCH_ERR=<path> dumps the same text
    call TriageWriteLogFile
;%triage:end
    call ShowFailDialogIfGui
;%triage:begin
    mov ecx, 5                          ; dialog path done
    call TriageStage
;%triage:end
fr_done:
    add rsp, 20h
    pop rsi
    pop rdi
    pop rbx
    ret
FailReport ENDP



; no hostfxr or no .NET root, the official apphost has no text either
FailReportNoHost PROC
    sub rsp, 28h
    mov ecx, 80008096h                  ; FrameworkMissingFailure, same status the host uses
    call FailReport
    add rsp, 28h
    ret
FailReportNoHost ENDP



; fills the capture buffer with the official "install .NET" wording
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



; gAnsiStat = "<payload major>" in decimal, from the patched PREFMAJ value
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



; "Description: ... / Application: / Path: / Message: <capture>" into the event log, source ".NET Runtime", id 1023
WriteEventLog PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 50h
    mov rdi, qword ptr [gAnsiLogP]
    lea rsi, szLogIntroA
    call StrCpyA
    call GetExeNames                    ; gExePathA / gExeNameA
    mov rdi, qword ptr [gAnsiLogP]
    lea rsi, szLogAppA
    call StrCatA
    mov rsi, qword ptr [gExeNameAP]
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szLogPathA
    call StrCatA
    mov rsi, qword ptr [gExePathAP]
    call StrCatA
    lea rsi, szCrLfA
    call StrCatA
    lea rsi, szLogMsgA
    call StrCatA
    mov rsi, qword ptr [gAnsiDlgP]
    call StrCatA
    mov rsi, qword ptr [gAnsiLogP]
    mov rdi, qword ptr [gWideLogP]
    call AnsiToWide
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz wel_done
    lea rcx, szAdvapi32
    call rax
    test rax, rax
    jz wel_done
    mov rbx, rax
    mov rcx, rbx
    lea rdx, szRegisterEventSourceW
    call GetProcAddrOf
    test rax, rax
    jz wel_done
    mov qword ptr [gApi1], rax
;%triage:begin
    mov ecx, 15                         ; stage '?'
    call TriageStage
;%triage:end
    mov rax, qword ptr [gApi1]
    xor ecx, ecx                        ; lpUNCServerName
    lea rdx, szNetRuntimeW
    call rax
    test rax, rax
    jz wel_done
    mov rsi, rax                        ; event source handle
;%triage:begin
    mov ecx, 16                         ; stage '@'
    call TriageStage
;%triage:end
    mov rax, qword ptr [gWideLogP]
    mov qword ptr [gEvtArg], rax
    mov rcx, rbx
    lea rdx, szReportEventW
    call GetProcAddrOf
    test rax, rax
    jz wel_dereg
    mov qword ptr [gApi2], rax
;%triage:begin
    mov ecx, 17                         ; stage 'A'
    call TriageStage
;%triage:end
    mov rax, qword ptr [gApi2]
    mov rcx, rsi
    mov edx, 1                          ; EVENTLOG_ERROR_TYPE
    xor r8d, r8d                        ; wCategory
    mov r9d, 1023                       ; same id the apphost uses (CoreCLR ERT_UnmanagedFailFast)
    mov qword ptr [rsp+20h], 0          ; lpUserSid
    mov qword ptr [rsp+28h], 1          ; wNumStrings
    mov qword ptr [rsp+30h], 0          ; dwDataSize
    lea r10, gEvtArg
    mov qword ptr [rsp+38h], r10        ; lpStrings
    mov qword ptr [rsp+40h], 0          ; lpRawData
    call rax
;%triage:begin
    mov ecx, 18                         ; stage 'B'
    call TriageStage
;%triage:end
wel_dereg:
    mov rcx, rbx
    lea rdx, szDeregisterEventSourceW
    call GetProcAddrOf
    test rax, rax
    jz wel_done
    mov rcx, rsi
    call rax
wel_done:
    add rsp, 50h
    pop rdi
    pop rsi
    pop rbx
    ret
WriteEventLog ENDP



; own module path and file name as ansi
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



; eax = 1 when this module is a GUI image (Subsystem == 2)
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



; official wording, the DOTNET_DISABLE_GUI_ERRORS hatch, Yes opens it
ShowFailDialogIfGui PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 20h
    call IsGuiProcess
;%triage:begin
    mov ecx, 6                          ; entered the dialog path
    call TriageStage
;%triage:end
    test eax, eax
    jz sfd_done
;%triage:begin
    mov ecx, 7                          ; the image really is a GUI image
    call TriageStage
;%triage:end
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
;%triage:begin
    mov ecx, 10                         ; the DOTNET_DISABLE_GUI_ERRORS check passed
    call TriageStage
;%triage:end
    call GetExeNames                    ; fills gExeNameP/gExePathP (do this before resolving/using volatile regs)
    call TryShowTaskDialog              ; official shape first, the task dialog needs no MessageBox
    test eax, eax
    jnz sfd_done
    mov rsi, qword ptr [gAnsiDlgP]
    mov rdi, qword ptr [gWideDlgP]
    call AnsiToWide
;%triage:begin
    mov ecx, 11                         ; stage ';' = the wide dialog text is ready
    call TriageStage
;%triage:end
;%triage:begin
    mov ecx, 12                         ; stage '<' = about to resolve LoadLibraryA
    call TriageStage
;%triage:end
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz sfd_done
    lea rcx, szUser32
    call rax
    test rax, rax
    jz sfd_done
    mov rbx, rax                        ; the user32 module handle
;%triage:begin
    mov ecx, 13                         ; stage '=' = user32.dll loaded
    call TriageStage
;%triage:end
    mov rcx, rbx
    lea rdx, szMessageBoxW
    call GetProcAddrOf
    test rax, rax
    jz sfd_done
;%triage:begin
    mov ecx, 8                          ; MessageBoxW resolved, calling it now
    call TriageStage
;%triage:end
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
;%triage:begin
    mov ecx, 9                          ; dialog answered with yes
    call TriageStage
;%triage:end
    call OpenDownloadUrl
sfd_done:
    add rsp, 20h
    pop rdi
    pop rsi
    pop rbx
    ret
ShowFailDialogIfGui ENDP



; rsi = wide haystack rdi = ansi needle -> eax = 1 on a match finds the download URL and appends "&gui=true"
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


;%triage:begin
TriageStage PROC
    push rbx
    push rsi
    push rdi
    push r12
    sub rsp, 58h
    mov ebx, ecx
    lea rcx, szGetEnvA
    call ResolveApi
    test rax, rax
    jz ts_done
    mov r12, rax
    lea rcx, szLaunchStageEnv
    lea rdx, gAnsiStat
    mov r8d, 128
    call r12
    test rax, rax
    jz ts_done
    lea rcx, szCreateFileA
    call ResolveApi
    test rax, rax
    jz ts_done
    mov r12, rax
    lea rcx, gAnsiStat
    mov edx, 4                          ; FILE_APPEND_DATA
    mov r8d, 1
    xor r9d, r9d
    mov qword ptr [rsp+20h], 4          ; OPEN_ALWAYS
    mov qword ptr [rsp+28h], 80h        ; FILE_ATTRIBUTE_NORMAL
    mov qword ptr [rsp+30h], 0
    call r12
    cmp rax, -1
    je ts_done
    mov rsi, rax
    mov al, bl
    add al, '0'
    mov byte ptr [rsp+40h], al
    mov byte ptr [rsp+41h], 13
    mov byte ptr [rsp+42h], 10
    lea rcx, szWriteFile
    call ResolveApi
    test rax, rax
    jz ts_close
    mov rcx, rsi
    lea rdx, [rsp+40h]
    mov r8d, 3
    lea r9, [rsp+48h]
    mov dword ptr [rsp+48h], 0
    mov qword ptr [rsp+20h], 0          ; lpOverlapped (the 5th argument of WriteFile)
    call rax
ts_close:
    lea rcx, szCloseHandle
    call ResolveApi
    test rax, rax
    jz ts_done
    mov rcx, rsi
    call rax
ts_done:
    add rsp, 58h
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
TriageStage ENDP



TriageWriteLogFile PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 40h
    lea rcx, szGetEnvA
    call ResolveApi
    test rax, rax
    jz twl_done
    mov rbx, rax
    lea rcx, szLaunchErrEnv
    mov rdx, qword ptr [gAnsiTmpP]
    mov r8d, 64
    call rbx
    test rax, rax
    jz twl_done
    mov rsi, qword ptr [gAnsiDlgP]
    mov rdi, qword ptr [gAnsiLogP]
    call StrCpyA
    lea rcx, szCreateFileA
    call ResolveApi
    test rax, rax
    jz twl_done
    mov rcx, qword ptr [gAnsiTmpP]      ; file name from the env var
    mov edx, 40000000h                  ; GENERIC_WRITE
    mov r8d, 1                          ; FILE_SHARE_READ
    xor r9d, r9d                        ; sa
    mov qword ptr [rsp+20h], 2          ; CREATE_ALWAYS
    mov qword ptr [rsp+28h], 80h        ; FILE_ATTRIBUTE_NORMAL
    mov qword ptr [rsp+30h], 0          ; template
    call rax
    cmp rax, -1
    je twl_done
    mov rsi, rax
    mov rcx, qword ptr [gAnsiLogP]
    call StrLenA
    mov rdi, rax
    lea rcx, szWriteFile
    call ResolveApi
    test rax, rax
    jz twl_close
    mov rcx, rsi
    mov rdx, qword ptr [gAnsiLogP]
    mov r8, rdi
    lea r9, [rsp+20h]
    mov dword ptr [rsp+20h], 0
    mov qword ptr [rsp+28h], 0
    call rax
twl_close:
    lea rcx, szCloseHandle
    call ResolveApi
    test rax, rax
    jz twl_done
    mov rcx, rsi
    call rax
twl_done:
    add rsp, 40h
    pop rdi
    pop rsi
    pop rbx
    ret
TriageWriteLogFile ENDP
;%triage:end

; rcx = module handle, rdx = ansi export name -> rax = function or 0
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



; rsi = wide path -> rdi = wide basename, the apphost captions the dialog
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

; the official shape, the shell manifest brings comctl32 v6 and MessageBoxW is the fallback
; task dialog content (ansi) framework line + learn more link + download link with the payload major (PREFMAJ) and the architecture
BuildTdText PROC
    push rdi
    push rsi
    sub rsp, 28h
    mov rdi, qword ptr [gAnsiTdP]
    lea rsi, szTdReqA
    call StrCpyA
    call FormatMajorA
    lea rsi, gAnsiStat
    call StrCatA
    lea rsi, szFwTailA
    call StrCatA
    lea rsi, szLfA
    call StrCatA
    call StrCatA
    lea rsi, szLearnHeadA
    call StrCatA
    lea rsi, szHrefPreA
    call StrCatA
    lea rsi, szLearnUrlA
    call StrCatA
    lea rsi, szHrefMidA
    call StrCatA
    lea rsi, szLearnUrlA
    call StrCatA
    lea rsi, szHrefEndA
    call StrCatA
    lea rsi, szLfA
    call StrCatA
    call StrCatA
    lea rsi, szDownLinkLfA
    call StrCatA
    lea rsi, szHrefPreA
    call StrCatA
    mov rsi, qword ptr [gAnsiUrlP]
    call StrCatA
    lea rsi, szHrefMidA
    call StrCatA
    mov rsi, qword ptr [gAnsiUrlP]
    call StrCatAmpA
    lea rsi, szHrefEndA
    call StrCatA
    add rsp, 28h
    pop rsi
    pop rdi
    ret
BuildTdText ENDP

; creates and activates the activation context of the shell manifest eax = 1 on success
; only the v6 snapshot exports TaskDialogIndirect so the manifest must map comctl32 to v6
EnableVisualStyles PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 20h
    lea rcx, szGetWindowsDirectoryW
    call ResolveApi
    test rax, rax
    jz evs_no
    mov rdi, rax
    mov rcx, qword ptr [gWinDirP]
    mov edx, 260
    call rdi
    test eax, eax
    jz evs_no
    mov rsi, qword ptr [gWinDirP]
    mov rdi, qword ptr [gManifP]
    call StrCpyW
    lea rsi, szShellManifestW
    mov rdi, qword ptr [gManifP]
    call StrCatW
;%triage:begin
    mov ecx, 25                         ; manifest path built
    call TriageStage
;%triage:end
    mov rbx, qword ptr [gActP]
    mov dword ptr [rbx], 38h
    mov dword ptr [rbx+4], 0
    mov rax, qword ptr [gManifP]
    mov qword ptr [rbx+8], rax
    mov word ptr [rbx+10h], 0
    mov word ptr [rbx+12h], 0
    mov qword ptr [rbx+18h], 0
    mov qword ptr [rbx+20h], 0
    mov qword ptr [rbx+28h], 0
    mov qword ptr [rbx+30h], 0
    lea rcx, szCreateActCtxW
    call ResolveApi
    test rax, rax
    jz evs_no
    mov rcx, rbx
    call rax
    test rax, rax
    jz evs_no
    mov rbx, rax
    lea rcx, szActivateActCtx
    call ResolveApi
    test rax, rax
    jz evs_no
    mov rcx, rbx
    mov rdx, qword ptr [gActCookie]
    call rax
    test eax, eax
    jz evs_no
    mov eax, 1
    jmp evs_done
evs_no:
    xor eax, eax
evs_done:
    add rsp, 20h
    pop rdi
    pop rsi
    pop rbx
    ret
EnableVisualStyles ENDP

; shows the task dialog eax = 1 when shown 0 when the caller must fall back, the manifest is activated first and the packed config below has cbSize 0A0h
TryShowTaskDialog PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 20h
;%triage:begin
    mov ecx, 19                         ; task dialog path entered
    call TriageStage
;%triage:end
    call EnableVisualStyles
    test eax, eax
    jz tsd_no
;%triage:begin
    mov ecx, 20                         ; activation context active
    call TriageStage
;%triage:end
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz tsd_no
    lea rcx, szComctl32A
    call rax
    test rax, rax
    jz tsd_no
    mov rbx, rax
;%triage:begin
    mov ecx, 21                         ; comctl32 loaded
    call TriageStage
;%triage:end
    mov rcx, rbx
    lea rdx, szTaskDialogA
    call GetProcAddrOf
    test rax, rax
    jz tsd_no
    mov qword ptr [gApi2], rax
;%triage:begin
    mov ecx, 22                         ; TaskDialogIndirect resolved
    call TriageStage
;%triage:end
    call BuildTdText
    mov rsi, qword ptr [gAnsiTdP]
    mov rdi, qword ptr [gWideTdP]
    call AnsiToWide
    lea rsi, szInsGenericA
    mov rdi, qword ptr [gWideInsP]
    call AnsiToWide
    lea rsi, szDownloadBtnA
    mov rdi, qword ptr [gWideBtnP]
    call AnsiToWide
    ; TASKDIALOGCONFIG is packed (pshpack1 in commctrl.h) so the fields sit 4 bytes apart and cbSize is 0A0h
    mov rbx, qword ptr [gTdCfgP]
    mov dword ptr [rbx], 0A0h           ; cbSize
    mov dword ptr [rbx+4], 0            ; hwndParent, the official dialog is unowned
    mov dword ptr [rbx+8], 0
    mov dword ptr [rbx+0Ch], 0          ; hInstance
    mov dword ptr [rbx+10h], 0
    mov dword ptr [rbx+14h], 1000019h   ; dwFlags, hyperlinks + cancellable + size to content
    mov dword ptr [rbx+18h], 20h        ; dwCommonButtons, TDCBF_CLOSE_BUTTON
    mov rax, qword ptr [gWideExeNameP]
    mov qword ptr [rbx+1Ch], rax        ; pszWindowTitle
    mov qword ptr [rbx+24h], 0          ; hMainIcon, the official config leaves it null
    mov rax, qword ptr [gWideInsP]
    mov qword ptr [rbx+2Ch], rax        ; pszMainInstruction
    mov rax, qword ptr [gWideTdP]
    mov qword ptr [rbx+34h], rax        ; pszContent, carries the hyperlink markup
    mov dword ptr [rbx+3Ch], 1          ; cButtons
    lea rax, [rbx+0B8h]
    mov qword ptr [rbx+40h], rax        ; pButtons
    mov dword ptr [rbx+48h], 7          ; nDefaultButton, the download button
    mov dword ptr [rbx+4Ch], 0          ; cRadioButtons
    mov qword ptr [rbx+50h], 0          ; pRadioButtons
    mov dword ptr [rbx+58h], 0          ; nDefaultRadioButton
    mov qword ptr [rbx+5Ch], 0          ; pszVerificationText
    mov qword ptr [rbx+64h], 0          ; pszExpandedInformation
    mov qword ptr [rbx+6Ch], 0          ; pszExpandedControlText
    mov qword ptr [rbx+74h], 0          ; pszCollapsedControlText
    mov qword ptr [rbx+7Ch], 0          ; hFooterIcon
    mov qword ptr [rbx+84h], 0          ; pszFooter
    lea rax, [TdCallback]
    mov qword ptr [rbx+8Ch], rax        ; pfCallback, opens a clicked hyperlink
    mov qword ptr [rbx+94h], 0          ; lpCallbackData
    mov dword ptr [rbx+9Ch], 0          ; cxWidth
    mov dword ptr [rbx+0A0h], 0         ; chosen button, out parameter
    mov dword ptr [rbx+0B8h], 7         ; packed too, id at +0, label at +4
    mov rax, qword ptr [gWideBtnP]
    mov qword ptr [rbx+0BCh], rax
    mov rax, qword ptr [gApi2]
    mov rcx, rbx
    lea rdx, [rbx+0A0h]
    xor r8d, r8d
    xor r9d, r9d
    call rax
    mov dword ptr [gTdHr], eax
;%triage:begin
    call TriageStage                    ; hresult in ecx, 4 bytes, low first
    mov ecx, dword ptr [gTdHr]
    shr ecx, 8
    call TriageStage
    mov ecx, dword ptr [gTdHr]
    shr ecx, 16
    call TriageStage
    mov ecx, dword ptr [gTdHr]
    shr ecx, 24
    call TriageStage
;%triage:end
    mov eax, dword ptr [gTdHr]
    test eax, eax
    js tsd_no
;%triage:begin
    mov ecx, 23                         ; the task dialog ran
    call TriageStage
;%triage:end
    cmp dword ptr [rbx+0A0h], 7
    jne tsd_ok
;%triage:begin
    mov ecx, 24                         ; the custom button was chosen
    call TriageStage
;%triage:end
    call OpenDownloadUrl
tsd_ok:
    mov eax, 1
    jmp tsd_done
tsd_no:
    xor eax, eax
tsd_done:
    add rsp, 20h
    pop rdi
    pop rsi
    pop rbx
    ret
TryShowTaskDialog ENDP

; hyperlink click, open the address, which arrives as wide text in lParam
TdCallback PROC
    cmp edx, 3                          ; TDN_HYPERLINK_CLICKED
    jne tdc_ok
    test r9, r9
    jz tdc_ok
    sub rsp, 28h
    mov rcx, r9
    call OpenUrlW
    add rsp, 28h
tdc_ok:
    xor eax, eax                        ; S_OK
    ret
TdCallback ENDP

; opens a wide url through ShellExecuteW (rcx = url)
OpenUrlW PROC
    push rbx
    push rsi
    push rdi
    sub rsp, 20h
    mov rbx, rcx
    lea rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz ouw_done
    lea rcx, szShell32
    call rax
    test rax, rax
    jz ouw_done
    mov rsi, rax
    mov rcx, rsi
    lea rdx, szShellExecuteW
    call GetProcAddrOf
    test rax, rax
    jz ouw_done
    xor ecx, ecx
    lea rdx, szOpenW
    mov r8, rbx
    xor r9d, r9d
    mov qword ptr [rsp+20h], 0
    mov qword ptr [rsp+28h], 1
    call rax
ouw_done:
    add rsp, 20h
    pop rdi
    pop rsi
    pop rbx
    ret
OpenUrlW ENDP


; fixed slots, the packer rewrites them in place
astub_slot_dir_n equ 3
astub_slot_dir:
    dd 0C4B1A7E3h, 05E2D90B0h
    dd astub_slot_dir_n
    dd astub_slot_0001 - astub_slot_dir, 8, 0, 0
    dd astub_slot_0002 - astub_slot_dir, 10, 0, 0
    dd astub_slot_0003 - astub_slot_dir, 8, 0, 0

END