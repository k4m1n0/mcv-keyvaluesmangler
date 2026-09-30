namespace Lamarr.Packer;
using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Numerics;

internal partial class PeWriterAntheil
{
    //伪造元数据根 表流与字符串流指向诱饵
    private static byte[] BuildFakeBsjb(int iNameOff, int iNameLen, int iDecOff, int iDecLen)
    {
        byte[] rgB = new byte[64];
        rgB[0] = 0x42; rgB[1] = 0x53; rgB[2] = 0x4A; rgB[3] = 0x42;//BSJB
        rgB[4] = 0x01; rgB[5] = 0x00;                        //uMajor 1
        rgB[6] = 0x01; rgB[7] = 0x00;                        //minor 1
        rgB[12] = 0x0C;                                      //version iLen 12
        byte[] rgVer = Encoding.ASCII.GetBytes("v4.0.30319");
        Array.Copy(rgVer, 0, rgB, 16, rgVer.Length);
        rgB[30] = 0x02;                                      //2 streams
        BitConverter.GetBytes((uint)iDecOff).CopyTo(rgB, 32);//stream0 #~ 解码器数据区
        BitConverter.GetBytes((uint)iDecLen).CopyTo(rgB, 36);
        rgB[40] = 0x23; rgB[41] = 0x7E;                      //"#~"
        BitConverter.GetBytes((uint)iNameOff).CopyTo(rgB, 44);//stream1 #Strings名称区
        BitConverter.GetBytes((uint)iNameLen).CopyTo(rgB, 48);
        byte[] rgNs = Encoding.ASCII.GetBytes("#Strings");
        Array.Copy(rgNs, 0, rgB, 52, rgNs.Length);
        return rgB;
    }

    private static byte[] GenRandomX64(Random rng, int iCbMin, int iCbMax)
    {
        int iCbTarget = rng.Next(iCbMin, iCbMax);
        using var ms = new MemoryStream();
        if (rng.Next(2) == 0)
        {
            //传统形态的序言影子区 保存非易失寄存器与栈调整
            int nReg = 1 + rng.Next(4);
            var rgReg = new int[nReg];
            for (int i = 0; i < nReg; i++) rgReg[i] = rng.Next(8);
            for (int i = 0; i < nReg; i++) { byte[] m = MsvcSave(rgReg[i], i); ms.Write(m, 0, m.Length); }
            for (int i = nReg - 1; i >= 0; i--) { byte[] p = MsvcPush(rgReg[i]); ms.Write(p, 0, p.Length); }
            int iFr = 0x20 + 0x10 * rng.Next(8);
            ms.Write(new byte[] { 0x48, 0x83, 0xEC, (byte)iFr }, 0, 4);//sub rsp,N
            iCbTarget += 16 + nReg * 4;
            while (ms.Length < iCbTarget)
            {
                byte[] rgInsn = X64Insn(rng);
                ms.Write(rgInsn, 0, rgInsn.Length);
            }
            ms.Write(new byte[] { 0x48, 0x83, 0xC4, (byte)iFr }, 0, 4);//add rsp,N
            for (int i = 0; i < nReg; i++) { byte[] p = MsvcPop(rgReg[i]); ms.Write(p, 0, p.Length); }
            ms.Write(new byte[] { 0xC3 }, 0, 1);//ret
            return ms.ToArray();
        }
        if (rng.Next(4) == 0)
        {
            ms.Write(new byte[] { 0x55, 0x48, 0x8B, 0xEC }, 0, 4);//push rbp mov rbp,rsp
            iCbTarget += 4;
        }
        while (ms.Length < iCbTarget)
        {
            byte[] rgInsn = X64Insn(rng);
            ms.Write(rgInsn, 0, rgInsn.Length);
        }
        if (rng.Next(4) == 0)//尾声
            ms.Write(new byte[] { 0x5D, 0xC3 }, 0, 2);//pop rbp ret
        return ms.ToArray();
    }

    private static byte[] MsvcSave(int ri, int iOff)
    {
        int r = MsvcReg(ri);
        byte[] rg = new byte[r >= 8 ? 5 : 4];
        int p = 0;
        if (r >= 8) rg[p++] = 0x41;
        rg[p++] = 0x89;
        rg[p++] = (byte)(0x44 | ((r & 7) << 3));
        rg[p++] = 0x24;
        rg[p++] = (byte)(8 + iOff * 8);
        return rg;
    }

    private static byte[] MsvcPush(int ri)
    {
        int r = MsvcReg(ri);
        if (r < 8) return new byte[] { (byte)(0x50 + r) };
        return new byte[] { 0x41, (byte)(0x50 + (r - 8)) };
    }

    private static byte[] MsvcPop(int ri)
    {
        int r = MsvcReg(ri);
        if (r < 8) return new byte[] { (byte)(0x58 + r) };
        return new byte[] { 0x41, (byte)(0x58 + (r - 8)) };
    }

    private static byte[] X64Insn(Random rng)
    {
        switch (rng.Next(40))
        {
            case 0: return new byte[] { 0x48, 0x8B, (byte)(0xC0 + rng.Next(8)) };//mov rAX,rX
            case 1: return new byte[] { 0x48, 0x89, (byte)(0xC0 + rng.Next(8)) };//mov rX,rAX
            case 2: { var rgI = new byte[10]; rgI[0] = 0x48; rgI[1] = 0xB8; BitConverter.GetBytes(((ulong)(uint)rng.Next() << 32) | (uint)rng.Next()).CopyTo(rgI, 2); return rgI; }//mov rAX,imm64
            case 3: return new byte[] { 0x48, 0x83, (byte)(0xC0 + rng.Next(8)), (byte)rng.Next(256) };//add rX,imm8
            case 4: return new byte[] { 0x48, 0x29, (byte)(0xC0 + rng.Next(8)) };//sub rAX,rX
            case 5: return new byte[] { 0x48, 0x33, (byte)(0xC0 + rng.Next(8)) };//xor rAX,rX
            case 6: return new byte[] { 0x31, (byte)(0xC0 + rng.Next(8)) };//xor rX,rAX 32位
            case 7: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0x8D; rgI[2] = (byte)(0x80 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//lea rX,[rAX+disp32]
            case 8: { var rgI = new byte[7]; rgI[0] = 0x48; rgI[1] = 0xC7; rgI[2] = (byte)(0xC0 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 3); return rgI; }//mov rX,imm32
            case 9: return new byte[] { (byte)(0x50 + rng.Next(8)) };//push rX
            case 10: return new byte[] { (byte)(0x58 + rng.Next(8)) };//pop rX
            case 11: return new byte[] { 0x48, 0x85, (byte)(0xC0 + rng.Next(8)) };//test rAX,rX
            case 12: return new byte[] { 0x48, 0x39, (byte)(0xC0 + rng.Next(8)) };//cmp rAX,rX
            case 13: return new byte[] { 0x48, 0x63, (byte)(0xC0 + rng.Next(8)) };//movsxd rAX,rX
            case 14: return new byte[] { 0x48, 0x0F, 0xAF, (byte)(0xC0 + rng.Next(8)) };//imul rAX,rX
            case 15: return new byte[] { 0x48, 0x01, (byte)(0xC0 + rng.Next(8)) };//add rAX,rX
            case 16: return new byte[] { 0x48, 0x21, (byte)(0xC0 + rng.Next(8)) };//and rAX,rX
            case 17: return new byte[] { 0x48, 0x09, (byte)(0xC0 + rng.Next(8)) };//or rAX,rX
            case 18: return new byte[] { 0x48, 0xD1, (byte)(0xE0 + rng.Next(8)) };//shl rX,1
            case 19: return new byte[] { 0x48, 0xD1, (byte)(0xE8 + rng.Next(8)) };//shr rX,1
            case 20: return new byte[] { 0x48, 0xC1, (byte)(0xE0 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//shl rX,imm8
            case 21: return new byte[] { 0x48, 0x0F, 0x44, (byte)(0xC0 + rng.Next(8)) };//cmove rAX,rX
            case 22: return new byte[] { 0x48, 0xF7, (byte)(0xD0 + rng.Next(8)) };//not rX
            case 23: return new byte[] { 0x48, 0xF7, (byte)(0xD8 + rng.Next(8)) };//neg rX
            case 24: return new byte[] { 0x0F, 0x1F, 0x40, 0x00 };//多字节nop
            case 25: return new byte[] { 0xF3, 0x90 };//pause
            case 26: return new byte[] { 0xEB, (byte)(1 + rng.Next(127)) };//jmp rel8正向前跳
            case 27: { var rgI = new byte[6]; rgI[0] = 0x0F; rgI[1] = (byte)(0x80 + rng.Next(16)); BitConverter.GetBytes(rng.Next(0, 0x20000)).CopyTo(rgI, 2); return rgI; }//jcc rel32
            case 28: { var rgI = new byte[5]; rgI[0] = 0xE8; BitConverter.GetBytes(0).CopyTo(rgI, 1); return rgI; }//call rel32
            case 29: return new byte[] { 0x48, 0x0F, 0xB6, (byte)(0x40 + rng.Next(8)), (byte)rng.Next(256) };//movzx rX,byte [rAX+off]
            case 30: return new byte[] { 0x48, 0x0F, 0xBF, (byte)(0x40 + rng.Next(8)), (byte)rng.Next(256) };//movsx rX,word [rAX+off]
            case 31: { var rgI = new byte[8]; rgI[0] = 0x48; rgI[1] = 0x69; rgI[2] = (byte)(0xC0 + rng.Next(8)); rgI[3] = (byte)(0xC0 + rng.Next(8)); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 4); return rgI; }//imul rX,rY,imm32
            case 32: { var rgI = new byte[4]; rgI[0] = 0x48; rgI[1] = 0x89; rgI[2] = (byte)(0x40 + rng.Next(8)); rgI[3] = (byte)rng.Next(256); return rgI; }//mov [rAX+off],rX
            case 33: { var rgI = new byte[8]; rgI[0] = 0x48; rgI[1] = 0xC7; rgI[2] = (byte)(0x40 + rng.Next(8)); rgI[3] = (byte)rng.Next(256); BitConverter.GetBytes(rng.Next()).CopyTo(rgI, 4); return rgI; }//mov qword [rAX+off],imm32
            case 34: return new byte[] { 0x48, 0xC1, (byte)(0xC8 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//ror rX,imm8
            case 35: return new byte[] { 0x48, 0xC1, (byte)(0xC0 + rng.Next(8)), (byte)(1 + rng.Next(63)) };//rol rX,imm8
            case 36: return new byte[] { 0x48, 0x8D, (byte)(0x80 + rng.Next(8)), 0, 0, 0, 0 };//lea rX,[rAX+0]
            case 37: return new byte[] { 0x48, 0x3B, (byte)(0xC0 + rng.Next(8)) };//cmp rAX,rX
            case 38: return new byte[] { 0x48, 0x23, (byte)(0xC0 + rng.Next(8)) };//and rAX,rX
            case 39: return new byte[] { 0x48, 0x33, (byte)(0xC0 + rng.Next(8)) };//xor rAX,rX

            default: return new byte[] { 0xC3 };//ret
        }
    }

    private static byte[] DcryptInsn(Random rng)
    {
        switch (rng.Next(8))
        {
            case 0: return new byte[] { 0x05, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//add eax,imm32
            case 1: return new byte[] { 0x2D, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//sub eax,imm32
            case 2: return new byte[] { 0x35, (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256) };//xor eax,imm32
            case 3: return new byte[] { 0xC1, 0xC0, (byte)(1 + rng.Next(31)) };//rol eax,imm8
            case 4: return new byte[] { 0xC1, 0xC8, (byte)(1 + rng.Next(31)) };//ror eax,imm8
            case 5: return new byte[] { 0x0F, 0xC8 };//bswap eax
            case 6: return new byte[] { 0xF7, 0xD0 };//not eax
            default: return new byte[] { 0xF7, 0xD8 };//neg eax
        }
    }

    private static byte[] PpcWord(uint u) => new byte[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u };

    //扰动所用的寄存器选取表 有传统与精简两种形态
    static int MsvcReg(int ri) => ri < 4 ? new[] { 3, 5, 6, 7 }[ri] : 12 + (ri - 4);
    static int PpcReg(Random rng)
    {
        int i = rng.Next(24);
        if (i < 8) return new[] { 0, 3, 4, 5, 6, 11, 12, 31 }[rng.Next(8)];
        if (i < 16) return 7 + rng.Next(5);
        if (i < 20) return 13 + rng.Next(6);
        return 20 + rng.Next(12);
    }

    private static byte[] IlToPpc(Random rng, byte bOp)
    {
        int iRD = PpcReg(rng), iRA = PpcReg(rng), iRB = PpcReg(rng);
        uint u;
        switch (bOp)
        {
            case 0x00: return PpcWord(24u << 26);                               //nop = ori r0,r0,0
            case 0x02: case 0x03: case 0x04: case 0x05:                         //ldarg.N lwz rD, 8+4N
                return PpcWord((32u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(8 + 4 * (bOp - 0x02)));
            case 0x06: case 0x07: case 0x08: case 0x09:                         //ldloc.N lwz rD,-(8+4N)
                return PpcWord((32u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x06)) & 0xFFFF));
            case 0x0A: case 0x0B: case 0x0C: case 0x0D:                         //stloc.N stw rD,-(8+4N)
                return PpcWord((36u << 26) | ((uint)iRD << 21) | (1u << 16) | (uint)(-(8 + 4 * (bOp - 0x0A)) & 0xFFFF));
            case 0x16: case 0x17: case 0x18: case 0x19: case 0x1A: case 0x1B: case 0x1C: case 0x1D: case 0x1E://ldc.i4.N li rD,N
                return PpcWord((14u << 26) | ((uint)iRD << 21) | (uint)(bOp - 0x16));
            case 0x28: return PpcWord((18u << 26) | 1u);                        //callbl+0
            case 0x58: u = 266u; break;                                         //add
            case 0x59: u = 40u; break;                                          //sub subf
            case 0x5A: u = 235u; break;                                         //mul mullw
            case 0x5B: case 0x5D: u = 491u; break;                              //div/rem divw
            case 0x5F: u = 28u; break;                                          //and
            case 0x60: u = 444u; break;                                         //or
            case 0x61: u = 316u; break;                                         //xor
            case 0x62: u = 24u; break;                                          //shl slw
            case 0x63: u = 536u; break;                                         //shr srw
            case 0x65: return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRA << 16) | (104u << 1));//neg rD,rA
            case 0x66: return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRB << 16) | ((uint)iRB << 11) | (124u << 1));//not nor rD,rB,rB
            case 0x2A: return PpcWord((19u << 26) | (20u << 21) | (16u << 1));  //ret blr
            case 0x2C: return PpcConcat(PpcWord((11u << 26) | ((uint)iRA << 16)), PpcWord((16u << 26) | (12u << 21) | (2u << 16)));//brfalse cmpwi rA,0 beq +0
            case 0x2D: return PpcConcat(PpcWord((11u << 26) | ((uint)iRA << 16)), PpcWord((16u << 26) | (4u << 21) | (2u << 16)));//brtrue cmpwi rA,0 bne +0
            case 0x70: return PpcRound(rng, 0);
            case 0x71: return PpcRound(rng, 1);
            case 0x72: return PpcRound(rng, 2);
            case 0x73: return PpcTbl(rng);
            case 0x74: return PpcByteSwap(rng);
            case 0x75: return PpcBarrier(rng);
            case 0x76: return PpcConcat(PpcWord((31u << 26) | (8u << 16) | (339u << 1)), PpcWord((31u << 26) | (8u << 16) | (467u << 1)));
            case 0x77: return PpcConcat(PpcWord((31u << 26) | ((uint)PpcReg(rng) << 21) | (854u << 1)), PpcWord((19u << 26) | (150u << 1)));

            default: return PpcWord(24u << 26);
        }
        return PpcWord((31u << 26) | ((uint)iRD << 21) | ((uint)iRA << 16) | ((uint)iRB << 11) | (u << 1));//算术通用
    }

    private static byte[] PpcConcat(byte[] rgA, byte[] rgB)
    {
        byte[] rgR = new byte[rgA.Length + rgB.Length];
        Buffer.BlockCopy(rgA, 0, rgR, 0, rgA.Length);
        Buffer.BlockCopy(rgB, 0, rgR, rgA.Length, rgB.Length);
        return rgR;
    }

    private static byte[] PpcConcat(byte[] rgA, byte[] rgB, byte[] rgC)
    {
        byte[] rgR = new byte[rgA.Length + rgB.Length + rgC.Length];
        Buffer.BlockCopy(rgA, 0, rgR, 0, rgA.Length);
        Buffer.BlockCopy(rgB, 0, rgR, rgA.Length, rgB.Length);
        Buffer.BlockCopy(rgC, 0, rgR, rgA.Length + rgB.Length, rgC.Length);
        return rgR;
    }

    //变体零用异或与旋转与带进位加 作解密轮
    private static byte[] PpcRound(Random rng, int iV)
    {
        int rD = PpcReg(rng), rK = 12 + rng.Next(4);
        if (iV == 0) return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | ((uint)rK << 11) | (316u << 1)),
            PpcWord((21u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (5u << 11) | (0u << 6) | (31u << 1)),
            PpcWord((12u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)));
        if (iV == 1) return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | ((uint)rK << 11) | (60u << 1)),
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (3u << 11) | (24u << 1)),
            PpcWord((8u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)));
        return PpcConcat(
            PpcWord((12u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)),
            PpcWord((15u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (uint)rng.Next(0, 0x10000)),
            PpcWord((20u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (8u << 11) | (8u << 6) | (31u << 1)));
    }

    private static byte[] PpcTbl(Random rng)
    {
        int rD = PpcReg(rng), rA = PpcReg(rng), rB = PpcReg(rng);
        return PpcConcat(
            PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | ((uint)rB << 11) | (87u << 1)),
            PpcWord((20u << 26) | ((uint)rD << 21) | ((uint)rD << 16) | (8u << 11) | (16u << 6) | (23u << 1)));
    }

    private static byte[] PpcByteSwap(Random rng)
    {
        int rD = PpcReg(rng), rA = PpcReg(rng), rB = PpcReg(rng);
        return PpcWord((31u << 26) | ((uint)rD << 21) | ((uint)rA << 16) | ((uint)rB << 11) | (790u << 1));
    }

    private static byte[] PpcBarrier(Random rng) => PpcConcat(PpcWord((31u << 26) | (854u << 1)), PpcWord((19u << 26) | (150u << 1)));

private static byte[] BuildVmLure(Random rng)
    {
        var rngV = new Random(rng.Next());//独立rng种子 使两个VM壳不同
        List<byte> rgIl = new();
        List<byte> rgPpc = new();
        int iFrame = 32 + 32 * rngV.Next(16);
        int iSave = 8 + 8 * rngV.Next(4);
        //IL操作码重映射 每实例不同
        byte[] rgP1 = new byte[0x80];
        for (int i = 0; i < 0x80; i++) rgP1[i] = (byte)i;
        for (int i = 0x7F; i > 0; i--) { int j = rngV.Next(i + 1); (rgP1[i], rgP1[j]) = (rgP1[j], rgP1[i]); }
        byte[] rgOps = { 0x00,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x16,0x17,0x18,0x19,0x1A,0x28,0x2A,0x2C,0x2D,0x65,0x66,0x70,0x71,0x72,0x73,0x74,0x75,0x76,0x77 };
        //序言
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (339u << 1)));
        rgPpc.AddRange(PpcWord((36u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));
        rgPpc.AddRange(PpcWord((37u << 26) | (1u << 21) | (1u << 16) | (uint)(-iFrame & 0xFFFF)));
        int iN = 12 + rngV.Next(24);
        for (int i = 0; i < iN; i++)
        {
            byte bSem = rgOps[rngV.Next(rgOps.Length)];
            rgIl.Add(rgP1[bSem]);
            rgPpc.AddRange(IlToPpc(rngV, bSem));
        }
        rgIl.Add(rgP1[0x2A]); rgPpc.AddRange(IlToPpc(rngV, 0x2A));
        //尾声
        rgPpc.AddRange(PpcWord((14u << 26) | (1u << 21) | (1u << 16) | (uint)iFrame));
        rgPpc.AddRange(PpcWord((32u << 26) | (1u << 16) | (uint)(iSave & 0xFFFF)));
        rgPpc.AddRange(PpcWord((31u << 26) | (8u << 16) | (467u << 1)));
        rgPpc.AddRange(PpcWord((19u << 26) | (20u << 21) | (16u << 1)));
        var rgHandlers = new List<byte[]>();
        int iNH = 6 + rngV.Next(6);
        for (int i = 0; i < iNH; i++)
        {
            using var h = new MemoryStream();
            int iC = 2 + rngV.Next(5);
            for (int j = 0; j < iC; j++) { byte[] rgInsn = DcryptInsn(rngV); h.Write(rgInsn, 0, rgInsn.Length); }
            h.Write(new byte[] { 0xC3 }, 0, 1);
            rgHandlers.Add(h.ToArray());
        }
        const ulong uBase = 0x180000000UL;
        int iTable = 47;
        int iHandlers = iTable + rgHandlers.Count * 8;
        int iIlIn = iHandlers + rgHandlers.Sum(x => x.Length) + 6;
        int iPpc = iIlIn + rgIl.Count;
        int iIlOut = iPpc + rgPpc.Count;
        byte[] rgDisp = new byte[]
        {
            0x48,0xBE,0,0,0,0,0,0,0,0,
            0x9C,
            0x41,0x57,
            0x41,0x56,
            0x8B,0x06,
            0x0F,0xC8,
            0x05,0,0,0,0,
            0x35,0,0,0,0,
            0xC1,0xC0,0x05,
            0x25,0xFF,0x00,0x00,0x00,
            0x48,0x8B,0x04,0xC5,0,0,0,0,
            0xFF,0xE0
        };
        Buffer.BlockCopy(BitConverter.GetBytes(uBase + (ulong)iPpc), 0, rgDisp, 2, 8);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rngV.Next()), 0, rgDisp, 20, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)rngV.Next()), 0, rgDisp, 25, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(uBase + (ulong)iTable)), 0, rgDisp, 41, 4);
        using var ms = new MemoryStream();
        ms.Write(rgDisp, 0, rgDisp.Length);
        for (int i = 0; i < rgHandlers.Count; i++)
        {
            int iH = iHandlers + rgHandlers.Take(i).Sum(x => x.Length);
            ms.Write(BitConverter.GetBytes((ulong)(uint)(uBase + (ulong)iH)), 0, 8);
        }
        foreach (var h in rgHandlers) ms.Write(h, 0, h.Length);
        ms.Write(new byte[] { 0x41,0x5E, 0x41,0x5F, 0x9D, 0xC3 }, 0, 6);
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        ms.Write(rgPpc.ToArray(), 0, rgPpc.Count);
        ms.Write(rgIl.ToArray(), 0, rgIl.Count);
        return ms.ToArray();
    }

    private static byte[] BuildFakePe(Random rng)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 }, 0, 16);
        ms.Write(new byte[0x20], 0, 0x20);
        byte[] rgDos = Encoding.ASCII.GetBytes("This program cannot be run in DOS mode.\r\n\r\n$");
        ms.Write(rgDos, 0, rgDos.Length);
        ms.Position = 0x3C;
        ms.Write(new byte[] { 0x80, 0x00, 0x00, 0x00 }, 0, 4);//e_lfanew PE
        ms.Position = 0x80;
        ms.Write(Encoding.ASCII.GetBytes("PE\0\0"), 0, 4);
        var bw = new BinaryWriter(ms, Encoding.ASCII, true);
        bw.Write((ushort)0x8664);//Machine x64
        bw.Write((ushort)2);     //NumberOfSections
        bw.Write(rng.Next());    //TimeDateStamp
        bw.Write(0u); bw.Write(0u);//PtrToSymbolTable / NumSymbols
        bw.Write((ushort)0xF0);  //SizeOfOptionalHeader
        bw.Write((ushort)0x2022);//Characteristics
        bw.Write((ushort)0x20B); //Magic PE32+
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0u);
        bw.Write(0x180000000UL); //ImageBase
        bw.Write(0x1000u); bw.Write(0x200u);
        bw.Write((ushort)6); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0);
        bw.Write(0x4000u);       //SizeOfImage
        bw.Write(0x400u);        //SizeOfHeaders
        bw.Write(0u);            //Checksum
        bw.Write((ushort)3);     //Subsystem console
        bw.Write((ushort)0);     //DllCharacteristics
        bw.Write(0x100000UL); bw.Write(0x1000UL); bw.Write(0x100000UL); bw.Write(0x1000UL);
        bw.Write(0u); bw.Write(0u);
        for (int i = 0; i < 16; i++) { bw.Write(0u); bw.Write(0u); }//数据目录
        bw.Write(Encoding.ASCII.GetBytes(".text\0\0\0")); bw.Write(0x1000u); bw.Write(0x1000u); bw.Write(0x400u); bw.Write(0x200u); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0x60000020u);
        bw.Write(Encoding.ASCII.GetBytes(".rdata\0\0")); bw.Write(0x2000u); bw.Write(0x200u); bw.Write(0x600u); bw.Write(0x600u); bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(0x40000040u);
        bw.Write(GenRandomX64(rng, 64, 256));//假代码段
        byte[] rgRes = ms.ToArray();
        //两字节交错从128起 镜像与DOS头明文保留
        for (int i = 0x80; i + 1 < rgRes.Length; i += 2)
        {
            byte byTmp = rgRes[i]; rgRes[i] = rgRes[i + 1]; rgRes[i + 1] = byTmp;
        }
        return rgRes;
    }
}
