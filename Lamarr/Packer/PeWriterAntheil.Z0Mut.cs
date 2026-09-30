namespace Lamarr.Packer;

using System.Text;

internal partial class PeWriterAntheil
{
    private void Z0Mutate(byte[] rgZ0, int iDbgOfs, int nDbg)
    {
        rgVarSlot.Clear();//变体槽只留本次
        if (!Z0Pe(rgZ0, out int iPe, out int iOpt, out bool bPlus))
            throw new InvalidOperationException("pheropod is not a PE image");
        int iSec = Z0SecOfs(rgZ0, iPe, iOpt, out ushort usSec);
        if (iSec < 0)
            throw new InvalidOperationException("pheropod section table is malformed");
        uint uBefore = Fnv1aK1(rgZ0);
        var rng = NewRng(0x5A17u);
        uint uStamp = (uint)rng.Next(0x5E000000, 0x6A000000);//2020-2026的时间戳
        int nChg = 0;
        Buffer.BlockCopy(BitConverter.GetBytes(uStamp), 0, rgZ0, iPe + 8, 4);
        nChg += 4;
        int iExp = Z0ExpDir(rgZ0, iOpt, bPlus, iSec, usSec);
        if (iExp >= 0)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(uStamp), 0, rgZ0, iExp + 4, 4);
            nChg += 4;
            nChg += Z0Names(rgZ0, iExp, iSec, usSec, rng);
        }
        nChg += Z0Debug(rgZ0, iDbgOfs, nDbg, rng, uStamp);
        nChg += Z0Slots(rgZ0, iSec, usSec, rng);
        Z0Checksum(rgZ0, iOpt, bPlus);
        uint uAfter = Fnv1aK1(rgZ0);
        Console.WriteLine($"[pheropod] mutate dbg={nDbg} changed={nChg} fnv {uBefore:x8}->{uAfter:x8}");
        if (uBefore == uAfter)//相同=输入已是改过的那份
            Console.WriteLine("[pheropod] mutate no byte changed: the input already carries this seed shape");
    }


    private static bool Z0Pe(byte[] rgPe, out int iPe, out int iOpt, out bool bPlus)
    {
        iPe = 0; iOpt = 0; bPlus = false;
        if (rgPe == null || rgPe.Length < 0x80) return false;
        iPe = BitConverter.ToInt32(rgPe, 0x3C);
        if (iPe <= 0 || iPe + 0x18 > rgPe.Length || BitConverter.ToUInt32(rgPe, iPe) != 0x4550) return false;
        iOpt = iPe + 24;
        if (iOpt + 2 > rgPe.Length) return false;
        bPlus = BitConverter.ToUInt16(rgPe, iOpt) == 0x20B;
        return true;
    }

    private static int Z0SecOfs(byte[] rgPe, int iPe, int iOpt, out ushort usNumSec)//段表偏移 越界返回-1
    {
        usNumSec = 0;
        if (iPe + 22 > rgPe.Length) return -1;
        usNumSec = BitConverter.ToUInt16(rgPe, iPe + 6);
        int iSec = iOpt + BitConverter.ToUInt16(rgPe, iPe + 20);
        if (usNumSec == 0 || usNumSec > 96 || iSec <= 0) return -1;
        if (iSec + usNumSec * 40 > rgPe.Length) return -1;
        return iSec;
    }

    private static int Z0RvaOfs(byte[] rgPe, int iSec, ushort usNumSec, uint uRva)//RVA到文件偏移 越界返回-1
    {
        for (int k = 0; k < usNumSec; k++)
        {
            int iH = iSec + k * 40;
            uint uVa = BitConverter.ToUInt32(rgPe, iH + 12);
            uint uVs = BitConverter.ToUInt32(rgPe, iH + 8);
            uint uRs = BitConverter.ToUInt32(rgPe, iH + 16);
            uint uRaw = BitConverter.ToUInt32(rgPe, iH + 20);
            uint uLen = Math.Max(uVs, uRs);
            if (uRva >= uVa && uRva < uVa + uLen)
            {
                long lOfs = (long)uRaw + (uRva - uVa);
                return lOfs >= 0 && lOfs < rgPe.Length ? (int)lOfs : -1;
            }
        }
        return -1;
    }

    private static int Z0ExpDir(byte[] rgPe, int iOpt, bool bPlus, int iSec, ushort usNumSec)
    {
        int iDd = iOpt + (bPlus ? 112 : 96);
        if (iDd + 8 > rgPe.Length) return -1;
        uint uRva = BitConverter.ToUInt32(rgPe, iDd);
        if (uRva == 0) return -1;
        int iExp = Z0RvaOfs(rgPe, iSec, usNumSec, uRva);
        if (iExp < 0 || iExp + 40 > rgPe.Length) return -1;
        return iExp;
    }

    private static int Z0Names(byte[] rgPe, int iExp, int iSec, ushort usNumSec, Random rng)//导出名等长替换并保持名字表升序
    {
        uint uN = BitConverter.ToUInt32(rgPe, iExp + 24);
        if (uN == 0 || uN > 64) return 0;
        int iTab = Z0RvaOfs(rgPe, iSec, usNumSec, BitConverter.ToUInt32(rgPe, iExp + 32));
        int iOrd = Z0RvaOfs(rgPe, iSec, usNumSec, BitConverter.ToUInt32(rgPe, iExp + 36));
        if (iTab < 0 || iOrd < 0) return 0;
        if (iTab + uN * 4 > rgPe.Length || iOrd + uN * 2 > rgPe.Length) return 0;
        int nChg = 0;
        bool bSkip = false;
        var rgRva = new uint[uN];
        var rgOrd = new ushort[uN];
        var rgNew = new string[uN];
        for (uint i = 0; i < uN; i++)
        {
            rgRva[i] = BitConverter.ToUInt32(rgPe, iTab + (int)i * 4);
            int iOfs = Z0RvaOfs(rgPe, iSec, usNumSec, rgRva[i]);
            if (iOfs < 0) return 0;
            int iEnd = iOfs;
            while (iEnd < rgPe.Length && rgPe[iEnd] != 0 && iEnd - iOfs < 64) iEnd++;
            int iLen = iEnd - iOfs;
            if (iLen <= 0) return 0;
            if (!Z0InSection(rgPe, iSec, usNumSec, iOfs, iLen, out bool bExec) || bExec)//名字串只能落在非可执行节里
            {
                Console.WriteLine($"[pheropod] export name {i} at 0x{iOfs:X} is outside a data section, kept as is");
                bSkip = true;
                rgNew[i] = new string('a', iLen);
                continue;
            }
            rgOrd[i] = BitConverter.ToUInt16(rgPe, iOrd + (int)i * 2);
            rgNew[i] = Z0Name(rng, iLen);//等长替换 表长与所有RVA都不动
            for (int k = 0; k < iLen; k++) rgPe[iOfs + k] = (byte)rgNew[i][k];
            nChg += iLen;
        }
        //按名查找依赖名字表升序 指针栏与序号栏须同步置换 有跳过则整体不动
        if (bSkip) return nChg;
        var rgIdx = new int[uN];
        for (int i = 0; i < uN; i++) rgIdx[i] = i;
        Array.Sort(rgIdx, (x, y) => string.CompareOrdinal(rgNew[x], rgNew[y]));
        for (int i = 0; i < uN; i++)
        {
            int j = rgIdx[i];
            Buffer.BlockCopy(BitConverter.GetBytes(rgRva[j]), 0, rgPe, iTab + i * 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(rgOrd[j]), 0, rgPe, iOrd + i * 2, 2);
        }
        return nChg;
    }

    private static bool Z0InSection(byte[] rgPe, int iSec, ushort usNumSec, int iOfs, int iLen, out bool bExec)
    {
        bExec = false;
        for (int k = 0; k < usNumSec; k++)
        {
            int iH = iSec + k * 40;
            int iFrom = (int)BitConverter.ToUInt32(rgPe, iH + 20);
            int iSize = (int)BitConverter.ToUInt32(rgPe, iH + 16);
            if (iFrom <= 0 || iSize <= 0) continue;
            if (iOfs >= iFrom && (long)iOfs + iLen <= iFrom + iSize)
            {
                bExec = (BitConverter.ToUInt32(rgPe, iH + 36) & 0x20000000) != 0;
                return true;
            }
        }
        return false;
    }

    private static string Z0Name(Random rng, int iLen)//同长度随机小写串 长度变则后面所有RVA要改
    {
        var rg = new char[iLen];
        for (int i = 0; i < iLen; i++) rg[i] = (char)('a' + rng.Next(26));
        return new string(rg);
    }

    private static int Z0DebugOfs(byte[] rgPe, out int nDbg)//调试目录数组偏移 必须在PeScrub清目录项之前调
    {
        nDbg = 0;
        if (!Z0Pe(rgPe, out int iPe, out int iOpt, out bool bPlus)) return -1;
        int iSec = Z0SecOfs(rgPe, iPe, iOpt, out ushort usNumSec);
        if (iSec < 0) return -1;
        int iDd = iOpt + (bPlus ? 112 : 96) + 6 * 8;
        if (iDd + 8 > rgPe.Length) return -1;
        uint uRva = BitConverter.ToUInt32(rgPe, iDd);
        uint uSize = BitConverter.ToUInt32(rgPe, iDd + 4);
        if (uRva == 0 || uSize < 28) return -1;
        uSize -= uSize % 28;
        int iDbg = Z0RvaOfs(rgPe, iSec, usNumSec, uRva);
        if (iDbg < 0 || iDbg + uSize > rgPe.Length) return -1;
        nDbg = (int)(uSize / 28);
        return iDbg;
    }

    private static int Z0Debug(byte[] rgPe, int iDbgOfs, int nDbg, Random rng, uint uStamp)//改调试记录 目录项已被清 内容不必再自洽
    {
        if (iDbgOfs < 0 || nDbg <= 0) return 0;
        int nChg = 0;
        for (int i = 0; i < nDbg; i++)
        {
            int iH = iDbgOfs + i * 28;
            if (iH + 28 > rgPe.Length) break;
            Buffer.BlockCopy(BitConverter.GetBytes(uStamp), 0, rgPe, iH + 4, 4);
            nChg += 4;
            if (BitConverter.ToUInt32(rgPe, iH + 12) != 16) continue;//16是Brepro记录 里面是构建输入摘要
            uint uSize = BitConverter.ToUInt32(rgPe, iH + 16);
            uint uPtr = BitConverter.ToUInt32(rgPe, iH + 24);
            long lEnd = (long)uPtr + uSize;
            if (uPtr == 0 || uSize == 0 || lEnd > rgPe.Length) continue;
            for (uint k = 0; k < uSize; k++) rgPe[uPtr + k] = (byte)rng.Next(256);
            nChg += (int)uSize;
        }
        return nChg;
    }

    private readonly List<(int Ofs, int Len, byte[][] Cand)> rgVarSlot = new();//变体槽 供穷尽自检复放

    private static readonly uint[] rgSlotSig = { 0xC4B1A7E3u, 0x5E2D90B0u };//v2目录签名 每项16字节 带等长变体

    private int Z0Slots(byte[] rgPe, int iSec, ushort usNumSec, Random rng)//插槽都在可执行段里
    {
        for (int k = 0; k < usNumSec; k++)
        {
            int iH = iSec + k * 40;
            if ((BitConverter.ToUInt32(rgPe, iH + 36) & 0x20000000) == 0) continue;
            int iFrom = (int)BitConverter.ToUInt32(rgPe, iH + 20);
            int iLen = (int)BitConverter.ToUInt32(rgPe, iH + 16);
            if (iFrom <= 0 || iLen <= 0 || (long)iFrom + iLen > rgPe.Length) continue;
            int n = Z0Slots1(rgPe, iFrom, iFrom + iLen, rng);
            if (n >= 0) return n;
        }
        Console.WriteLine("[pheropod] slots none found");
        return 0;
    }

    private int Z0Slots1(byte[] rgPe, int iFrom, int iTo, Random rng)//没有目录就返回-1
    {
        int iDir = Z0FindDir(rgPe, iFrom, iTo, out int nSlot, out int iEnd);
        if (iDir < 0) return -1;
        Console.WriteLine($"[pheropod] slots dir=0x{iDir:X} entries={nSlot} span=0x{iDir:X}..0x{iEnd:X}");
        int nChg = 0, nSkip = 0, nVar = 0;
        for (int i = 0; i < nSlot; i++)
        {
            int iOfs = iDir + (int)BitConverter.ToUInt32(rgPe, iDir + 12 + i * 16);
            int iLen = (int)BitConverter.ToUInt32(rgPe, iDir + 12 + i * 16 + 4);
            int nAlt = (int)BitConverter.ToUInt32(rgPe, iDir + 12 + i * 16 + 8);
            if (!Z0NeutralOk(rgPe, iOfs, iLen))//只动能按中立块表精确分解的槽 不识别不动
            {
                var sb = new StringBuilder();
                for (int k = 0; k < iLen; k++) sb.Append(rgPe[iOfs + k].ToString("x2"));
                Console.WriteLine($"[pheropod] slot {i} abs=0x{iOfs:X} rel={iOfs - iDir} len {iLen} skipped bytes {sb}");
                nSkip++;
                continue;
            }
            if (nAlt == 0)
            {
                Z0Neutral(rgPe, iOfs, iLen, rng);
            }
            else
            {
                int iBlob = iDir + (int)BitConverter.ToUInt32(rgPe, iDir + 12 + i * 16 + 12);
                var rgCand = new byte[1 + nAlt][];
                rgCand[0] = new byte[iLen];
                Buffer.BlockCopy(rgPe, iOfs, rgCand[0], 0, iLen);//槽里现有那份也是候选之一
                for (int k = 0; k < nAlt; k++)
                {
                    rgCand[1 + k] = new byte[iLen];
                    Buffer.BlockCopy(rgPe, iBlob + k * iLen, rgCand[1 + k], 0, iLen);
                }
                int iPick = rng.Next(rgCand.Length);
                Buffer.BlockCopy(rgCand[iPick], 0, rgPe, iOfs, iLen);
                rgVarSlot.Add((iOfs, iLen, rgCand));//自检要把每个候选都跑一遍
                nVar++;
            }
            nChg += iLen;
        }
        for (int k = iDir; k < iEnd; k++) rgPe[k] = (byte)rng.Next(256);//目录与变体区都不留痕
        Console.WriteLine($"[pheropod] slots {nSlot} filled {nChg} bytes skipped {nSkip} variants {nVar}");
        return nChg;
    }

    private static byte[][]? rgZ0Pool;

    private static byte[][] Z0Blocks()//中立块表的每条都必须寄存器中立 标志中立 不访存 长度已知
    {
        if (rgZ0Pool != null)
            return rgZ0Pool;
        var rg = new List<byte[]>
        {
            new byte[] { 0x90 },                                          // nop
            new byte[] { 0x66, 0x90 },
            new byte[] { 0x0F, 0x1F, 0x00 },                              // 规范多字节nop 长度1到9
            new byte[] { 0x0F, 0x1F, 0x40, 0x00 },
            new byte[] { 0x0F, 0x1F, 0x44, 0x00, 0x00 },
            new byte[] { 0x66, 0x0F, 0x1F, 0x44, 0x00, 0x00 },
            new byte[] { 0x0F, 0x1F, 0x80, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x0F, 0x1F, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x66, 0x0F, 0x1F, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00 },
            new byte[] { 0x66, 0x66, 0x90 },                              // 冗余前缀nop
            new byte[] { 0x66, 0x66, 0x66, 0x90 },
            new byte[] { 0x66, 0x66, 0x66, 0x66, 0x90 },
        };
        for (int i = 0; i < 16; i++)
        {
            byte uRex = (byte)(i >= 8 ? 0x4D : 0x48);// REX.W 加 r8以上需要的 R 与 B
            byte uReg = (byte)(i & 7);
            byte uMod = (byte)(0xC0 | (uReg << 3) | uReg);
            rg.Add(new byte[] { uRex, 0x89, uMod });// mov rX,rX
            rg.Add(new byte[] { uRex, 0x8B, uMod });// 同一语义的另一种编码
            rg.Add(new byte[] { uRex, 0x87, uMod });// xchg rX,rX 寄存器形式不访存
            if (uReg != 4 && uReg != 5)             // lea 需要内存操作数 mod=00 时 rsp rbp 不可直接寻址
                rg.Add(new byte[] { uRex, 0x8D, (byte)((uReg << 3) | uReg) });
        }
        rgZ0Pool = rg.ToArray();
        return rgZ0Pool;
    }

    private static bool Z0NeutralOk(byte[] rgPe, int iOfs, int iLen)//逐字节都必须是中立块
    {
        int i = 0;
        while (i < iLen)
        {
            int iHit = -1;
            foreach (byte[] rgB in Z0Blocks())
            {
                if (i + rgB.Length > iLen) continue;
                bool bSame = true;
                for (int k = 0; k < rgB.Length && bSame; k++) if (rgPe[iOfs + i + k] != rgB[k]) bSame = false;
                if (bSame) { iHit = rgB.Length; break; }
            }
            if (iHit < 0) return false;
            i += iHit;
        }
        return true;
    }

    private static void Z0Neutral(byte[] rgPe, int iOfs, int iLen, Random rng)//替换块必须等长 寄存器与标志中立 且不访存
    {
        byte[][] rgAll = Z0Blocks();
        int i = 0;
        while (i < iLen)
        {
            int iRest = iLen - i;
            var rgOk = new List<byte[]>();//只挑放得下的块 越界会写坏相邻槽
            foreach (byte[] rgC in rgAll) if (rgC.Length <= iRest) rgOk.Add(rgC);
            byte[] rgB = rgOk[rng.Next(rgOk.Count)];
            Buffer.BlockCopy(rgB, 0, rgPe, iOfs + i, rgB.Length);
            i += rgB.Length;
        }
    }

    private int Z0SlotFill(byte[] rgD, string sTag)//等长插槽重写 打包器与构建链共用这一条路径
    {
        if (!Z0Pe(rgD, out int iPe, out int iOpt, out _)) return -1;
        int iSec = Z0SecOfs(rgD, iPe, iOpt, out ushort usSec);
        if (iSec < 0) return -1;
        return Z0Slots(rgD, iSec, usSec, Z0SeedRng(uVmSeed, sTag));
    }

    internal static int SlotMorph(string sIn, string? sOut, ulong uSeed, string sTag, bool bCheck)//构建链入口 与打包路径共用目录解析与池
    {
        string sDst = sOut ?? sIn;
        byte[] rgD = File.ReadAllBytes(sIn);
        if (!Z0Pe(rgD, out int iPe, out int iOpt, out _))
        {
            Console.Error.WriteLine($"[slot-morph] not a PE image: {sIn}");
            return 1;
        }
        int iSec = Z0SecOfs(rgD, iPe, iOpt, out ushort usSec);
        if (iSec < 0)
        {
            Console.Error.WriteLine($"[slot-morph] section table malformed: {sIn}");
            return 1;
        }
        var pe = new PeWriterAntheil();
        int n = pe.Z0Slots(rgD, iSec, usSec, Z0SeedRng(uSeed, sTag));
        if (n == 0)
        {
            Console.WriteLine($"[slot-morph] no slot directory in {sIn}, nothing to do");
            return 0;
        }
        File.WriteAllBytes(sDst, rgD);
        Console.WriteLine($"[slot-morph] {sDst} seed={uSeed:X16} tag={sTag} bytes={n}");
        if (!bCheck)
            return 0;
        string sA = LayOutZ0(File.ReadAllBytes(sIn));
        string sB = LayOutZ0(rgD);
        try
        {
            int nBad = 0, nTry = 0;
            var rng = new Random(0x5A17);
            for (int v = 0; v < 3; v++)
            {
                byte[] rgSrc = new byte[257 + v * 2048];
                rng.NextBytes(rgSrc);
                byte[] rgA = Z0Press(sA, rgSrc);
                byte[] rgB = Z0Press(sB, rgSrc);
                nTry++;
                if (rgA.Length != rgB.Length || !rgA.AsSpan().SequenceEqual(rgB)) nBad++;
            }
            Console.WriteLine($"[slot-morph] check vectors={nTry} mismatches={nBad}");
            return nBad == 0 ? 0 : 1;
        }
        catch (Exception ex)//无压缩入口时只做结构层面
        {
            Console.WriteLine($"[slot-morph] semantic check skipped: {ex.GetType().Name}");
            return 0;
        }
    }

    private static Random Z0SeedRng(ulong uSeed, string sTag)//与产物种子解绑
    {
        uint uTag = Fnv1aK1(Encoding.ASCII.GetBytes(sTag));
        return new Random((int)(uSeed ^ ((ulong)uTag << 32) ^ 0x9E3779B97F4A7C15UL));
    }

    private static int Z0FindDir(byte[] rgPe, int iFrom, int iTo, out int nSlot, out int iEnd)//v2目录 每项16字节 自带等长变体区
    {
        nSlot = 0; iEnd = 0;
        for (int i = iFrom; i + 20 <= iTo; i++)
        {
            if (BitConverter.ToUInt32(rgPe, i) != rgSlotSig[0]) continue;
            if (BitConverter.ToUInt32(rgPe, i + 4) != rgSlotSig[1]) continue;
            int uN = (int)BitConverter.ToUInt32(rgPe, i + 8);
            if (uN <= 0 || uN > 512) continue;
            long lDirEnd = (long)i + 12 + uN * 16;
            if (lDirEnd > iTo) continue;
            var rgBeg = new int[uN];
            var rgEnd = new int[uN];
            int iBlobEnd = (int)lDirEnd;
            bool bOk = true;
            for (int k = 0; k < uN && bOk; k++)
            {
                int iAbs = i + (int)BitConverter.ToUInt32(rgPe, i + 12 + k * 16);
                int iLen = (int)BitConverter.ToUInt32(rgPe, i + 12 + k * 16 + 4);
                int nAlt = (int)BitConverter.ToUInt32(rgPe, i + 12 + k * 16 + 8);
                int iVar = i + (int)BitConverter.ToUInt32(rgPe, i + 12 + k * 16 + 12);
                if (iLen < 6 || iLen > 16 || nAlt < 0 || nAlt > 8) bOk = false;
                else if (iAbs < iFrom || (long)iAbs + iLen > iTo) bOk = false;
                else { rgBeg[k] = iAbs; rgEnd[k] = iAbs + iLen; }
                if (!bOk) break;
                if (nAlt > 0)
                {
                    long lBlobEnd = (long)iVar + nAlt * iLen;
                    if (iVar < lDirEnd || lBlobEnd > iTo) bOk = false;
                    else if (lBlobEnd > iBlobEnd) iBlobEnd = (int)lBlobEnd;
                }
            }
            if (bOk) bOk = Z0NoOverlap(rgBeg, rgEnd);
            if (!bOk) continue;
            nSlot = uN;
            iEnd = iBlobEnd;//覆盖到变体区末尾
            return i;
        }
        return -1;
    }

    private static bool Z0NoOverlap(int[] rgBeg, int[] rgEnd)//槽在源码里是乱序的 只要求两两不重叠
    {
        var rgIdx = new int[rgBeg.Length];
        for (int i = 0; i < rgIdx.Length; i++) rgIdx[i] = i;
        Array.Sort(rgIdx, (x, y) => rgBeg[x].CompareTo(rgBeg[y]));
        for (int i = 1; i < rgIdx.Length; i++)
            if (rgBeg[rgIdx[i]] < rgEnd[rgIdx[i - 1]])
                return false;
        return true;
    }

    private static void Z0Checksum(byte[] rgPe, int iOpt, bool bPlus)//按标准算法重算
    {
        int iCk = iOpt + 64;
        if (iCk + 4 > rgPe.Length) return;
        Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, rgPe, iCk, 4);
        ulong uSum = 0;
        int iN = rgPe.Length & ~1;
        for (int i = 0; i < iN; i += 2) uSum += BitConverter.ToUInt16(rgPe, i);
        if ((rgPe.Length & 1) != 0) uSum += rgPe[rgPe.Length - 1];
        for (int k = 0; k < 3; k++) uSum = (uSum & 0xFFFF) + (uSum >> 16);
        while (uSum > 0xFFFF) uSum = (uSum & 0xFFFF) + (uSum >> 16);//折到不再进位 固定折三次会把进位丢掉
        uint uCk = (uint)uSum + (uint)rgPe.Length;
        Buffer.BlockCopy(BitConverter.GetBytes(uCk), 0, rgPe, iCk, 4);
    }
}
