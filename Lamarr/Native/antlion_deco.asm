.code

PUBLIC z0_init
PUBLIC z0_read
PUBLIC z0_align
PUBLIC z0_size

antlion_text_base:
L_0000::
    push rbx
    push rsi
    push rdi
    push r14
    sub rsp, 28h
    mov edi, edx                        ; nbits needed
    mov rbx, rcx                        ; br (bitreader state)
    cmp dword ptr [rcx+4020h], edx      ; br+4020h = nbits
    jge L_00BC                          ; already have enough
    mov r14d, 4000h                     ; INBUF_SIZE = 1<<14
L_0021::
    mov r8d, dword ptr [rbx+4010h]      ; buf_pos
    cmp r8d, dword ptr [rbx+4014h]      ; buf_end
    jl L_0073                           ; bytes still buffered
    cmp dword ptr [rbx+4024h], 0        ; src_eof
    jne L_00A8
    mov rcx, qword ptr [rbx+8]          ; read_fn
    lea rdx, [rbx+10h]                  ; buf
    mov r8, r14                         ; INBUF_SIZE
    call qword ptr [rbx]                ; read_fn(user, buf, len)
    cmp rax, r14
    jae L_0058
    mov dword ptr [rbx+4024h], 1        ; src_eof = 1
    jmp L_005B
L_0058::
    mov rax, r14
L_005B::
    mov dword ptr [rbx+4014h], eax      ; buf_end = got
    mov dword ptr [rbx+4010h], 0        ; buf_pos = 0
    test rax, rax
    je L_00A8                           ; EOF (0 bytes read)
    xor r8d, r8d                        ; buf_pos
L_0073::
    mov ecx, dword ptr [rbx+4020h]      ; nbits
    movsxd rax, r8d                     ; buf_pos
    movzx edx, byte ptr [rax+rbx+10h]   ; edx = buf[buf_pos]
    lea eax, [r8+1]                     ; buf_pos + 1
    shl rdx, cl                         ; shift into bit accumulator
    or qword ptr [rbx+4018h], rdx       ; br+4018h = bits
    mov dword ptr [rbx+4010h], eax      ; buf_pos = ...
    lea eax, [rcx+8]                    ; nbits += 8
    mov dword ptr [rbx+4020h], eax      ; nbits = ...
    cmp eax, edi                        ; enough bits yet?
    jl L_0021                           ; keep refilling
    jmp L_00BC
L_00A8::
    mov dword ptr [rbx+4028h], 1        ; eof = 1
    mov dword ptr [rbx+4020h], 40h      ; nbits = 0x40 (drain mode)
L_00BC::
    add rsp, 28h
    pop r14
    pop rdi
    pop rsi
    pop rbx
    ret
    int 3
    int 3
L_00C8::
    mov qword ptr [rsp+8], rbx
    push rdi
    sub rsp, 20h
    mov edx, 8
    mov rbx, rcx
    call L_0000
    mov rdi, qword ptr [rbx+4018h]
    mov edx, 8
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rax, rdi
    shr rax, 8
    mov rcx, rbx
    mov qword ptr [rbx+4018h], rax
    call L_0000
    mov rdx, qword ptr [rbx+4018h]
    mov eax, 0FFh
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rcx, rdx
    shr rcx, 8
    and ax, di
    shl dx, 8
    mov qword ptr [rbx+4018h], rcx
    or ax, dx
    mov rbx, qword ptr [rsp+30h]
    add rsp, 20h
    pop rdi
    ret
    int 3
    int 3
L_0140::
    push rbx
    push rbp
    push rsi
    push rdi
    push r12
    push r14
    push r15
    sub rsp, 180h
    mov edi, 5
    mov rbx, rcx
    mov edx, edi
    call L_0000
    mov rbp, qword ptr [rbx+4018h]
    mov edx, edi
    sub dword ptr [rbx+4020h], edi
    mov rax, rbp
    shr rax, 5
    and ebp, 1Fh
    mov rcx, rbx
    mov qword ptr [rbx+4018h], rax
    lea r12d, [rbp+101h]
    call L_0000
    mov rdi, qword ptr [rbx+4018h]
    mov edx, 4
    add dword ptr [rbx+4020h], 0FFFFFFFBh
    mov rax, rdi
    shr rax, 5
    mov rcx, rbx
    mov qword ptr [rbx+4018h], rax
    call L_0000
    mov rsi, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFFCh
    mov rax, rsi
    shr rax, 4
    mov qword ptr [rbx+4018h], rax
    cmp r12d, 11Eh
    ja L_0400
    and edi, 1Fh
    lea r15d, [rdi+1]
    cmp r15d, 1Eh
    ja L_0400
    and esi, 0Fh
    xor eax, eax
    xorps xmm0, xmm0
    add esi, 4
    movups xmmword ptr [rsp+20h], xmm0
    xor r14d, r14d
    mov dword ptr [rsp+2Fh], eax
L_020A::
    mov edx, 3
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    lea rdx, [R_00C0]
    sub dword ptr [rbx+4020h], 3
    mov rax, rcx
    shr rax, 3
    and cl, 7
    mov qword ptr [rbx+4018h], rax
    movzx eax, byte ptr [rdx+r14]
    inc r14d
    mov byte ptr [rsp+rax+20h], cl
    cmp r14d, esi
    jl L_020A
    lea r14, [rbx+34050h]
    mov r8d, 13h
    mov rcx, r14
    lea rdx, [rsp+20h]
    call L_0F24
    test eax, eax
    js L_0400
    xor edx, edx
    lea rcx, [rsp+40h]
    mov r8d, 140h
    call L_1404
    add edi, 102h
    add ebp, edi
    xor edi, edi
L_028C::
    mov edx, 0Fh
    mov rcx, rbx
    call L_0000
    mov rdx, qword ptr [rbx+4018h]
    mov rax, rdx
    and eax, 7FFFh
    mov r8d, dword ptr [rbx+rax*4+34050h]
    test r8d, 0F0000h
    je L_0400
    mov eax, r8d
    shr eax, 10h
    and eax, 0Fh
    sub dword ptr [rbx+4020h], eax
    mov cl, al
    shr rdx, cl
    mov qword ptr [rbx+4018h], rdx
    movzx ecx, r8w
    test r8d, 0FFF0h
    jne L_02F1
    movsxd rax, edi
    mov byte ptr [rsp+rax+40h], cl
    jmp L_03AD
L_02F1::
    cmp ecx, 10h
    jne L_0335
    test edi, edi
    je L_0400
    lea edx, [rcx-0Eh]
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFFEh
    mov rax, rcx
    shr rax, 2
    and ecx, 3
    mov qword ptr [rbx+4018h], rax
    add ecx, 3
    movsxd rax, edi
    mov sil, byte ptr [rsp+rax+3Fh]
    jmp L_0393
L_0335::
    xor sil, sil
    cmp ecx, 11h
    mov rcx, rbx
    jne L_0367
    mov edx, 3
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFFDh
    mov rax, rcx
    shr rax, 3
    and ecx, 7
    add ecx, 3
    jmp L_038C
L_0367::
    mov edx, 7
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFF9h
    mov rax, rcx
    shr rax, 7
    and ecx, 7Fh
    add ecx, 0Bh
L_038C::
    mov qword ptr [rbx+4018h], rax
L_0393::
    lea eax, [rcx+rdi]
    cmp eax, ebp
    jg L_0400
L_039A::
    movsxd rax, edi
    mov edx, edi
    inc edi
    mov byte ptr [rsp+rax+40h], sil
    sub ecx, 1
    jne L_039A
    mov edi, edx
L_03AD::
    inc edi
    cmp edi, ebp
    jl L_028C
    cmp dword ptr [rbx+4028h], 0
    jne L_0400
    lea rcx, [rbx+14050h]
    mov r8d, r12d
    lea rdx, [rsp+40h]
    call L_0F24
    test eax, eax
    js L_0400
    mov eax, r12d
    lea rdx, [rsp+40h]
    add rdx, rax
    mov r8d, r15d
    mov rcx, r14
    call L_0F24
    test eax, eax
    js L_0400
    mov dword ptr [rbx+1404Ch], 0
    xor eax, eax
    jmp L_0403
L_0400::
    or eax, 0FFFFFFFFh
L_0403::
    add rsp, 180h
    pop r15
    pop r14
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
    int 3
    int 3
    int 3
L_0418::
    push rbx
    sub rsp, 160h
    mov rbx, rcx
    mov r8d, 90h
    lea rcx, [rsp+40h]
    mov dl, 8
    call L_1404
    mov r8d, 70h
    lea rcx, [rsp+0D0h]
    mov dl, 9
    call L_1404
    movdqa xmm0, xmmword ptr [R_00F0]
    lea rcx, [rbx+14050h]
    mov rax, 808080808080808h
    movq mmword ptr [rsp+150h], xmm0
    mov r8d, 120h
    mov qword ptr [rsp+158h], rax
    lea rdx, [rsp+40h]
    movups xmmword ptr [rsp+140h], xmm0
    call L_0F24
    movdqa xmm0, xmmword ptr [R_00E0]
    lea rcx, [rbx+34050h]
    mov r8d, 20h
    lea rdx, [rsp+20h]
    movups xmmword ptr [rsp+20h], xmm0
    movups xmmword ptr [rsp+30h], xmm0
    call L_0F24
    mov dword ptr [rbx+1404Ch], 1
    add rsp, 160h
    pop rbx
    ret
    int 3
    int 3
    int 3
L_04CC::
    mov qword ptr [rsp+8], rbx
    mov qword ptr [rsp+18h], r8
    mov qword ptr [rsp+10h], rdx
    push rbp
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15
    sub rsp, 30h
    xor r12d, r12d
    mov r14, r8
    mov rbx, rdx
    mov rdi, rcx
    mov r15d, r12d
    test r8, r8
    je L_0EB4
    lea r10, [rcx+0C030h]
    lea r11, [rcx+4030h]
L_0510::
    mov ecx, dword ptr [rdi+14040h]
    mov ebp, 1
    cmp ecx, 5
    jg L_0BCE
    je L_07A0
    test ecx, ecx
    je L_0785
    sub ecx, ebp
    je L_06D8
    sub ecx, ebp
    je L_067B
    sub ecx, ebp
    je L_0577
    cmp ecx, ebp
    jne L_055A
    mov rcx, rdi
    call L_0140
    test eax, eax
    jns L_0E59
L_055A::
    mov dword ptr [rdi+14040h], 9
    or rax, 0FFFFFFFFFFFFFFFFh
    mov dword ptr [rdi+14044h], 0FFFFFFFFh
    jmp L_0EB7
L_0577::
    cmp word ptr [rdi+54050h], r12w
    jbe L_0661
L_0585::
    cmp r15, r14
    jae L_0EB4
    mov eax, dword ptr [rdi+14030h]
    cmp eax, 10000h
    jb L_05E4
    mov edx, dword ptr [rdi+14034h]
    mov r8, r10
    mov r9d, 8000h
L_05AA::
    movzx ecx, byte ptr [r8]
    mov eax, edx
    xor rcx, rax
    shr edx, 8
    movzx eax, cl
    add r8, rbp
    xor edx, dword ptr [rdi+rax*4+54058h]
    sub r9, rbp
    jne L_05AA
    mov dword ptr [rdi+14034h], edx
    mov r8d, 8000h
    mov rdx, r10
    mov rcx, r11
    call L_12E8
    mov eax, 8000h
L_05E4::
    mov edx, 8
    mov dword ptr [rdi+14030h], eax
    mov rcx, rdi
    call L_0000
    mov rcx, qword ptr [rdi+4018h]
    add dword ptr [rdi+4020h], 0FFFFFFF8h
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rdi+4018h], rax
    cmp dword ptr [rdi+4028h], r12d
    jne L_055A
    mov byte ptr [r15+rbx], cl
    lea r10, [rdi+0C030h]
    mov eax, dword ptr [rdi+14030h]
    lea r11, [rdi+4030h]
    add r15, rbp
    mov byte ptr [rdi+rax+4030h], cl
    mov eax, 0FFFFh
    add dword ptr [rdi+14030h], ebp
    add qword ptr [rdi+14038h], rbp
    add word ptr [rdi+54050h], ax
    jne L_0585
L_0661::
    mov eax, dword ptr [rdi+14048h]
    neg eax
    sbb ecx, ecx
    and ecx, 6
    add ecx, ebp
    mov dword ptr [rdi+14040h], ecx
    jmp L_0E63
L_067B::
    mov eax, dword ptr [rdi+4020h]
    mov cl, al
    and cl, 7
    and eax, 0FFFFFFF8h
    shr qword ptr [rdi+4018h], cl
    mov rcx, rdi
    mov dword ptr [rdi+4020h], eax
    call L_00C8
    mov rcx, rdi
    movzx ebx, ax
    call L_00C8
    cmp dword ptr [rdi+4028h], r12d
    jne L_055A
    not ax
    cmp bx, ax
    jne L_055A
    mov word ptr [rdi+54050h], bx
    mov dword ptr [rdi+14040h], 3
    jmp L_0E63
L_06D8::
    mov edx, ebp
    mov rcx, rdi
    call L_0000
    mov rcx, qword ptr [rdi+4018h]
    mov edx, 2
    dec dword ptr [rdi+4020h]
    mov rax, rcx
    and ecx, ebp
    shr rax, 1
    mov qword ptr [rdi+4018h], rax
    mov dword ptr [rdi+14048h], ecx
    mov rcx, rdi
    call L_0000
    mov rcx, qword ptr [rdi+4018h]
    add dword ptr [rdi+4020h], 0FFFFFFFEh
    mov rax, rcx
    shr rax, 2
    mov qword ptr [rdi+4018h], rax
    cmp dword ptr [rdi+4028h], r12d
    jne L_055A
    and ecx, 3
    je L_0776
    sub ecx, 1
    je L_075C
    cmp ecx, 1
    jne L_055A
    mov dword ptr [rdi+14040h], 4
    jmp L_0E63
L_075C::
    cmp dword ptr [rdi+1404Ch], r12d
    jne L_0E59
    mov rcx, rdi
    call L_0418
    jmp L_0E59
L_0776::
    mov dword ptr [rdi+14040h], 2
    jmp L_0E63
L_0785::
    mov rcx, rdi
    call L_1088
    test eax, eax
    js L_055A
    mov dword ptr [rdi+14040h], ebp
    jmp L_0E63
L_07A0::
    mov esi, dword ptr [rdi+14030h]     ; out_pos
    mov r13, qword ptr [rdi+14038h]     ; out_end
    jmp L_07C1
L_07AF::
    mov rbp, r9
L_07B2::
    mov r14, qword ptr [rsp+80h]        ; output buffer (caller)
L_07BA::
    lea r10, [rdi+0C030h]               ; window (32768 bytes)
L_07C1::
    cmp esi, 10000h                     ; window wrap?
    jb L_080C
    mov edx, dword ptr [rdi+14034h]     ; crc
    mov esi, 8000h
    mov r9d, esi
    mov r8, r10
L_07DA::
    movzx eax, byte ptr [r8]            ; CRC32 slicing-by-8 over window
    mov ecx, edx
    xor rcx, rax
    shr edx, 8
    movzx eax, cl
    add r8, rbp
    xor edx, dword ptr [rdi+rax*4+54058h] ; crc_tab[byte]
    sub r9, rbp
    jne L_07DA
    mov dword ptr [rdi+14034h], edx     ; crc = ...
    mov r8, rsi
    mov rdx, r10
    mov rcx, r11
    call L_12E8                         ; memmove(window)
L_080C::
    cmp r15, r14
    jae L_0BBC
    mov edx, 0Fh
    mov rcx, rdi
    call L_0000                         ; br_need(15)
    mov rdx, qword ptr [rdi+4018h]      ; bits
    mov rax, rdx
    and eax, 7FFFh                      ; sym index (15-bit code)
    mov r8d, dword ptr [rdi+rax*4+14050h]  ; huff_lit[sym]
    test r8d, 0F0000h                   ; huff_LEN
    jne L_0847
    or ecx, 0FFFFFFFFh                  ; invalid code
    jmp L_0866
L_0847::
    mov eax, r8d
    shr eax, 10h
    and eax, 0Fh                        ; code length
    mov cl, al
    shr rdx, cl                         ; consume code bits
    sub dword ptr [rdi+4020h], eax      ; nbits -= len
    mov qword ptr [rdi+4018h], rdx      ; bits = ...
    movzx ecx, r8w                      ; symbol (lit=0..255 / len>=256)
L_0866::
    cmp dword ptr [rdi+4028h], r12d
    jne L_0ECC
    cmp ecx, 100h
    jae L_089C
    mov eax, esi
    lea r11, [rdi+4030h]
    mov byte ptr [rdi+rax+4030h], cl
    mov byte ptr [r15+rbx], cl
    add r15, rbp
    inc esi
    add r13, rbp
    jmp L_07BA
L_089C::
    je L_0BA7
    cmp ecx, 11Dh
    ja L_0ECC
    lea eax, [rcx-101h]                 ; n = sym - 0x101 (length symbol)
    mov rcx, rdi
    lea rbp, [antlion_text_base-1000h]  ; image base
    mov ebx, eax                        ; n
    movzx r14d, byte ptr [rax+rbp+3040h]; len_extra[n]
    mov edx, r14d
    call L_0000                         ; br_need(len_extra)
    mov r12, qword ptr [rdi+4018h]      ; bits
    mov cl, r14b
    sub dword ptr [rdi+4020h], r14d     ; nbits -= len_extra
    mov rax, r12
    shr rax, cl                         ; consume extra bits
    mov edx, 0Fh
    mov qword ptr [rdi+4018h], rax      ; bits = ...
    mov rcx, rdi
    movzx eax, word ptr [rbp+rbx*2+3000h] ; len_base[n]
    mov word ptr [rsp+88h], ax
    call L_0000                         ; br_need(15) for dist code
    mov rdx, qword ptr [rdi+4018h]      ; bits
    mov rax, rdx
    and eax, 7FFFh                      ; sym index
    mov r8d, dword ptr [rdi+rax*4+34050h] ; huff_dist[sym]
    test r8d, 0F0000h                   ; huff_LEN
    je L_0ECC
    mov eax, r8d
    shr eax, 10h
    and eax, 0Fh                        ; code length
    sub dword ptr [rdi+4020h], eax      ; nbits -= len
    mov cl, al
    shr rdx, cl                         ; consume code bits
    movzx eax, r8w                      ; dist symbol
    mov qword ptr [rdi+4018h], rdx      ; bits = ...
    cmp eax, 1Eh
    jae L_0ECC
    mov ebp, eax                        ; n = dist symbol
    mov rcx, rdi
    lea rax, [antlion_text_base-1000h]  ; image base
    movzx ebx, byte ptr [rax+rbp+30A0h] ; dist_extra[n]
    mov edx, ebx
    call L_0000                         ; br_need(dist_extra)
    mov r8, qword ptr [rdi+4018h]       ; bits
    mov cl, bl
    sub dword ptr [rdi+4020h], ebx      ; nbits -= dist_extra
    mov rax, r8
    shr rax, cl                         ; consume extra bits
    cmp dword ptr [rdi+4028h], 0
    mov qword ptr [rdi+4018h], rax      ; bits = ...
    jne L_0ECC
    mov ecx, ebx
    lea rax, [antlion_text_base-1000h]  ; image base
    movzx ebx, word ptr [rax+rbp*2+3060h] ; dist_base[n]
    mov r9d, 1
    mov edx, r9d
    shl rdx, cl
    sub edx, r9d
    and edx, r8d                        ; extra bits value
    add ebx, edx
    mov r10d, ebx                       ; distance
    mov dword ptr [rsp+20h], ebx
    mov qword ptr [rsp+28h], r10
    cmp r10, r13                        ; dist > out_pos?
    ja L_0ECC
    movzx eax, word ptr [rsp+88h]
    mov ecx, r14d
    mov ebp, r9d
    shl rbp, cl
    sub ebp, r9d
    and ebp, r12d
    add ebp, eax
    mov rax, qword ptr [rsp+80h]
    sub rax, r15
    mov r14d, ebp
    cmp r14, rax
    ja L_0A4F
    mov eax, 10000h
    sub eax, esi
    cmp ebp, eax
    ja L_0A4F
    cmp ebx, ebp
    jb L_0A4F
    lea rax, [rdi+4030h]
    mov edx, esi
    mov r8d, r14d
    lea rbx, [rdx+rax]
    sub rdx, r10
    add rdx, rax
    mov rcx, rbx
    call L_12E8
    mov rcx, qword ptr [rsp+78h]
    mov r8d, r14d
    add rcx, r15
    mov rdx, rbx
    call L_12E8
    add esi, ebp
    add r15, r14
    add r13, r14
    jmp L_0B8E
L_0A4F::
    mov rbx, qword ptr [rsp+78h]
    lea r11, [rdi+4030h]
    xor r12d, r12d
    test ebp, ebp
    je L_07AF
    lea rbx, [rdi+4030h]
L_0A6D::
    cmp esi, 10000h
    jb L_0ACA
    mov edx, dword ptr [rdi+14034h]
    lea r10, [rdi+0C030h]
    mov esi, 8000h
    mov r8, r10
    mov r9d, esi
    mov r11d, 1
L_0A93::
    movzx eax, byte ptr [r8]
    mov ecx, edx
    xor rcx, rax
    shr edx, 8
    movzx eax, cl
    add r8, r11
    xor edx, dword ptr [rdi+rax*4+54058h]
    sub r9, r11
    jne L_0A93
    mov dword ptr [rdi+14034h], edx
    mov r8, rsi
    mov rdx, r10
    mov rcx, rbx
    call L_12E8
    mov r10, qword ptr [rsp+28h]
L_0ACA::
    mov rdx, qword ptr [rsp+80h]
    mov ecx, ebp
    sub ecx, r12d
    sub rdx, r15
    cmp rcx, rdx
    mov r14d, 10000h
    cmovbe rdx, rcx
    sub r14d, esi
    cmp rdx, r14
    cmovbe r14, rdx
    test r14, r14
    je L_0E83
    cmp r10, r14
    jb L_0B3E
    lea rax, [rdi+4030h]
    mov edx, esi
    mov r8, r14
    lea rbx, [rdx+rax]
    sub rdx, r10
    add rdx, rax
    mov rcx, rbx
    call L_12E8
    mov rcx, qword ptr [rsp+78h]
    mov r8, r14
    add rcx, r15
    mov rdx, rbx
    call L_12E8
    mov r10, qword ptr [rsp+28h]
    lea rbx, [rdi+4030h]
    jmp L_0B79
L_0B3E::
    xor edx, edx
    test r14, r14
    je L_0B79
    mov r11, qword ptr [rsp+78h]
    mov r8d, esi
    sub r8, r10
    mov r9d, esi
L_0B53::
    lea rax, [r8+rdx]
    mov cl, byte ptr [rdi+rax+4030h]
    lea rax, [r9+rdx]
    mov byte ptr [rdi+rax+4030h], cl
    lea rax, [rdx+r15]
    inc rdx
    mov byte ptr [r11+rax], cl
    cmp rdx, r14
    jb L_0B53
L_0B79::
    add esi, r14d
    add r15, r14
    add r13, r14
    add r12d, r14d
    cmp r12d, ebp
    jb L_0A6D
L_0B8E::
    mov rbx, qword ptr [rsp+78h]
    lea r11, [rdi+4030h]
    xor r12d, r12d
    lea ebp, [r12+1]
    jmp L_07B2
L_0BA7::
    mov eax, dword ptr [rdi+14048h]
    neg eax
    sbb ecx, ecx
    and ecx, 6
    add ecx, ebp
    mov dword ptr [rdi+14040h], ecx
L_0BBC::
    mov dword ptr [rdi+14030h], esi
    mov qword ptr [rdi+14038h], r13
    jmp L_0E63
L_0BCE::
    sub ecx, 6
    je L_0CF0
    sub ecx, 1
    jne L_0F0C
    mov eax, dword ptr [rdi+14030h]
    add eax, 0FFFF8000h
    je L_0C1D
    mov edx, dword ptr [rdi+14034h]
    mov r8, r10
    mov r9d, eax
L_0BF9::
    movzx eax, byte ptr [r8]
    mov ecx, edx
    xor rcx, rax
    shr edx, 8
    movzx eax, cl
    add r8, rbp
    xor edx, dword ptr [rdi+rax*4+54058h]
    sub r9, rbp
    jne L_0BF9
    mov dword ptr [rdi+14034h], edx
L_0C1D::
    mov eax, dword ptr [rdi+4020h]
    mov ebp, r12d
    mov cl, al
    mov ebx, r12d
    and cl, 7
    shr qword ptr [rdi+4018h], cl
    and eax, 0FFFFFFF8h
    mov dword ptr [rdi+4020h], eax
L_0C3E::
    mov edx, 8
    mov rcx, rdi
    call L_0000
    mov rcx, qword ptr [rdi+4018h]
    sub dword ptr [rdi+4020h], 8
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rdi+4018h], rax
    movzx eax, cl
    mov ecx, ebx
    shl eax, cl
    add ebx, 8
    or ebp, eax
    cmp ebx, 20h
    jne L_0C3E
    mov esi, r12d
    mov ebx, r12d
L_0C7E::
    mov edx, 8
    mov rcx, rdi
    call L_0000
    mov rcx, qword ptr [rdi+4018h]
    sub dword ptr [rdi+4020h], 8
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rdi+4018h], rax
    movzx eax, cl
    mov ecx, ebx
    shl eax, cl
    add ebx, 8
    or esi, eax
    cmp ebx, 20h
    jne L_0C7E
    cmp dword ptr [rdi+4028h], r12d
    jne L_055A
    mov eax, dword ptr [rdi+14034h]
    not eax
    cmp eax, ebp
    jne L_0EEF
    cmp dword ptr [rdi+14038h], esi
    jne L_0EEF
    mov dword ptr [rdi+14040h], 8
    jmp L_0E63
L_0CF0::
    movzx eax, word ptr [rdi+54056h]
    movzx ecx, word ptr [rdi+54052h]
    mov ebp, dword ptr [rdi+14030h]
    mov r12, qword ptr [rdi+14038h]
    movzx r13d, word ptr [rdi+54054h]
    cmp ax, cx
    jae L_0E47
L_0D1C::
    cmp r15, r14
    jae L_0E47
    cmp ebp, 10000h
    jb L_0D7F
    mov edx, dword ptr [rdi+14034h]
    mov r8, r10
    mov esi, 1
    mov r9d, 8000h
L_0D41::
    movzx ecx, byte ptr [r8]
    mov eax, edx
    xor rcx, rax
    shr edx, 8
    movzx eax, cl
    add r8, rsi
    xor edx, dword ptr [rdi+rax*4+54058h]
    sub r9, rsi
    jne L_0D41
    mov dword ptr [rdi+14034h], edx
    mov ebp, 8000h
    mov r8d, ebp
    mov rdx, r10
    mov rcx, r11
    call L_12E8
    lea r11, [rdi+4030h]
L_0D7F::
    movzx eax, word ptr [rdi+54056h]
    mov esi, 10000h
    movzx ecx, word ptr [rdi+54052h]
    sub esi, ebp
    sub rcx, rax
    mov rdx, r14
    sub rdx, r15
    lea r14, [r15+rbx]
    cmp rcx, rdx
    mov rax, r13
    cmovbe rdx, rcx
    cmp rdx, rsi
    cmovbe rsi, rdx
    mov edx, ebp
    cmp r13, rsi
    jb L_0DEC
    lea rbx, [rdx+r11]
    mov r8, rsi
    sub rdx, rax
    mov rcx, rbx
    add rdx, r11
    call L_12E8
    mov r8, rsi
    mov rdx, rbx
    mov rcx, r14
    call L_12E8
    add word ptr [rdi+54056h], si
    mov edx, esi
    mov rbx, qword ptr [rsp+78h]
    jmp L_0E12
L_0DEC::
    mov ecx, ebp
    sub ecx, r13d
    mov cl, byte ptr [rdi+rcx+4030h]
    mov byte ptr [rdi+rdx+4030h], cl
    mov byte ptr [r14], cl
    mov ecx, 1
    add word ptr [rdi+54056h], cx
    mov esi, ecx
    mov edx, ecx
L_0E12::
    movzx eax, word ptr [rdi+54056h]
    lea r10, [rdi+0C030h]
    movzx ecx, word ptr [rdi+54052h]
    lea r11, [rdi+4030h]
    mov r14, qword ptr [rsp+80h]
    add r15, rsi
    add ebp, edx
    add r12, rsi
    cmp ax, cx
    jb L_0D1C
L_0E47::
    mov dword ptr [rdi+14030h], ebp
    mov qword ptr [rdi+14038h], r12
    cmp ax, cx
    jne L_0E63
L_0E59::
    mov dword ptr [rdi+14040h], 5
L_0E63::
    cmp r15, r14
    jae L_0EB4
    mov rbx, qword ptr [rsp+78h]
    lea r10, [rdi+0C030h]
    lea r11, [rdi+4030h]
    xor r12d, r12d
    jmp L_0510
L_0E83::
    mov eax, dword ptr [rsp+20h]
    mov word ptr [rdi+54054h], ax
    mov dword ptr [rdi+14030h], esi
    mov qword ptr [rdi+14038h], r13
    mov word ptr [rdi+54052h], bp
    mov word ptr [rdi+54056h], r12w
    mov dword ptr [rdi+14040h], 6
L_0EB4::
    mov rax, r15
L_0EB7::
    mov rbx, qword ptr [rsp+70h]
    add rsp, 30h
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbp
    ret
L_0ECC::
    or rax, 0FFFFFFFFFFFFFFFFh
    mov dword ptr [rdi+14030h], esi
    mov dword ptr [rdi+14044h], eax
    mov qword ptr [rdi+14038h], r13
    mov dword ptr [rdi+14040h], 9
    jmp L_0EB7
L_0EEF::
    mov dword ptr [rdi+14040h], 9
    mov rax, 0FFFFFFFFFFFFFFFEh
    mov dword ptr [rdi+14044h], 0FFFFFFFEh
    jmp L_0EB7
L_0F0C::
    sub ecx, 1
    je L_0EB4
    cmp ecx, 1
    jne L_055A
    movsxd rax, dword ptr [rdi+14044h]
    jmp L_0EB7
    int 3
L_0F24::
    push rbx
    push rbp
    push rsi
    push rdi
    push r12
    push r14
    push r15
    sub rsp, 2A0h
    xorps xmm0, xmm0
    mov ebx, r8d
    mov rdi, rdx
    mov r14, rcx
    xor edx, edx
    lea rcx, [rsp+60h]
    mov r8d, 240h
    movups xmmword ptr [rsp+20h], xmm0
    movups xmmword ptr [rsp+30h], xmm0
    call L_1404
    xor r9d, r9d
    xor r8d, r8d
    lea r15d, [r9+1]
    test ebx, ebx
    jle L_0F94
L_0F6C::
    movzx ecx, byte ptr [rdi+r8]
    cmp cl, 0Fh
    ja L_1080
    add word ptr [rsp+rcx*2+20h], r15w
    mov eax, ecx
    cmp ecx, r9d
    cmovle eax, r9d
    add r8d, r15d
    mov r9d, eax
    cmp r8d, ebx
    jl L_0F6C
L_0F94::
    xor eax, eax
    xor edx, edx
    mov word ptr [rsp+20h], ax
    mov r8d, r15d
    cmp r9d, r15d
    jl L_0FBE
L_0FA5::
    movsxd rcx, r8d
    add r8d, r15d
    movzx eax, word ptr [rsp+rcx*2+1Eh]
    add edx, eax
    add edx, edx
    mov word ptr [rsp+rcx*2+40h], dx
    cmp r8d, r9d
    jle L_0FA5
L_0FBE::
    xor edx, edx
    test ebx, ebx
    jle L_106C
L_0FC8::
    movzx eax, byte ptr [rdi+rdx]
    test al, al
    je L_0FE5
    mov ecx, eax
    movzx eax, word ptr [rsp+rax*2+40h]
    mov word ptr [rsp+rdx*2+60h], ax
    add ax, r15w
    mov word ptr [rsp+rcx*2+40h], ax
L_0FE5::
    add edx, r15d
    cmp edx, ebx
    jl L_0FC8
    xor edx, edx
    mov r12d, 8000h
L_0FF4::
    movsxd r9, edx
    movzx eax, byte ptr [rdi+r9]
    test al, al
    je L_1065
    mov ecx, eax
    mov esi, r15d
    shl esi, cl
    xor r10d, r10d
    mov r8d, eax
    test al, al
    je L_1043
    movzx r9d, word ptr [rsp+r9*2+60h]
    xor r11d, r11d
    movzx ebp, ax
L_101D::
    mov cl, bpl
    movzx eax, r9w
    sub cl, r11b
    sub cl, r15b
    shr ax, cl
    mov ecx, r11d
    and ax, r15w
    add r11d, r15d
    shl ax, cl
    or r10w, ax
    cmp r11d, r8d
    jl L_101D
L_1043::
    movzx ecx, r10w
    cmp r10w, r12w
    jae L_1065
    shl r8d, 10h
    movzx eax, dx
    or r8d, eax
L_1057::
    movsxd rax, ecx
    add ecx, esi
    mov dword ptr [r14+rax*4], r8d
    cmp ecx, r12d
    jl L_1057
L_1065::
    add edx, r15d
    cmp edx, ebx
    jl L_0FF4
L_106C::
    xor eax, eax
L_106E::
    add rsp, 2A0h
    pop r15
    pop r14
    pop r12
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret
L_1080::
    or eax, 0FFFFFFFFh
    jmp L_106E
    int 3
    int 3
    int 3
L_1088::
    push rbx
    push rbp
    push rsi
    push rdi
    sub rsp, 28h
    mov ebp, 8
    mov rbx, rcx
    mov edx, ebp
    call L_0000
    mov rdx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rax, rdx
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    cmp dl, 1Fh
    jne L_123C
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    cmp cl, 8Bh
    jne L_123C
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    cmp cl, bpl
    jne L_123C
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rdi, qword ptr [rbx+4018h]
    add dword ptr [rbx+4020h], 0FFFFFFF8h
    mov rax, rdi
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    test dil, 0E0h
    jne L_123C
    lea esi, [rbp-2]
L_1156::
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rax, qword ptr [rbx+4018h]
    sub dword ptr [rbx+4020h], ebp
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    sub esi, 1
    jne L_1156
    test dil, 4
    je L_11C1
    mov rcx, rbx
    call L_00C8
    xor ecx, ecx
    movzx esi, ax
    cmp cx, ax
    jae L_11C1
L_1195::
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rax, qword ptr [rbx+4018h]
    sub dword ptr [rbx+4020h], ebp
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    mov eax, 0FFFFh
    add si, ax
    jne L_1195
L_11C1::
    test bpl, dil
    je L_11F8
L_11C6::
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    sub dword ptr [rbx+4020h], ebp
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    test cl, cl
    je L_11F8
    cmp dword ptr [rbx+4028h], 0
    je L_11C6
L_11F8::
    test dil, 10h
    je L_1230
L_11FE::
    mov edx, ebp
    mov rcx, rbx
    call L_0000
    mov rcx, qword ptr [rbx+4018h]
    sub dword ptr [rbx+4020h], ebp
    mov rax, rcx
    shr rax, 8
    mov qword ptr [rbx+4018h], rax
    test cl, cl
    je L_1230
    cmp dword ptr [rbx+4028h], 0
    je L_11FE
L_1230::
    mov eax, dword ptr [rbx+4028h]
    neg eax
    sbb eax, eax
    jmp L_123F
L_123C::
    or eax, 0FFFFFFFFh
L_123F::
    add rsp, 28h
    pop rdi
    pop rsi
    pop rbp
    pop rbx
    ret

; init: clear state, build CRC32 table
z0_init PROC
    mov qword ptr [rsp+8], rbx
    push rdi
    sub rsp, 20h
    mov rdi, rdx                        ; rdi = cfg
    mov rbx, rcx                        ; rbx = state (scratch)
    test rcx, rcx
    je L_12D9
    test rdx, rdx
    je L_12D9
    cmp qword ptr [rdx], 0              ; cfg->read_fn?
    je L_12D9
    xor edx, edx
    mov r8d, 54458h                     ; state size
    call L_1404                         ; memset(state, 0, 54458h)
    mov rcx, qword ptr [rdi+8]          ; cfg->user
    xor edx, edx
    mov rax, qword ptr [rdi]            ; cfg->read_fn
    mov qword ptr [rbx], rax            ; state+0 = read_fn
    mov qword ptr [rbx+8], rcx          ; state+8 = user
    mov dword ptr [rbx+14030h], 8000h   ; out_pos = window half
    mov dword ptr [rbx+14034h], 0FFFFFFFFh ; crc = ~0
; !! CRC32 slicing-by-8 table, built at init (rdi+54058h)
L_1299::
    mov r8d, edx                        ; byte value (0..255)
    mov r9d, 8                          ; 8 bits per entry
L_12A2::
    mov eax, r8d
    and al, 1
    neg al                              ; CF = bit0
    mov eax, r8d
    sbb ecx, ecx                        ; ecx = 0xFFFFFFFF if CF else 0
    shr eax, 1
    and ecx, 0EDB88320h                 ; CRC32 polynomial
    mov r8d, ecx
    xor r8d, eax
    sub r9d, 1
    jne L_12A2
    mov dword ptr [rbx+rdx*4+54058h], r8d ; crc_tab[i]
    inc edx
    cmp edx, 100h
    jb L_1299
    mov rax, rbx                        ; return state
    jmp L_12DB
L_12D9::
    xor eax, eax
L_12DB::
    mov rbx, qword ptr [rsp+30h]
    add rsp, 20h
    pop rdi
    ret
    int 3
    int 3
L_12E8::
    push rbx
    sub rsp, 20h
    mov rbx, rcx
    test r8, r8
    je L_132E
    xor ecx, ecx
    cmp r8, 10h
    jb L_1320
    lea rax, [r8-1]
    add rax, rdx
    cmp rbx, rax
    ja L_1316
    lea rax, [r8-1]
    add rax, rbx
    cmp rax, rdx
    jae L_1320
L_1316::
    mov rcx, rbx
    call L_138C
    jmp L_132E
L_1320::
    mov al, byte ptr [rcx+rdx]
    mov byte ptr [rcx+rbx], al
    inc rcx
    cmp rcx, r8
    jb L_1320
L_132E::
    mov rax, rbx
    add rsp, 20h
    pop rbx
    ret
    int 3
z0_init ENDP

; !! decoder entry: pull bytes per state machine
z0_read PROC
    test rcx, rcx                       ; state
    jne L_1342
    or rax, 0FFFFFFFFFFFFFFFFh          ; null state -> -1
    ret
L_1342::
    cmp dword ptr [rcx+14040h], 9       ; phase == complete?
    jne L_1353
    movsxd rax, dword ptr [rcx+14044h]  ; return last result
    ret
L_1353::
    test r8, r8                         ; out cap == 0
    jne L_135B
    xor eax, eax                        ; 0 bytes -> 0
    ret
L_135B::
    test rdx, rdx                       ; out buf == NULL?
    jne L_1375
    or rax, 0FFFFFFFFFFFFFFFFh          ; error
    mov dword ptr [rcx+14040h], 9       ; phase = complete
    mov dword ptr [rcx+14044h], eax     ; result = -1
    ret
L_1375::
    jmp L_04CC                          ; main state machine (gz state)
    int 3
    int 3
z0_read ENDP

z0_align PROC
    mov eax, 8
    ret
    int 3
    int 3
z0_align ENDP

; state size = 54458h (~345KB, all scratch)
z0_size PROC
    mov eax, 54458h
    ret
    int 3
    int 3
L_138C::
    mov qword ptr [rsp+18h], r8
    mov qword ptr [rsp+10h], rdx
    mov qword ptr [rsp+8], rcx
    sub rsp, 28h
    mov rax, qword ptr [rsp+30h]
    mov qword ptr [rsp+8], rax
    mov rax, qword ptr [rsp+38h]
    mov qword ptr [rsp+10h], rax
    mov qword ptr [rsp], 0
    jmp L_13C8
L_13BD::
    mov rax, qword ptr [rsp]
    inc rax
    mov qword ptr [rsp], rax
L_13C8::
    mov rax, qword ptr [rsp+40h]
    cmp qword ptr [rsp], rax
    jae L_13F7
    mov rax, qword ptr [rsp]
    mov rcx, qword ptr [rsp+8]
    add rcx, rax
    mov rax, rcx
    mov rcx, qword ptr [rsp]
    mov rdx, qword ptr [rsp+10h]
    add rdx, rcx
    mov rcx, rdx
    mov cl, byte ptr [rcx]
    mov byte ptr [rax], cl
    jmp L_13BD
L_13F7::
    mov rax, qword ptr [rsp+30h]
    add rsp, 28h
    ret
    int 3
    int 3
    int 3
L_1404::
    mov qword ptr [rsp+18h], r8
    mov dword ptr [rsp+10h], edx
    mov qword ptr [rsp+8], rcx
    sub rsp, 18h
    mov rax, qword ptr [rsp+20h]
    mov qword ptr [rsp+8], rax
    mov qword ptr [rsp], 0
    jmp L_1435
L_142A::
    mov rax, qword ptr [rsp]
    inc rax
    mov qword ptr [rsp], rax
L_1435::
    mov rax, qword ptr [rsp+30h]
    cmp qword ptr [rsp], rax
    jae L_1457
    mov rax, qword ptr [rsp]
    mov rcx, qword ptr [rsp+8]
    add rcx, rax
    mov rax, rcx
    mov cl, byte ptr [rsp+28h]
    mov byte ptr [rax], cl
    jmp L_142A
L_1457::
    mov rax, qword ptr [rsp+20h]
    add rsp, 18h
    ret
z0_size ENDP

    org antlion_text_base + 2000h
antlion_rdata_base:
    db 03h, 00h, 04h, 00h, 05h, 00h, 06h, 00h, 07h, 00h, 08h, 00h               ; +0x0
    db 09h, 00h, 0Ah, 00h, 0Bh, 00h, 0Dh, 00h, 0Fh, 00h, 011h, 00h              ; +0xC
    db 013h, 00h, 017h, 00h, 01Bh, 00h, 01Fh, 00h, 023h, 00h, 02Bh, 00h         ; +0x18
    db 033h, 00h, 03Bh, 00h, 043h, 00h, 053h, 00h, 063h, 00h, 073h, 00h         ; +0x24
    db 083h, 00h, 0A3h, 00h, 0C3h, 00h, 0E3h, 00h, 02h, 01h, 00h, 00h           ; +0x30
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x3C
    db 01h, 01h, 01h, 01h, 02h, 02h, 02h, 02h, 03h, 03h, 03h, 03h               ; +0x48
    db 04h, 04h, 04h, 04h, 05h, 05h, 05h, 05h, 00h, 00h, 00h, 00h               ; +0x54
    db 01h, 00h, 02h, 00h, 03h, 00h, 04h, 00h, 05h, 00h, 07h, 00h               ; +0x60
    db 09h, 00h, 0Dh, 00h, 011h, 00h, 019h, 00h, 021h, 00h, 031h, 00h           ; +0x6C
    db 041h, 00h, 061h, 00h, 081h, 00h, 0C1h, 00h, 01h, 01h, 081h, 01h          ; +0x78
    db 01h, 02h, 01h, 03h, 01h, 04h, 01h, 06h, 01h, 08h, 01h, 0Ch               ; +0x84
    db 01h, 010h, 01h, 018h, 01h, 020h, 01h, 030h, 01h, 040h, 01h, 060h         ; +0x90
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 01h, 01h, 02h, 02h               ; +0x9C
    db 03h, 03h, 04h, 04h, 05h, 05h, 06h, 06h, 07h, 07h, 08h, 08h               ; +0xA8
    db 09h, 09h, 0Ah, 0Ah, 0Bh, 0Bh, 0Ch, 0Ch, 0Dh, 0Dh, 00h, 00h               ; +0xB4
R_00C0:
    db 010h, 011h, 012h, 00h, 08h, 07h, 09h, 06h, 0Ah, 05h, 0Bh, 04h            ; +0xC0
    db 0Ch, 03h, 0Dh, 02h, 0Eh, 01h, 0Fh, 00h, 00h, 00h, 00h, 00h               ; +0xCC
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h        ; +0xD8
R_00E0:
    db 05h, 05h, 05h, 05h, 05h, 05h, 05h, 05h, 05h, 05h, 05h, 05h               ; +0xE0
    db 05h, 05h, 05h, 05h                                                       ; +0xEC
R_00F0:
    db 07h, 07h, 07h, 07h, 07h, 07h, 07h, 07h, 07h, 07h, 07h, 07h               ; +0xF0
    db 07h, 07h, 07h, 07h, 00h, 00h, 00h, 00h, 094h, 079h, 08Bh, 06Ah           ; +0xFC
    db 00h, 00h, 00h, 00h, 0Dh, 00h, 00h, 00h, 088h, 00h, 00h, 00h              ; +0x108
    db 034h, 031h, 00h, 00h, 034h, 01Bh, 00h, 00h, 018h, 00h, 00h, 00h          ; +0x114
    db 00h, 080h, 00h, 080h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h             ; +0x120
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x12C
    db 00h, 010h, 00h, 00h, 061h, 014h, 00h, 00h, 02Eh, 074h, 065h, 078h        ; +0x138
    db 074h, 024h, 06Dh, 06Eh, 00h, 00h, 00h, 00h, 00h, 030h, 00h, 00h          ; +0x144
    db 01Ch, 01h, 00h, 00h, 02Eh, 072h, 064h, 061h, 074h, 061h, 00h, 00h        ; +0x150
    db 01Ch, 031h, 00h, 00h, 018h, 00h, 00h, 00h, 02Eh, 072h, 064h, 061h        ; +0x15C
    db 074h, 061h, 024h, 076h, 06Fh, 06Ch, 074h, 06Dh, 064h, 00h, 00h, 00h      ; +0x168
    db 034h, 031h, 00h, 00h, 088h, 00h, 00h, 00h, 02Eh, 072h, 064h, 061h        ; +0x174
    db 074h, 061h, 024h, 07Ah, 07Ah, 07Ah, 064h, 062h, 067h, 00h, 00h, 00h      ; +0x180
    db 0BCh, 031h, 00h, 00h, 0A4h, 00h, 00h, 00h, 02Eh, 078h, 064h, 061h        ; +0x18C
    db 074h, 061h, 00h, 00h, 060h, 032h, 00h, 00h, 09Ch, 00h, 00h, 00h          ; +0x198
    db 02Eh, 065h, 064h, 061h, 074h, 061h, 00h, 00h, 00h, 040h, 00h, 00h        ; +0x1A4
    db 084h, 00h, 00h, 00h, 02Eh, 070h, 064h, 061h, 074h, 061h, 00h, 00h        ; +0x1B0
    db 01h, 06h, 02h, 00h, 06h, 032h, 02h, 030h, 01h, 0Ah, 04h, 00h             ; +0x1BC
    db 0Ah, 034h, 06h, 00h, 0Ah, 032h, 06h, 070h, 01h, 0Ah, 05h, 00h            ; +0x1C8
    db 0Ah, 042h, 06h, 0E0h, 04h, 070h, 03h, 060h, 02h, 030h, 00h, 00h          ; +0x1D4
    db 01h, 012h, 09h, 00h, 012h, 01h, 054h, 00h, 0Bh, 0F0h, 09h, 0E0h          ; +0x1E0
    db 07h, 0C0h, 05h, 070h, 04h, 060h, 03h, 050h, 02h, 030h, 00h, 00h          ; +0x1EC
    db 01h, 09h, 03h, 00h, 09h, 01h, 02Ch, 00h, 02h, 030h, 00h, 00h             ; +0x1F8
    db 01h, 09h, 05h, 00h, 09h, 042h, 05h, 070h, 04h, 060h, 03h, 050h           ; +0x204
    db 02h, 030h, 00h, 00h, 01h, 012h, 09h, 00h, 012h, 01h, 030h, 00h           ; +0x210
    db 0Bh, 0F0h, 09h, 0E0h, 07h, 0C0h, 05h, 070h, 04h, 060h, 03h, 050h         ; +0x21C
    db 02h, 030h, 00h, 00h, 01h, 01Eh, 0Ah, 00h, 01Eh, 034h, 0Eh, 00h           ; +0x228
    db 01Eh, 052h, 01Ah, 0F0h, 018h, 0E0h, 016h, 0D0h, 014h, 0C0h, 012h, 070h   ; +0x234
    db 011h, 060h, 010h, 050h, 01h, 013h, 01h, 00h, 013h, 042h, 00h, 00h        ; +0x240
    db 01h, 012h, 01h, 00h, 012h, 022h, 00h, 00h, 00h, 00h, 00h, 00h            ; +0x24C
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x258
    db 0FFh, 0FFh, 0FFh, 0FFh, 00h, 00h, 00h, 00h, 0B0h, 032h, 00h, 00h         ; +0x264
    db 01h, 00h, 00h, 00h, 04h, 00h, 00h, 00h, 04h, 00h, 00h, 00h               ; +0x270
    db 088h, 032h, 00h, 00h, 098h, 032h, 00h, 00h, 0A8h, 032h, 00h, 00h         ; +0x27C
    db 048h, 022h, 00h, 00h, 038h, 023h, 00h, 00h, 07Ch, 023h, 00h, 00h         ; +0x288
    db 084h, 023h, 00h, 00h, 0BFh, 032h, 00h, 00h, 0CBh, 032h, 00h, 00h         ; +0x294
    db 0D7h, 032h, 00h, 00h, 0EAh, 032h, 00h, 00h, 00h, 00h, 01h, 00h           ; +0x2A0
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2AC
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2B8
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2C4
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2D0
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2DC
    db 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h, 00h               ; +0x2E8
    db 074h, 065h, 05Fh, 073h, 069h, 07Ah, 065h, 00h                            ; +0x2F4

END