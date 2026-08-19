M_COM  equ 1000h
M_RES  equ 2000h
P_EXRW equ 40h
P_RW   equ 4

L_DEF  equ 012h
L_1B   equ 0FFh + L_DEF
L_2B   equ 0FFFFh + L_1B

.code







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
    ; Direct-hostfxr entry: runtime-resolve dotnet root + hostfxr,
    ; then call hostfxr_main_bundle_startupinfo. No manual apphost mapping.
    jmp  hostfxr_main_direct
StubEntry ENDP



; Direct-hostfxr: call hostfxr_main_bundle_startupinfo directly, skipping
; the manually-mapped apphost image entirely.
;   host_path       = GetModuleFileNameW(NULL)         (this exe = bundle)
;   dotnet_root     = runtime-resolved (env/registry) -> gDotnetRootW
;   app_path        = gAppPathW      (patched by packer)
;   header_offset   = gHeaderOff     (patched by packer)
hostfxr_main_direct PROC
    sub  rsp, 800h
    ; --- host_path: GetModuleFileNameW(NULL, [rsp+40h], 512) ---
    lea  rcx, szGetModuleFileNameW
    call ResolveApi
    test rax, rax
    jz   hmd_fail
    mov  r12, rax
    lea  rdx, [rsp+40h]
    mov  r8d, 512
    xor  ecx, ecx
    call r12
    test rax, rax
    jz   hmd_fail
    ; --- resolve dotnet root + best hostfxr at runtime ---
    call ResolveDotnetRoot
    call EnumFxrBestMatch
    ; --- LoadLibraryA(gHostfxrA) ---
    lea  rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz   hmd_fail
    mov  r12, rax
    lea  rcx, gHostfxrA
    call r12
    test rax, rax
    jz   hmd_fail
    mov  r13, rax                     ; r13 = hostfxr base
    ; --- GetProcAddress(hostfxr, "hostfxr_main_bundle_startupinfo") ---
    lea  rcx, szGetProcAddress
    call ResolveApi
    test rax, rax
    jz   hmd_fail
    mov  r12, rax
    mov  rcx, r13
    lea  rdx, szHostfxrMainBundle
    call r12
    test rax, rax
    jz   hmd_fail
    mov  r14, rax                     ; r14 = hostfxr_main_bundle_startupinfo
    ; --- call(argc=1, argv=[host_path], host_path, dotnet_root, app_path, header_offset) ---
    ; NOTE: argv block must live OUTSIDE the host_path buffer at [rsp+40h]
    ; (WDC's full path can exceed 190 bytes; overlapping would truncate it).
    lea  rax, [rsp+40h]               ; argv[0] = host_path (wide)
    mov  [rsp+500h], rax
    mov  qword ptr [rsp+508h], 0      ; argv[1] = NULL
    mov  ecx, 1                       ; argc = 1
    lea  rdx, [rsp+500h]              ; argv
    lea  r8,  [rsp+40h]               ; host_path
    lea  r9,  gDotnetRootW            ; dotnet_root
    lea  rax, gAppPathW
    mov  [rsp+20h], rax               ; app_path
    mov  rax, qword ptr [gHeaderOff]
    mov  [rsp+28h], rax               ; bundle_header_offset
    call r14
    mov  r12d, eax                    ; exit code
    lea  rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz   hmd_dead
    mov  ecx, r12d
    call rax
hmd_fail:
    lea  rcx, szExitProcess
    call ResolveApi
    test rax, rax
    jz   hmd_dead
    mov  ecx, 3
    call rax
hmd_dead:
    jmp  $
hostfxr_main_direct ENDP



; ---------------------------------------------------------------------------
; Runtime hostfxr resolution.
;   ResolveDotnetRoot: DOTNET_ROOT env -> registry -> default. -> gDotnetRootW
;   EnumFxrBestMatch:  scan <root>\host\fxr, prefer major==gPrefMajor.
;                      -> gHostfxrPathW (wide) + gHostfxrA (ANSI)
; ---------------------------------------------------------------------------

ResolveDotnetRoot PROC
    push rbx
    push r12
    sub  rsp, 28h
    ; --- 1. DOTNET_ROOT env var ---
    lea  rcx, szGetEnvironmentVariableW
    call ResolveApi
    test rax, rax
    jz   rdr_reg
    mov  rbx, rax
    lea  rcx, szEnvDotnetRootW
    lea  rdx, gDotnetRootW
    mov  r8d, 260
    call rbx
    test rax, rax
    jnz  rdr_done
rdr_reg:
    ; --- 2. registry ---
    lea  rcx, szLoadLibraryA
    call ResolveApi
    test rax, rax
    jz   rdr_default
    mov  rbx, rax
    lea  rcx, szAdvapi32
    call rbx                    ; LoadLibraryA("advapi32.dll")
    test rax, rax
    jz   rdr_default
    lea  rcx, szRegOpenKeyExW
    lea  rdx, szAdvapi32
    call ResolveApiIn
    test rax, rax
    jz   rdr_default
    mov  rbx, rax
    mov  rcx, 80000002h         ; HKEY_LOCAL_MACHINE
    lea  rdx, szRegSubKeyW
    xor  r8d, r8d               ; ulOptions = 0
    mov  r9d, 20019h            ; KEY_READ
    lea  rax, [rsp+20h]
    mov  [rsp+20h], rax         ; phkResult
    call rbx
    test eax, eax
    jnz  rdr_default
    mov  r12, [rsp+20h]         ; hKey
    lea  rcx, szRegQueryValueExW
    lea  rdx, szAdvapi32
    call ResolveApiIn
    test rax, rax
    jz   rdr_default
    mov  rbx, rax
    mov  rcx, r12               ; hKey
    lea  rdx, szRegValueW       ; "InstallLocation"
    xor  r8d, r8d               ; lpReserved
    xor  r9d, r9d               ; lpType
    lea  rax, gDotnetRootW
    mov  [rsp+20h], rax         ; lpData
    lea  rax, [rsp+30h]
    mov  [rsp+28h], rax         ; lpcbData ptr
    mov  dword ptr [rsp+30h], 1024   ; initial size
    call rbx
    test eax, eax
    jnz  rdr_default
    jmp  rdr_done
rdr_default:
    lea  rsi, szDefaultRootW
    lea  rdi, gDotnetRootW
    call StrCpyW
rdr_done:
    add  rsp, 28h
    pop  r12
    pop  rbx
    ret
ResolveDotnetRoot ENDP



EnumFxrBestMatch PROC
    push rbx
    push rbp
    push r12
    push r13
    push r14
    push r15
    sub  rsp, 28h
    ; reset bests
    mov  dword ptr [gFall], 0
    mov  dword ptr [gFall+4], 0
    mov  dword ptr [gFall+8], 0
    mov  dword ptr [gBest], 0
    mov  dword ptr [gBest+4], 0
    mov  dword ptr [gBest+8], 0
    ; gFxrDirW = root + "\host"
    lea  rsi, gDotnetRootW
    lea  rdi, gFxrDirW
    call StrCpyW
    lea  rsi, szHostDirW
    lea  rdi, gFxrDirW
    call StrCatW
    ; gFxrSearchW = gFxrDirW + "\fxr\*"
    lea  rsi, gFxrDirW
    lea  rdi, gFxrSearchW
    call StrCpyW
    lea  rsi, szFxrNameW
    lea  rdi, gFxrSearchW
    call StrCatW
    lea  rsi, szBackslashW
    lea  rdi, gFxrSearchW
    call StrCatW
    lea  rsi, szStarW
    lea  rdi, gFxrSearchW
    call StrCatW
    ; FindFirstFileW(gFxrSearchW, &gFindData)
    lea  rcx, szFindFirstFileW
    call ResolveApi
    test rax, rax
    jz   efb_fail
    mov  rbx, rax
    lea  rcx, gFxrSearchW
    lea  rdx, gFindData
    call rbx
    cmp  rax, -1
    je   efb_fail
    mov  r12, rax                ; hFind
efb_loop:
    mov  eax, dword ptr [gFindData]
    test eax, 10h                ; FILE_ATTRIBUTE_DIRECTORY
    jz   efb_next
    lea  rsi, gFindData+2Ch      ; cFileName
    lea  rdi, szDotW
    call StrEqW
    test eax, eax
    jnz  efb_next
    lea  rsi, gFindData+2Ch
    lea  rdi, szDotDotW
    call StrEqW
    test eax, eax
    jnz  efb_next
    lea  rcx, gFindData+2Ch
    call ParseVerW               ; eax=major edx=minor r8d=patch
    mov  r13d, eax
    mov  r14d, edx
    mov  r15d, r8d
    ; fallback: highest overall
    lea  rbx, gFall
    lea  rsi, gFindData+2Ch
    call UpdateBest
    ; matched: major == gPrefMajor
    mov  eax, dword ptr [gPrefMajor]
    test eax, eax
    jz   efb_next
    cmp  r13d, eax
    jne  efb_next
    lea  rbx, gBest
    lea  rsi, gFindData+2Ch
    call UpdateBest
efb_next:
    lea  rcx, szFindNextFileW
    call ResolveApi
    test rax, rax
    jz   efb_close
    mov  rbx, rax
    mov  rcx, r12
    lea  rdx, gFindData
    call rbx
    test rax, rax
    jnz  efb_loop
efb_close:
    lea  rcx, szFindClose
    call ResolveApi
    test rax, rax
    jz   efb_build
    mov  rcx, r12
    call rax
efb_build:
    ; gHostfxrPathW = root + "\host" + "\fxr" + "\" + name + "\hostfxr.dll"
    lea  rsi, gDotnetRootW
    lea  rdi, gHostfxrPathW
    call StrCpyW
    lea  rsi, szHostDirW
    lea  rdi, gHostfxrPathW
    call StrCatW
    lea  rsi, szFxrNameW
    lea  rdi, gHostfxrPathW
    call StrCatW
    lea  rsi, szBackslashW
    lea  rdi, gHostfxrPathW
    call StrCatW
    mov  eax, dword ptr [gBest]
    test eax, eax
    jnz  efb_use_best
    lea  rsi, gFall+0Ch
    jmp  efb_use
efb_use_best:
    lea  rsi, gBest+0Ch
efb_use:
    lea  rdi, gHostfxrPathW
    call StrCatW
    lea  rsi, szHostfxrDllW
    lea  rdi, gHostfxrPathW
    call StrCatW
    lea  rsi, gHostfxrPathW
    lea  rdi, gHostfxrA
    call WideToAnsi
efb_done:
    add  rsp, 28h
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbp
    pop  rbx
    ret
efb_fail:
    lea  rsi, gDotnetRootW
    lea  rdi, gHostfxrPathW
    call StrCpyW
    lea  rsi, szHostfxrDllW
    lea  rdi, gHostfxrPathW
    call StrCatW
    lea  rsi, gHostfxrPathW
    lea  rdi, gHostfxrA
    call WideToAnsi
    add  rsp, 28h
    pop  r15
    pop  r14
    pop  r13
    pop  r12
    pop  rbp
    pop  rbx
    ret
EnumFxrBestMatch ENDP



; rbx = storage (maj,min,pat + name at +0Ch)
; r13 = major, r14 = minor, r15 = patch, rsi = name (wide)
; Updates storage if (r13,r14,r15) is a strictly better version.
UpdateBest PROC
    push rsi
    push rdi
    push rbx
    mov  rax, rbx
    mov  rcx, rsi
    mov  ebx, [rax]
    cmp  r13d, ebx
    jg   ub_new
    jl   ub_none
    mov  ebx, [rax+4]
    cmp  r14d, ebx
    jg   ub_new
    jl   ub_none
    mov  ebx, [rax+8]
    cmp  r15d, ebx
    jle  ub_none
ub_new:
    mov  [rax], r13d
    mov  [rax+4], r14d
    mov  [rax+8], r15d
    lea  rdi, [rax+0Ch]
    mov  rsi, rcx
    call StrCpyW
ub_none:
    pop  rbx
    pop  rdi
    pop  rsi
    ret
UpdateBest ENDP



; rsi = wide src, rdi = wide dst (copies until NUL)
StrCpyW PROC
    push rsi
    push rdi
scw_l:
    mov  ax, [rsi]
    mov  [rdi], ax
    test ax, ax
    jz   scw_d
    add  rsi, 2
    add  rdi, 2
    jmp  scw_l
scw_d:
    pop  rdi
    pop  rsi
    ret
StrCpyW ENDP



; rsi = wide src appended to end of rdi (wide)
StrCatW PROC
    push rsi
    push rdi
scat_l:
    cmp  word ptr [rdi], 0
    je   scat_f
    add  rdi, 2
    jmp  scat_l
scat_f:
    call StrCpyW
    pop  rdi
    pop  rsi
    ret
StrCatW ENDP



; rsi vs rdi (wide). eax = 1 if equal, 0 otherwise.
StrEqW PROC
    push rsi
    push rdi
seq_l:
    mov  ax, [rsi]
    mov  dx, [rdi]
    cmp  ax, dx
    jne  seq_no
    test ax, ax
    jz   seq_yes
    add  rsi, 2
    add  rdi, 2
    jmp  seq_l
seq_yes:
    mov  eax, 1
    jmp  seq_d
seq_no:
    xor  eax, eax
seq_d:
    pop  rdi
    pop  rsi
    ret
StrEqW ENDP



; rcx = wide version string ("10.0.1")
; eax = major, edx = minor, r8d = patch
ParseVerW PROC
    push rbx
    push rsi
    mov  rsi, rcx
    call ParseNumW
    mov  ebx, eax
    xor  edx, edx
    xor  r8d, r8d
    cmp  word ptr [rsi], '.'
    jne  pv_d
    add  rsi, 2
    call ParseNumW
    mov  edx, eax
    cmp  word ptr [rsi], '.'
    jne  pv_d
    add  rsi, 2
    call ParseNumW
    mov  r8d, eax
pv_d:
    mov  eax, ebx
    pop  rsi
    pop  rbx
    ret
ParseVerW ENDP



; rsi = wide decimal digits. eax = value, rsi advanced past digits.
ParseNumW PROC
    push rbx
    xor  eax, eax
    mov  ebx, 10
pn_l:
    movzx ecx, word ptr [rsi]
    sub  ecx, '0'
    cmp  ecx, 9
    ja   pn_d
    imul eax, ebx
    add  eax, ecx
    add  rsi, 2
    jmp  pn_l
pn_d:
    pop  rbx
    ret
ParseNumW ENDP



; rsi = wide src, rdi = ANSI dst (LoadLibraryA-friendly)
WideToAnsi PROC
    push rsi
    push rdi
wta_l:
    movzx eax, word ptr [rsi]
    test eax, eax
    jz   wta_d
    mov  [rdi], al
    add  rsi, 2
    inc  rdi
    jmp  wta_l
wta_d:
    mov  byte ptr [rdi], 0
    pop  rdi
    pop  rsi
    ret
WideToAnsi ENDP



; Wait for the Loader Lock to become idle. Called after each stub step
; that touches the PEB module list / loader state (RegisterModule,
; TlsInit, RtlAddFunctionTable). The loader's async notification thread
; walks the list while holding the lock; LockCount<0 means idle. Yield
; (Sleep 0) so the holder can run; bounded retries as a deadlock backstop.
; Requires 3 consecutive idle samples (loader activity is bursty; a single
; idle read may be just a gap between two lock acquisitions).
; Preserves rdi (apphost base) and all other registers used by caller.



; !!! allocates 400h bytes, zeroes it, then fakes three !!!
; LDR_DATA_TABLE_ENTRY list heads (InLoadOrder/InMemory/InInit)
; and points all three at itself so the TLS callback runner
; won't crash on the manual-mapped image.







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


; !! shared by ResolveApi and ResolveApiIn
; !! rdx = module base, rdi = API name
; !! returns rax = function addr (0 if not found)
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
; shared failure path
rfail:
    xor  eax, eax
    pop  r12
    pop  rdi
    pop  rsi
    pop  rbx
    ret



szExitProcess    db "ExitProcess",0
szLoadLibraryA   db "LoadLibraryA",0
szGetProcAddress db "GetProcAddress",0
szGetModuleFileNameW db "GetModuleFileNameW",0
szHostfxrMainBundle db "hostfxr_main_bundle_startupinfo",0
szGetEnvironmentVariableW db "GetEnvironmentVariableW",0
szFindFirstFileW db "FindFirstFileW",0
szFindNextFileW  db "FindNextFileW",0
szFindClose      db "FindClose",0
szAdvapi32       db "advapi32.dll",0
szRegOpenKeyExW  db "RegOpenKeyExW",0
szRegQueryValueExW db "RegQueryValueExW",0

    align 8
; wide string constants (UTF-16LE)
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
; --- runtime-resolved data (stub fills at startup) ---
gDotnetRootW   db 520 dup(0)      ; dotnet root (wide)
gHostfxrPathW  db 520 dup(0)      ; full hostfxr.dll path (wide)
gHostfxrA      db 520 dup(0)      ; full hostfxr.dll path (ANSI, LoadLibraryA)
gFxrDirW       db 520 dup(0)      ; <root>\host
gFxrSearchW    db 520 dup(0)      ; <root>\host\fxr\*
gFindData      db 640 dup(0)      ; WIN32_FIND_DATAW
gFall          db 12 dup(0), 520 dup(0)   ; fallback best: maj,min,pat + name (wide)
gBest          db 12 dup(0), 520 dup(0)   ; pref-matched best: maj,min,pat + name (wide)

    align 8
gAppPathW   db "##APPPATH##",0           ; wide app.dll path (packer patches)
            db 500 dup(0)
    align 8
gPrefMajor  db "##PREFMAJ##",0           ; payload runtime major version (packer patches)
    align 8
gHeaderOff  dq 01122334455667788h        ; bundle header offset (packer patches)

    align 8



; rcx = module base
; rdx = LoadLibraryA
; r8  = GetProcAddress

END