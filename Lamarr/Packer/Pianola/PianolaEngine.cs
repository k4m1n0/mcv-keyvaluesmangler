namespace Lamarr.Packer;
using System.Text;

public static partial class PianolaEngine
{
    const int W8 = 0, W16 = 1, W32 = 2, W64 = 3;//opcode = 族基址 + 宽度w 0=8 1=16 2=32 3=64操作数 = [rs*16+rd]
    const int OP_END = 0x00, OP_RET = 0x01, OP_BAD = 0x7F;
    const int OP_MOVIMM = 0x08, OP_MOVREG = 0x09;
    const int OP_LD = 0x10, OP_ST = 0x14;
    const int OP_BSWAP = 0x18;
    const int OP_ADD = 0x20, OP_SUB = 0x30, OP_MUL = 0x40, OP_DIVU = 0x50, OP_MODU = 0x60,
        OP_AND = 0x70, OP_OR = 0x80, OP_XOR = 0x90, OP_SHL = 0xA0, OP_SHR = 0xB0, OP_ASR = 0xC0,
        OP_ROL = 0xD0, OP_ROR = 0xE0;
    const int OP_NOT = 0xF0;
    const int OP_CMP = 0x1C, OP_JMP = 0x1D, OP_JCC = 0x1E, OP_CALL = 0x1F;
    const int NReg = 16;
    //内存映射 低段是数据页与虚拟机栈 高段是解码缓冲 私有上下文不在内存里 依次存放流密钥与代码基址与代码长度
    const int VM_DATA = 0x1000;    //数据页大小 = LD/ST上界
    const int VM_CODE = 0x1000;    //解码缓冲基址
    const int VM_CODE_END = 0x1FF0;//解码缓冲上界
    const int VM_MEM = 0x2000;     //cbM下限
    const int VM_CTX_OFF = 128;    //pRegs私有区偏移
    const int CallMax = 64;//CALL深度上限 返回地址放在栈帧高位窗口 窗口200h正好装64个

    //at-rest密钥流是ChaCha12 asm侧a_rest helper按同一算式展开
    //4B域键派生key32与nonce12 用ChaCha12自身以key全0与nonce由uKey与D0与D1组成且ctr为1的一块
    //流本体自ctr为0起逐64B块推进 机器无关且不引K 字节序与读取次序与uFold的先后一律不动
    const uint AR_D0 = 0x41525431u;//'AR1'
    const uint AR_D1 = 0x41525432u;//'AR2'

    static void AtRestSched(uint uKey, out byte[] rgKey32, out byte[] rgNonce12)
    {
        byte[] rgN = new byte[12];
        rgN[0] = (byte)(uKey & 0xFF); rgN[1] = (byte)((uKey >> 8) & 0xFF); rgN[2] = (byte)((uKey >> 16) & 0xFF); rgN[3] = (byte)((uKey >> 24) & 0xFF);
        rgN[4] = (byte)(AR_D0 & 0xFF); rgN[5] = (byte)((AR_D0 >> 8) & 0xFF); rgN[6] = (byte)((AR_D0 >> 16) & 0xFF); rgN[7] = (byte)((AR_D0 >> 24) & 0xFF);
        rgN[8] = (byte)(AR_D1 & 0xFF); rgN[9] = (byte)((AR_D1 >> 8) & 0xFF); rgN[10] = (byte)((AR_D1 >> 16) & 0xFF); rgN[11] = (byte)((AR_D1 >> 24) & 0xFF);
        byte[] rgBlk = new byte[64];
        ChaCha.Block(new byte[32], rgN, 1, rgBlk, 12);//KDF块key全0 ctr为1 与流本体从ctr0起分离域
        rgKey32 = new byte[32]; rgNonce12 = new byte[12];
        Array.Copy(rgBlk, 0, rgKey32, 0, 32);
        Array.Copy(rgBlk, 32, rgNonce12, 0, 12);
        Array.Clear(rgBlk, 0, rgBlk.Length);
    }

    public static void AtRestCrypt(byte[] rg, uint uKey, bool bBe = true)//at-rest逐字异或 走ChaCha12流key为0的替换只在本函数里
    {
        uint uK = uKey != 0 ? uKey : 0x9E3779B9u;
        AtRestSched(uK, out byte[] rgKey32, out byte[] rgNonce12);
        byte[] rgBlk = new byte[64];
        uint uCtr = 0;
        for (int i = 0; i + 4 <= rg.Length; i += 4)
        {
            int iW = (i >> 2) & 15;
            if (iW == 0) ChaCha.Block(rgKey32, rgNonce12, uCtr++, rgBlk, 12);
            uint uS = (uint)(rgBlk[iW * 4] | (rgBlk[iW * 4 + 1] << 8) | (rgBlk[iW * 4 + 2] << 16) | (rgBlk[iW * 4 + 3] << 24));
            uint uV = bBe
                ? ((uint)rg[i] << 24) | ((uint)rg[i + 1] << 16) | ((uint)rg[i + 2] << 8) | rg[i + 3]
                : (uint)rg[i] | ((uint)rg[i + 1] << 8) | ((uint)rg[i + 2] << 16) | ((uint)rg[i + 3] << 24);
            uV ^= uS;
            rg[i] = (byte)(uV >> 24); rg[i + 1] = (byte)(uV >> 16); rg[i + 2] = (byte)(uV >> 8); rg[i + 3] = (byte)uV;
        }
    }

    //链式at-rest与AtRestCrypt同流 额外返回本案明文指令字的折叠量 引擎在解码循环里逐字异或进r15
    //不做key为0的替换 引擎侧没有这一替换 链上必须逐位相同
    public static uint AtRestCryptChain(byte[] rg, uint uKey, bool bBe = true)
    {
        AtRestSched(uKey, out byte[] rgKey32, out byte[] rgNonce12);
        byte[] rgBlk = new byte[64];
        uint uCtr = 0;
        uint uFold = 0;
        for (int i = 0; i + 4 <= rg.Length; i += 4)
        {
            int iW = (i >> 2) & 15;
            if (iW == 0) ChaCha.Block(rgKey32, rgNonce12, uCtr++, rgBlk, 12);
            uint uS = (uint)(rgBlk[iW * 4] | (rgBlk[iW * 4 + 1] << 8) | (rgBlk[iW * 4 + 2] << 16) | (rgBlk[iW * 4 + 3] << 24));
            uint uV = bBe
                ? ((uint)rg[i] << 24) | ((uint)rg[i + 1] << 16) | ((uint)rg[i + 2] << 8) | rg[i + 3]
                : (uint)rg[i] | ((uint)rg[i + 1] << 8) | ((uint)rg[i + 2] << 16) | ((uint)rg[i + 3] << 24);
            uFold ^= uV;
            uV ^= uS;
            rg[i] = (byte)(uV >> 24); rg[i + 1] = (byte)(uV >> 16); rg[i + 2] = (byte)(uV >> 8); rg[i + 3] = (byte)uV;
        }
        return uFold;
    }

    public static uint ChainMix(uint u)//收尾混合用murmur3终结器 单元i+1的编码键等于uKey异或本值
    {
        u ^= u >> 16; u *= 0x85EBCA6Bu;
        u ^= u >> 13; u *= 0xC2B2AE35u;
        u ^= u >> 16;
        return u;
    }

    public static uint TextSum(byte[] rgDll)
    {
        uint uH = 0x811C9DC5u;//与引擎侧初值相同 空.text也返回该值
        if (rgDll == null || rgDll.Length < 0x40) return uH;
        int iPe = BitConverter.ToInt32(rgDll, 0x3C);
        if (iPe <= 0 || iPe + 0x40 > rgDll.Length) return uH;
        int iSecCnt = BitConverter.ToUInt16(rgDll, iPe + 6);
        int iSecOff = iPe + 24 + BitConverter.ToUInt16(rgDll, iPe + 20);
        for (int i = 0; i < iSecCnt; i++)
        {
            int o = iSecOff + i * 40;
            if (o + 40 > rgDll.Length) return uH;
            if (Encoding.ASCII.GetString(rgDll, o, 8).TrimEnd('\0') != ".text") continue;
            int iRawSz = BitConverter.ToInt32(rgDll, o + 16);
            int iRaw = BitConverter.ToInt32(rgDll, o + 20);
            if (iRawSz <= 0 || iRaw < 0 || iRaw + iRawSz > rgDll.Length) return uH;
            for (int k = 0; k < iRawSz; k++) { uH ^= rgDll[iRaw + k]; uH *= 0x01000193u; }
            return uH;
        }
        return uH;
    }

    //布局置换管引擎代码怎么排 字节置换管字节含义 两者都只做行为不变的置换
    static ulong _uSkel;
    static void SkelBegin(ulong uSeed) => _uSkel = uSeed ^ 0xD1B54A32D192ED03UL;
    static ulong SkelNext()
    {
        _uSkel += 0x9E3779B97F4A7C15UL;
        ulong z = _uSkel;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    static T[] SkelShuffle<T>(T[] rg)
    {
        for (int i = rg.Length - 1; i > 0; i--)
        {
            int j = (int)(SkelNext() % (ulong)(i + 1));
            (rg[i], rg[j]) = (rg[j], rg[i]);
        }
        return rg;
    }
    static string SkelPostProcess(string sAsm)//文本级后处理只动两类 同一寄存器上连续的cmp rX,K与je L是互斥测试 交换顺序行为不变 h_exit的ret之后与h_perm数据之前插种子派生死块
    {
        string[] rgLine = sAsm.Replace("\r\n", "\n").Split('\n');
        var lst = new System.Collections.Generic.List<string>(rgLine.Length + 64);
        int i = 0;
        while (i < rgLine.Length)
        {
            string sReg;
            if (i + 1 < rgLine.Length && IsSkelCmp(rgLine[i], out sReg) && IsSkelJe(rgLine[i + 1]))
            {
                string sRegRun = sReg;
                int iEnd = i;
                var lstPair = new System.Collections.Generic.List<string[]>();
                while (iEnd + 1 < rgLine.Length && IsSkelCmp(rgLine[iEnd], out sReg) && sReg == sRegRun && IsSkelJe(rgLine[iEnd + 1]))
                {
                    lstPair.Add(new[] { rgLine[iEnd], rgLine[iEnd + 1] });
                    iEnd += 2;
                }
                string[][] rgPair = lstPair.ToArray();
                if (rgPair.Length > 1) SkelShuffle(rgPair);
                foreach (string[] rgP in rgPair) { lst.Add(rgP[0]); lst.Add(rgP[1]); }
                i = iEnd;
                continue;
            }
            if (rgLine[i].TrimEnd() == "h_perm:") SkelDeadBlocks(lst);
            lst.Add(rgLine[i]);
            i++;
        }
        return string.Join(System.Environment.NewLine, lst);
    }
    static bool IsSkelCmp(string sLine, out string sReg)
    {
        sReg = "";
        string s = sLine.Trim();
        if (!s.StartsWith("cmp ")) return false;
        int iC = s.IndexOf(',');
        if (iC < 0) return false;
        sReg = s.Substring(4, iC - 4).Trim();
        return sReg.Length > 0 && sReg != "r12d";//r12d是at-rest流密钥 不在寄存器链内 出现就不参与置换
    }
    static bool IsSkelJe(string sLine) => sLine.Trim().StartsWith("je ");
    static void SkelDeadBlocks(System.Collections.Generic.List<string> lst)//死块2..5块x 3..8行db每块自带jmp自环
    {
        int iBlocks = 2 + (int)(SkelNext() % 4);
        for (int b = 0; b < iBlocks; b++)
        {
            lst.Add($"h_dead{b}:");
            int iRows = 3 + (int)(SkelNext() % 6);
            for (int r = 0; r < iRows; r++)
            {
                var rgB = new string[8];
                for (int k = 0; k < 8; k++) rgB[k] = $"0{(byte)(SkelNext() & 0xFFu):X2}h";
                lst.Add("    db " + string.Join(", ", rgB));
            }
            lst.Add("    jmp h_dead" + b);
        }
    }

    static readonly (int, string)[] Fam = { (OP_ADD,"add"),(OP_SUB,"sub"),(OP_MUL,"mul"),
        (OP_DIVU,"divu"),(OP_MODU,"modu"),(OP_AND,"and"),(OP_OR,"or"),(OP_XOR,"xor"),
        (OP_SHL,"shl"),(OP_SHR,"shr"),(OP_ASR,"asr"),(OP_ROL,"rol"),(OP_ROR,"ror") };
    static readonly (int, string)[] FamUn = { (OP_NOT,"not") };

    public static byte[] BuildDispatchPerm(ulong uSeed, string sIsa, out byte[] rgPiOut)//dispatch置换表的唯一计算入口 与写进boot的模板必须逐位一致
    {
        ulong uM = uSeed ^ 0x9E3779B97F4A7C15UL;//内部操作码表由种子派生 内部操作码槽位映射真置换表
        uM ^= uM << 13; uM ^= uM >> 7; uM ^= uM << 17;
        byte[] rgPi = BuildOpPerm(uM);
        rgPiOut = rgPi;
        OpTable.Begin(uSeed, sIsa);//h_perm把操作码置换的槽位映射到布局置换的槽位 操作码置换恒等时为空操作
        CarrierFmt.Begin(uSeed);//载体宽度码表以字节表写进镜像
        SkelBegin(uSeed);//布局置换与操作码置换分属两域 都只看种子与ISA 互不影响
        byte[] rgPerm = (byte[])rgPi.Clone();
        byte[] rgS = OpTable.Sigma;
        int[] rgCanon = new int[] { OP_END, OP_RET, OP_MOVIMM, OP_MOVREG, OP_CMP, OP_JMP, OP_JCC, OP_CALL };
        for (int i = 0; i < rgCanon.Length; i++) rgPerm[rgS[rgCanon[i]]] = rgPi[rgCanon[i]];
        for (int w = 0; w < 4; w++)
        {
            rgPerm[rgS[OP_LD + w]] = rgPi[OP_LD + w];
            rgPerm[rgS[OP_ST + w]] = rgPi[OP_ST + w];
            rgPerm[rgS[OP_BSWAP + w]] = rgPi[OP_BSWAP + w];//BSWAP的四宽度也要进置换表
        }
        foreach (var f in Fam) for (int w = 0; w < 4; w++) rgPerm[rgS[f.Item1 + w]] = rgPi[f.Item1 + w];//ALU与NOT的8/16位也是原生能力 四宽度全部进表
        foreach (var f in FamUn) for (int w = 0; w < 4; w++) rgPerm[rgS[f.Item1 + w]] = rgPi[f.Item1 + w];
        return rgPerm;
    }

    //at-rest流在asm侧与C#侧必须逐位相同 差异即链上分歧
    //栈布局以调用点rsp加8为基准 保存区必须含rax 因为调用点把指令字放在eax
    //helper不push也不pop 全部搬进保存区 体内rsp恒定所以偏移一律rsp加8加K
    const int AR_SAVE = 0x00, AR_UKEY = 0x58, AR_KEY = 0x60, AR_NON = 0x80, AR_CTR = 0x8C, AR_CUR = 0x90, AR_ST = 0x98;
    const int AR_FRAME = 0xE0;
    static readonly string[] rgArSave = { "rbx", "rcx", "rdx", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "rax" };

    static void EmitAtRestStream(StringBuilder sb)
    {
        sb.AppendLine("; at-rest stream = ChaCha12 (state + 64B block live on the host stack)");
        sb.AppendLine("; caller did 'sub rsp,0E0h'; r12d = the 4B domain key on entry to a_rest_init.");
        sb.AppendLine("; a_rest returns the next keystream u32 in edx; all other registers are preserved (incl. r12).");
        sb.AppendLine("a_rest_init:");
        for (int i = 0; i < rgArSave.Length; i++) sb.AppendLine($"    mov [rsp+8+0{AR_SAVE + i * 8:X2}h], {rgArSave[i]}");
        sb.AppendLine($"    mov [rsp+8+0{AR_UKEY:X2}h], r12d ; domain key (KDF input)");
        EmitArBlock(sb, true, "ar_i");//KDF块key全0 ctr为1 nonce由uKey与AR1与AR2组成
        for (int i = 0; i < 8; i++)
        {
            sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_ST + i * 4:X2}h]");
            sb.AppendLine($"    mov [rsp+8+0{AR_KEY + i * 4:X2}h], eax ; key32[{i}]");
        }
        for (int i = 0; i < 3; i++)
        {
            sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_ST + 32 + i * 4:X2}h]");
            sb.AppendLine($"    mov [rsp+8+0{AR_NON + i * 4:X2}h], eax ; nonce12[{i}]");
        }
        sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_CTR:X2}h], 0 ; stream counter starts at 0");
        sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_CUR:X2}h], 16 ; force the first a_rest call to make block 0");
        for (int i = rgArSave.Length - 1; i >= 0; i--) sb.AppendLine($"    mov {rgArSave[i]}, [rsp+8+0{AR_SAVE + i * 8:X2}h]");
        sb.AppendLine("    ret");
        sb.AppendLine("a_rest:");
        for (int i = 0; i < rgArSave.Length; i++) sb.AppendLine($"    mov [rsp+8+0{AR_SAVE + i * 8:X2}h], {rgArSave[i]}");
        sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_CUR:X2}h]");
        sb.AppendLine("    cmp eax, 16");
        sb.AppendLine("    jb ar_have");
        EmitArBlock(sb, false, "ar_s");//流块key32与nonce12与ctr都取自槽位 尾部把ctr自增且cursor归0
        sb.AppendLine("ar_have:");
        sb.AppendLine($"    mov ecx, dword ptr [rsp+8+0{AR_CUR:X2}h] ; index for this word (rcx is dead after dec_entry)");
        sb.AppendLine($"    inc dword ptr [rsp+8+0{AR_CUR:X2}h]");
        for (int i = rgArSave.Length - 1; i >= 0; i--)
        {
            if (rgArSave[i] == "rcx" || rgArSave[i] == "rdx") continue;
            sb.AppendLine($"    mov {rgArSave[i]}, [rsp+8+0{AR_SAVE + i * 8:X2}h]");
        }
        sb.AppendLine($"    mov rdx, [rsp+8+0{AR_SAVE + 16:X2}h] ; restore the caller's rdx (its old value is dead here)");
        sb.AppendLine($"    mov edx, dword ptr [rsp+0{8 + AR_ST:X2}h+rcx*4] ; keystream word to edx");
        sb.AppendLine($"    mov rcx, [rsp+8+0{AR_SAVE + 8:X2}h] ; restore the caller's rcx");
        sb.AppendLine("    ret");
        sb.AppendLine("align 8");
        sb.AppendLine("at_rest_tbl:");
        for (int p = 0; p < 8; p++)
        {
            var rg = new int[4];
            for (int r = 0; r < 4; r++) rg[r] = p < 4 ? p + 4 * r : (r == 0 ? p - 4 : 4 * r + (((p - 4) + r) & 3));
            sb.AppendLine("    db " + string.Join(",", rg));
        }
    }

    static void EmitArBlock(StringBuilder sb, bool bKdf, string sLbl)//一个ChaCha12块 状态就地建在AR_ST 6个双轮乘8个QR 末尾把输出加初始写回
    {
        var rgC = new[] { "61707865", "3320646E", "79622D32", "6B206574" };
        for (int i = 0; i < 4; i++) sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_ST + i * 4:X2}h], 0{rgC[i]}h");
        for (int i = 0; i < 8; i++)
        {
            if (bKdf) sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_ST + 16 + i * 4:X2}h], 0");
            else
            {
                sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_KEY + i * 4:X2}h]");
                sb.AppendLine($"    mov [rsp+8+0{AR_ST + 16 + i * 4:X2}h], eax");
            }
        }
        if (bKdf)
        {
            sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_ST + 48:X2}h], 1 ; KDF ctr=1 (domain split)");
            sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_UKEY:X2}h]");
            sb.AppendLine($"    mov [rsp+8+0{AR_ST + 52:X2}h], eax");
            sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_ST + 56:X2}h], 041525431h ; 'AR1'");
            sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_ST + 60:X2}h], 041525432h ; 'AR2'");
        }
        else
        {
            sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_CTR:X2}h]");
            sb.AppendLine($"    mov [rsp+8+0{AR_ST + 48:X2}h], eax");
            for (int i = 0; i < 3; i++)
            {
                sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_NON + i * 4:X2}h]");
                sb.AppendLine($"    mov [rsp+8+0{AR_ST + 52 + i * 4:X2}h], eax");
            }
        }
        sb.AppendLine($"    lea rdi, [rsp+8+0{AR_ST:X2}h]");
        sb.AppendLine("    lea rbx, at_rest_tbl");
        sb.AppendLine("    xor r12d, r12d");
        sb.AppendLine(sLbl + "_dr:");
        sb.AppendLine("    xor esi, esi");
        sb.AppendLine(sLbl + "_qr:");
        sb.AppendLine("    movzx r8d,  byte ptr [rbx+rsi*4+0]");
        sb.AppendLine("    movzx r9d,  byte ptr [rbx+rsi*4+1]");
        sb.AppendLine("    movzx r10d, byte ptr [rbx+rsi*4+2]");
        sb.AppendLine("    movzx r11d, byte ptr [rbx+rsi*4+3]");
        sb.AppendLine("    mov eax, [rdi+r8*4] ; a");
        sb.AppendLine("    add eax, [rdi+r9*4] ; a += b");
        sb.AppendLine("    mov [rdi+r8*4], eax");
        sb.AppendLine("    mov edx, [rdi+r11*4] ; d");
        sb.AppendLine("    xor edx, eax ; d ^= a");
        sb.AppendLine("    rol edx, 16");
        sb.AppendLine("    mov [rdi+r11*4], edx");
        sb.AppendLine("    mov ecx, [rdi+r10*4] ; c");
        sb.AppendLine("    add ecx, edx ; c += d");
        sb.AppendLine("    mov [rdi+r10*4], ecx");
        sb.AppendLine("    mov edx, [rdi+r9*4] ; b");
        sb.AppendLine("    xor edx, ecx ; b ^= c");
        sb.AppendLine("    rol edx, 12");
        sb.AppendLine("    mov [rdi+r9*4], edx");
        sb.AppendLine("    mov eax, [rdi+r8*4] ; a");
        sb.AppendLine("    add eax, edx ; a += b");
        sb.AppendLine("    mov [rdi+r8*4], eax");
        sb.AppendLine("    mov edx, [rdi+r11*4] ; d");
        sb.AppendLine("    xor edx, eax ; d ^= a");
        sb.AppendLine("    rol edx, 8");
        sb.AppendLine("    mov [rdi+r11*4], edx");
        sb.AppendLine("    mov ecx, [rdi+r10*4] ; c");
        sb.AppendLine("    add ecx, edx ; c += d");
        sb.AppendLine("    mov [rdi+r10*4], ecx");
        sb.AppendLine("    mov edx, [rdi+r9*4] ; b");
        sb.AppendLine("    xor edx, ecx ; b ^= c");
        sb.AppendLine("    rol edx, 7");
        sb.AppendLine("    mov [rdi+r9*4], edx");
        sb.AppendLine("    inc esi");
        sb.AppendLine("    cmp esi, 8");
        sb.AppendLine($"    jb {sLbl}_qr");
        sb.AppendLine("    inc r12d");
        sb.AppendLine("    cmp r12d, 6");
        sb.AppendLine($"    jb {sLbl}_dr");
        for (int i = 0; i < 16; i++)
        {
            sb.AppendLine($"    mov eax, dword ptr [rsp+8+0{AR_ST + i * 4:X2}h]");
            if (i < 4) sb.AppendLine($"    add eax, 0{rgC[i]}h");
            else if (i < 12) { if (!bKdf) sb.AppendLine($"    add eax, dword ptr [rsp+8+0{AR_KEY + (i - 4) * 4:X2}h]"); }
            else if (i == 12) sb.AppendLine(bKdf ? "    add eax, 1" : $"    add eax, dword ptr [rsp+8+0{AR_CTR:X2}h]");
            else if (bKdf)
            {
                if (i == 13) sb.AppendLine($"    add eax, dword ptr [rsp+8+0{AR_UKEY:X2}h]");
                else sb.AppendLine($"    add eax, 0{(i == 14 ? "41525431" : "41525432")}h");
            }
            else sb.AppendLine($"    add eax, dword ptr [rsp+8+0{AR_NON + (i - 13) * 4:X2}h]");
            sb.AppendLine($"    mov [rsp+8+0{AR_ST + i * 4:X2}h], eax");
        }
        if (!bKdf)
        {
            sb.AppendLine($"    inc dword ptr [rsp+8+0{AR_CTR:X2}h]");
            sb.AppendLine($"    mov dword ptr [rsp+8+0{AR_CUR:X2}h], 0");
        }
    }

public static string Generate(ulong uSeed, string sIsa = "ppc")//内部字节码解释器 寄存器组按基址加序号寻址 标志位由两个位承载 调用约定按平台接口
    {
        byte[] rgPerm = BuildDispatchPerm(uSeed, sIsa, out byte[] rgPi);
        var sb = new StringBuilder(256 * 1024);
        sb.AppendLine($"; Pianola engine (plain interpreter) seed=0x{uSeed:X16})");
        sb.AppendLine(".code");
        sb.AppendLine("PUBLIC PianolaRun");
        sb.AppendLine("PianolaRun:");
        sb.AppendLine("    push rbx");
        sb.AppendLine("    push rsi");
        sb.AppendLine("    push rdi");
        sb.AppendLine("    push r12");
        sb.AppendLine("    push r13");
        sb.AppendLine("    push r14");
        sb.AppendLine("    push r15");
        sb.AppendLine("    push rbp ; VM stack pointer (M is the VM's own memory: no host stack state)");
        sb.AppendLine($"    sub rsp, 0{AR_FRAME:X2}h ; the at-rest helper host stack frame (state/block/saved regs)");
        sb.AppendLine("    mov rbx, rdx ; pRegs v[16] (private ctx follows at VM_CTX_OFF)");
        sb.AppendLine("    mov rdi, r8 ; pM (data memory base)");
        sb.AppendLine("    mov r13, r9 ; cbM");
        sb.AppendLine("    xor r12d, r12d ; flags");
        sb.AppendLine("    xor r14d, r14d ; spare/zero");
        sb.AppendLine("    xor r15d, r15d ; call depth counter, return addresses live on the host stack");
        bool bCarrier = sIsa == "ppc" || sIsa == "rv64" || sIsa == "mips64" || sIsa == "loongarch" || sIsa == "alpha";
        sb.AppendLine("    cmp r13, 2000h ; cbM must cover the data page plus the code window");//宿主栈不参与VM状态 VM栈全落在M里 cbM不足数据页+代码窗口即h_bad VM栈指针在rbp自数据页顶向下
        sb.AppendLine("    jb h_bad");
        sb.AppendLine("    lea rbp, [rdi+1000h] ; VM stack pointer: grows down inside the data page");
        if (bCarrier)
        {
            sb.AppendLine("    mov r12d, dword ptr [rbx+128] ; at-rest stream key: private ctx, NOT in VM memory");//解密密钥 = 调用方置入的m_key ^ 引擎自身.text的滚动摘录
            sb.AppendLine("    mov r10, qword ptr [rbx+136] ; absolute address of this engine .text (private ctx)");
            sb.AppendLine("    mov r11d, dword ptr [rbx+144] ; length of this engine .text (private ctx)");
            sb.AppendLine("    mov eax, 0811C9DC5h");
            sb.AppendLine("    xor esi, esi");
            sb.AppendLine("    test r11d, r11d");
            sb.AppendLine("    jz key_mix_done");
            sb.AppendLine("key_mix:");
            sb.AppendLine("    movzx edx, byte ptr [r10+rsi]");
            sb.AppendLine("    xor eax, edx");
            sb.AppendLine("    imul eax, eax, 01000193h");
            sb.AppendLine("    inc esi");
            sb.AppendLine("    cmp esi, r11d");
            sb.AppendLine("    jb key_mix");
            sb.AppendLine("key_mix_done:");
            sb.AppendLine("    xor r12d, eax ; bound to its own measurement");
            sb.AppendLine("    xor r12d, dword ptr [rbx+148] ; chain state in: 0 only for the first unit of a run");
            sb.AppendLine("    ; window contract: the caller declares the material window it drives (ctx+160).");
            sb.AppendLine("    ; v3 (material length) must fit inside it, an over-window material is rejected here,");
            sb.AppendLine("    ; so the VM never carries more than one window of caller data at a time.");
            sb.AppendLine("    ; NB: rcx still holds pBc and is consumed by dec_entry, so use rdx/eax only here.");
            sb.AppendLine("    mov eax, dword ptr [rbx+160]");
            sb.AppendLine("    test eax, eax");
            sb.AppendLine("    jz win_ok");
            sb.AppendLine("    mov edx, dword ptr [rbx+24]");
            sb.AppendLine("    cmp edx, eax");
            sb.AppendLine("    ja h_bad");
            sb.AppendLine("    mov dword ptr [rbx+164], edx ; stash the material length for the exit wipe");
            sb.AppendLine("win_ok:");
            sb.AppendLine("    call a_rest_init ; init the at-rest stream (reads the domain key in r12d)");
            sb.AppendLine("    jmp dec_entry");
            EmitAtRestStream(sb);
        }
        else
        {
            sb.AppendLine("    mov rsi, rcx ; pc, internal bytecode, carrier form is not enabled for this ISA");
        }
        sb.AppendLine("main_loop:");
        sb.AppendLine("    movzx eax, byte ptr [rsi]");
        //run-instance表由调用方提供ctx+180处是指针 为0时回退内置表以免影响自测与triage
        sb.AppendLine("    mov r10, qword ptr [rbx+180] ; run-instance dispatch base (0 = built-in)");
        sb.AppendLine("    cmp dword ptr [rbx+188], 05A17C0DEh ; opt-in tag: only a caller that really installed tables sets it");
        sb.AppendLine("    jne ml_perm0");
        sb.AppendLine("    test r10, r10");
        sb.AppendLine("    jz ml_perm0");
        sb.AppendLine("    movzx eax, byte ptr [r10+rax]");
        sb.AppendLine("    jmp ml_slot");
        sb.AppendLine("ml_perm0:");
        sb.AppendLine("    lea r10, [h_perm]");
        sb.AppendLine("    movzx eax, byte ptr [r10+rax] ; opcode to handler slot (permutation table)");
        sb.AppendLine("ml_slot:");
        sb.AppendLine("    inc rsi");
        sb.AppendLine("    mov r10, qword ptr [rbx+180] ; reload: the perm lookup used r10 as a base");
        sb.AppendLine("    cmp dword ptr [rbx+188], 05A17C0DEh");
        sb.AppendLine("    jne ml_tbl0");
        sb.AppendLine("    test r10, r10");
        sb.AppendLine("    jz ml_tbl0");
        sb.AppendLine("    mov rax, qword ptr [r10+rax*8+256] ; absolute handler address (run-instance table)");
        sb.AppendLine("    jmp rax");
        sb.AppendLine("ml_tbl0:");
        sb.AppendLine("    lea r10, [h_tbl]");
        sb.AppendLine("    movsxd rax, dword ptr [r10+rax*4] ; relative offset, position independent");
        sb.AppendLine("    add rax, r10");
        sb.AppendLine("    jmp rax");

        foreach (var fnBody in SkelShuffle(new System.Action<System.Text.StringBuilder>[] {//处理器块顺序逐产物打乱 每块都自终止 h_tbl按标签取址 顺序无含义
            EmitEnd, EmitMov, EmitAluFam, EmitAluUn, EmitCmp, EmitBranch, EmitLdSt })) fnBody(sb);
        if (bCarrier)
        {
            switch (sIsa)
            {
                case "ppc": EmitIsaDecodePpc(sb); break;
                case "rv64": EmitIsaDecodeRv(sb); EmitIsaDecodeRv2(sb); break;
                case "mips64": EmitIsaDecodeMi(sb); EmitIsaDecodeMi2(sb); break;
                case "loongarch": EmitIsaDecodeLa(sb); EmitIsaDecodeLa2(sb); EmitIsaDecodeLa3(sb); break;
                case "alpha": EmitIsaDecodeAl(sb); EmitIsaDecodeAl2(sb); EmitIsaDecodeAl3(sb); break;
            }
            EmitIsaDecodeDone(sb);
        }

        sb.AppendLine("h_bad:");
        sb.AppendLine("    mov rax, -1");
        sb.AppendLine("h_exit:");
        sb.AppendLine("    ; chained at-rest: finalise the next chain state from what this run actually executed.");
        sb.AppendLine("    ; ctx[156] holds ChainMix(in ^ plaintext-fold) from dec_done; fold in the execution side");
        sb.AppendLine("    ; effects (the word the unit left in M[0x20] and its returned v0). A failed run (rax=-1)");
        sb.AppendLine("    ; publishes 0, so the chain cannot continue past a call that did not do its job.");
        sb.AppendLine("    cmp rax, -1");
        sb.AppendLine("    je chain_zero");
        sb.AppendLine("    mov edx, dword ptr [rbx+156]");
        sb.AppendLine("    ; the chain fold must read VM [0x20,0x24) through the page table. A raw read");
        sb.AppendLine("    ; would take the physical slot and make the published chain differ from the packed");
        sb.AppendLine("    ; one, and the driver's next unit key (rollKey ^ chain) would then be wrong.");
        sb.AppendLine("    mov ecx, 20h");
        sb.AppendLine("    mov r9, qword ptr [rbx+172]");
        sb.AppendLine("    test r9, r9");
        sb.AppendLine("    jz s4b_cfold");
        sb.AppendLine("    shr ecx, 10");
        sb.AppendLine("    mov ecx, dword ptr [r9+rcx*4]");
        sb.AppendLine("    and ecx, 3");
        sb.AppendLine("    shl ecx, 10");
        sb.AppendLine("    add ecx, 20h");
        sb.AppendLine("s4b_cfold:");
        sb.AppendLine("    mov ecx, dword ptr [rdi+rcx]");
        sb.AppendLine("    xor edx, ecx");
        sb.AppendLine("    mov ecx, eax");
        sb.AppendLine("    xor edx, ecx");
        sb.AppendLine("    mov ecx, edx");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    xor edx, ecx");
        sb.AppendLine("    mov ecx, 85EBCA6Bh");
        sb.AppendLine("    imul edx, ecx");
        sb.AppendLine("    mov ecx, edx");
        sb.AppendLine("    shr ecx, 13");
        sb.AppendLine("    xor edx, ecx");
        sb.AppendLine("    mov ecx, 0C2B2AE35h");
        sb.AppendLine("    imul edx, ecx");
        sb.AppendLine("    mov ecx, edx");
        sb.AppendLine("    shr ecx, 16");
        sb.AppendLine("    xor edx, ecx");
        sb.AppendLine("    mov dword ptr [rbx+152], edx ; chain state out, execution bound");
        sb.AppendLine("    jmp chain_done");
        sb.AppendLine("chain_zero:");
        sb.AppendLine("    mov dword ptr [rbx+152], 0 ; failed run: no usable next state");
        sb.AppendLine("chain_done:");
        sb.AppendLine("    ; the wipe walks the virtual window and zeroes the slot the table maps each byte");
        sb.AppendLine("    ; to. Wiping physical [0,len) instead would leave the material behind under a");
        sb.AppendLine("    ; permutation (it is spread over slots), which would silently break the identity-window contract.");
        sb.AppendLine("    ; The exempt result window is virtual [0x20,0x30). The driver reads it through the");
        sb.AppendLine("    ; same table, so both sides agree on where it lives.");
        sb.AppendLine("    mov ecx, dword ptr [rbx+168] ; 1 = material in/out (result lives in the material)");
        sb.AppendLine("    test ecx, ecx");
        sb.AppendLine("    jnz win_wipe_done");
        sb.AppendLine("    mov r9, qword ptr [rbx+172]");
        sb.AppendLine("    xor r10d, r10d");
        sb.AppendLine("    test r9, r9");
        sb.AppendLine("    jz win_res_ready");
        sb.AppendLine("    mov r10d, dword ptr [r9]");
        sb.AppendLine("    and r10d, 3");
        sb.AppendLine("    shl r10d, 10");
        sb.AppendLine("win_res_ready:");
        sb.AppendLine("    add r10d, 20h ; exempt start (physical)");
        sb.AppendLine("    mov r11d, r10d");
        sb.AppendLine("    add r11d, 10h ; exempt end");
        sb.AppendLine("    mov ecx, 1000h");
        sb.AppendLine("win_wipe_go:");
        sb.AppendLine("    xor edx, edx");
        sb.AppendLine("win_wipe_loop:");
        sb.AppendLine("    cmp edx, ecx");
        sb.AppendLine("    jae win_wipe_done");
        sb.AppendLine("    cmp edx, r10d");
        sb.AppendLine("    jb win_wipe_store");
        sb.AppendLine("    cmp edx, r11d");
        sb.AppendLine("    jb win_wipe_next");
        sb.AppendLine("win_wipe_store:");
        sb.AppendLine("    mov byte ptr [rdi+rdx], 0");
        sb.AppendLine("win_wipe_next:");
        sb.AppendLine("    inc edx");
        sb.AppendLine("    jmp win_wipe_loop");
        sb.AppendLine("win_wipe_done:");
        sb.AppendLine("    ; wipe the code window on the way out. The decoded bytecode must not survive the call");
        sb.AppendLine("    mov rcx, rdi");
        sb.AppendLine("    add rcx, 1000h");
        sb.AppendLine("    mov edx, 0FF0h");
        sb.AppendLine("wipe_code:");
        sb.AppendLine("    mov qword ptr [rcx], 0");
        sb.AppendLine("    add rcx, 8");
        sb.AppendLine("    sub edx, 8");
        sb.AppendLine("    jnz wipe_code");
        sb.AppendLine($"    add rsp, 0{AR_FRAME:X2}h ; drop the at-rest helper host stack frame");
        sb.AppendLine("    pop rbp");
        sb.AppendLine("    pop r15");
        sb.AppendLine("    pop r14");
        sb.AppendLine("    pop r13");
        sb.AppendLine("    pop r12");
        sb.AppendLine("    pop rdi");
        sb.AppendLine("    pop rsi");
        sb.AppendLine("    pop rbx");
        sb.AppendLine("    ret");

        sb.AppendLine("h_perm:");//位置无关派发 存相对h_tbl的32位偏移 同节常数无重定位 h_perm在h_tbl之前 运行时先把操作码查成槽位再按槽位取处理程序偏移
        for (int i = 0; i < 256; i += 16)
        {
            var rgRow = new string[16];
            for (int k = 0; k < 16; k++) rgRow[k] = $"0{rgPerm[i + k]:X2}h";
            sb.AppendLine("    db " + string.Join(", ", rgRow));
        }
        sb.AppendLine("h_tbl:");
        var rgL = new string[256];
        for (int i = 0; i < 256; i++) rgL[i] = "h_bad";
        void Map(int op, string s) { rgL[rgPi[op]] = s; }
        Map(OP_END, "h_end"); Map(OP_RET, "h_ret");
        Map(OP_MOVIMM, "h_movimm"); Map(OP_MOVREG, "h_movreg");
        for (int w = 0; w < 4; w++) { Map(OP_LD + w, "h_ld" + w); Map(OP_ST + w, "h_st" + w); Map(OP_BSWAP + w, "h_bswap" + w); }
        foreach (var f in Fam) for (int w = 0; w < 4; w++) Map(f.Item1 + w, "h_" + f.Item2 + w);
        foreach (var f in FamUn) for (int w = 0; w < 4; w++) Map(f.Item1 + w, "h_" + f.Item2 + w);
        Map(OP_CMP, "h_cmp"); Map(OP_JMP, "h_jmp"); Map(OP_JCC, "h_jcc"); Map(OP_CALL, "h_call");
        for (int i = 0; i < 256; i += 4)
            sb.AppendLine($"    dd {rgL[i]} - h_tbl, {rgL[i + 1]} - h_tbl, {rgL[i + 2]} - h_tbl, {rgL[i + 3]} - h_tbl");
        sb.AppendLine("d_wcode:");//宽度码表载体第三字段[4:3]的2位值 内部宽度码数据紧跟h_tbl 解码时lea取基址再查
        sb.AppendLine($"    db {CarrierFmt.Inv[0]}, {CarrierFmt.Inv[1]}, {CarrierFmt.Inv[2]}, {CarrierFmt.Inv[3]}");
        sb.AppendLine("END");
        return SkelPostProcess(sb.ToString());
    }

static string Wx(int v) => $"0{v:X}h";
    static string MaskStr(int w) => w switch { 0 => "0FFh", 1 => "0FFFFh", 2 => "0FFFFFFFFh", _ => "-1" };

    static byte[] BuildOpPerm(ulong uSeed)//操作码槽位置换表 splitmix64驱动Fisher-Yates同一个种子逐位可复现且必是256! 中的一员
    {
        var rgP = new byte[256];
        for (int i = 0; i < 256; i++) rgP[i] = (byte)i;
        ulong uS = uSeed;
        for (int i = 255; i > 0; i--)
        {
            uS += 0x9E3779B97F4A7C15UL;
            ulong uZ = uS;
            uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9UL;
            uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBUL;
            uZ ^= uZ >> 31;
            int j = (int)(uZ % (ulong)(i + 1));
            (rgP[i], rgP[j]) = (rgP[j], rgP[i]);
        }
        return rgP;
    }

    static void EmitIsaDecodeDone(StringBuilder sb)//解码末尾复位r12为执行期标志 初值后进入执行
    {
        sb.AppendLine("dec_done:");
        sb.AppendLine("    ; chained at-rest: stash ChainMix(in ^ fold of this unit's decoded words) in ctx[156].");
        sb.AppendLine("    ; The chain itself is finalised after execution (h_exit), so the state also binds what this");
        sb.AppendLine("    ; run actually produced: a run that did not execute cannot continue the chain.");
        sb.AppendLine("    mov eax, dword ptr [rbx+148] ; chain state in");
        sb.AppendLine("    xor eax, r15d ; ^ fold of the decoded plaintext words");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 16");
        sb.AppendLine("    xor eax, edx");
        sb.AppendLine("    mov edx, 85EBCA6Bh");
        sb.AppendLine("    imul eax, edx");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 13");
        sb.AppendLine("    xor eax, edx");
        sb.AppendLine("    mov edx, 0C2B2AE35h");
        sb.AppendLine("    imul eax, edx");
        sb.AppendLine("    mov edx, eax");
        sb.AppendLine("    shr edx, 16");
        sb.AppendLine("    xor eax, edx");
        sb.AppendLine("    mov dword ptr [rbx+156], eax ; decode half of the next state (kept for h_exit)");
        sb.AppendLine("    xor r15d, r15d ; the decode fold must not leak into the executor");
        sb.AppendLine("    xor r12d, r12d ; r12 held the stream state during decode, clear it before execution");
        sb.AppendLine("    lea rsi, [rdi+1000h] ; execute internal bytecode from the VM code window");
        sb.AppendLine("    jmp main_loop");
    }

    static uint XoOf2(int iFam) => NotenrolleIsa.XoForFam(iFam);//XO表只有编码器那一份 这里直接转调
}
