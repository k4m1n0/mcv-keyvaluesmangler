using Lamarr;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.NativePack;

internal partial class PeWriterAntheil
{

    private static int RvaToOffset(byte[] rgD, int iPe, ushort usOptSize, ushort usNumSec, uint uRva)
    {
        int iSecStart = iPe + 24 + usOptSize;
        for (int i = 0; i < usNumSec; i++)
        {
            int iS = iSecStart + i * 40;
            uint uVs = BitConverter.ToUInt32(rgD, iS + 8);
            uint uVa = BitConverter.ToUInt32(rgD, iS + 12);
            uint uRs = BitConverter.ToUInt32(rgD, iS + 16);
            uint uPo = BitConverter.ToUInt32(rgD, iS + 20);
            uint uEnd = Math.Max(uVs, uRs);
            if (uRva >= uVa && uRva < uVa + uEnd) return (int)(uPo + (uRva - uVa));
        }
        return -1;
    }

    #region IL扰动

    private sealed class MReloc
    {
        public int Row, iRva, iHdr, iCs;
        public byte[] rgBody;
        public bool bGrow;
        public bool bPlain;
        public string sName;
    }

    //方法区平移注入 目标方法体内插恒假分支 后续方法起点平移 md6表与metadata同步
    private static byte[] MoveInjectIl(byte[] rgB, Random rng)
    {
        int iPe = BitConverter.ToInt32(rgB, 0x3C);
        if (iPe + 0x18 > rgB.Length || BitConverter.ToUInt32(rgB, iPe) != 0x4550) return rgB;
        ushort usMagic = BitConverter.ToUInt16(rgB, iPe + 24);
        ushort usNum = BitConverter.ToUInt16(rgB, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgB, iPe + 20);
        int iOpt = iPe + 24;
        int iSec = iOpt + usOpt;
        int iTva = 0, iTroff = 0, iTvs = 0;
        for (int i = 0; i < usNum; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rgB.Length) break;
            if (Encoding.ASCII.GetString(rgB, o, 8).TrimEnd('\0') == ".text")
            {
                iTva = BitConverter.ToInt32(rgB, o + 12);
                iTvs = BitConverter.ToInt32(rgB, o + 8);
                iTroff = BitConverter.ToInt32(rgB, o + 20);
                break;
            }
        }
        if (iTva == 0 || iTroff == 0) return rgB;
        string sOnly = Environment.GetEnvironmentVariable("LAMARR_MOVE_ONLY") ?? "";
        var rgPlain = new HashSet<string> { "DeriveSeed", "RestoreData", "GenDisturb", "CheckElapsed", "SelfCheck" };
        var rgAll = new List<MReloc>();
        using (var ms = new MemoryStream(rgB, writable: false))
        using (var per = new PEReader(ms))
        {
            var mr = per.GetMetadataReader();
            foreach (var mdh in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(mdh);
                int iRva = md.RelativeVirtualAddress;
                if (iRva == 0) continue;
                string sName = mr.GetString(md.Name);
                int iOff = iTroff + (iRva - iTva);
                if (iOff < 0 || iOff + 12 > rgB.Length) continue;
                byte b0 = rgB[iOff];
                int iHdr, iCs;
                if ((b0 & 3) == 2) { iHdr = 1; iCs = b0 >> 2; }
                else if ((b0 & 3) == 3) { iHdr = 12; iCs = BitConverter.ToInt32(rgB, iOff + 4); }
                else continue;
                if (iCs <= 0 || iCs > 0xFFFF) continue;
                int iRow = MetadataTokens.GetRowNumber(mr, mdh);
                bool bHasEh = false;
                try { bHasEh = per.GetMethodBody(iRva).ExceptionRegions.Any(); }
                catch { }
                var m = new MReloc { Row = iRow, iRva = iRva, iHdr = iHdr, iCs = iCs, sName = sName };
                m.bPlain = !bHasEh && m.iHdr == 12 &&
                    (sOnly.Length > 0 ? sName == sOnly : rgPlain.Contains(sName));
                rgAll.Add(m);
            }
        }
        if (rgAll.Count == 0) return rgB;
        var rgS = rgAll.OrderBy(m => m.iRva).ToList();

        int iCli = iOpt + (usMagic == 0x20B ? 112 : 96) + 14 * 8;
        int iMd = -1, iCliOff = -1;
        if (iCli + 16 <= rgB.Length)
        {
            uint uCliRva = BitConverter.ToUInt32(rgB, iCli);
            iCliOff = RvaToOffset(rgB, iPe, usOpt, usNum, uCliRva);
            if (iCliOff >= 0)
            {
                uint uMdRva = BitConverter.ToUInt32(rgB, iCliOff + 8);
                if (uMdRva != 0) iMd = RvaToOffset(rgB, iPe, usOpt, usNum, uMdRva);
            }
        }

        var rgOut = (byte[])rgB.Clone();
        var rgNewBody = new Dictionary<int, byte[]>();
        int iGrowth = 0;
        foreach (var m in rgS)
        {
            if (!m.bPlain || m.iHdr != 12) continue;
            int iOff = iTroff + (m.iRva - iTva);
            byte[] rgD;
            if (Environment.GetEnvironmentVariable("LAMARR_MOVE_NOPONLY") == "1")
            {
                //纯移动 末尾追加nop 数量由LAMARR_MOVE_NOP控制
                int iNop = int.TryParse(Environment.GetEnvironmentVariable("LAMARR_MOVE_NOP") ?? "", out int iV) ? iV : 6;
                rgD = new byte[m.iCs + iNop];
                Array.Copy(rgB, iOff + m.iHdr, rgD, 0, m.iCs);
            }
            else
                rgD = ObfuscateIl(rgB, iOff + m.iHdr, m.iCs, rng);
            if (rgD == null || rgD.Length <= m.iCs) continue;
            if (!IlSanity(rgD))
            {
                Console.WriteLine($"  il_noise: OBFUSCATE-FAIL {m.sName} len={rgD.Length}");
                continue;
            }

            rgNewBody[m.Row] = rgD;
            iGrowth += rgD.Length - m.iCs;
        }

        int iMetaShift = 8;
        if (iMd >= 0)
        {
            iMetaShift = (iGrowth + 7) & ~7;
            int iMetaSize = BitConverter.ToInt32(rgB, iCliOff + 12);
            if (iMetaSize <= 0 || iMd + iMetaSize + iMetaShift > iTroff + iTvs) return rgB;
            byte[] rgMeta = new byte[iMetaSize];
            Buffer.BlockCopy(rgOut, iMd, rgMeta, 0, iMetaSize);
            Buffer.BlockCopy(rgMeta, 0, rgOut, iMd + iMetaShift, iMetaSize);
            Array.Clear(rgOut, iMd, Math.Min(iMetaShift, iMetaSize));
            BitConverter.GetBytes(BitConverter.ToUInt32(rgB, iCliOff + 8) + (uint)iMetaShift).CopyTo(rgOut, iCliOff + 8);
        }

        int iFirstOff = iTroff + (rgS[0].iRva - iTva);
        int iAreaEnd = 0;
        foreach (var m in rgS)
            iAreaEnd = Math.Max(iAreaEnd, iTroff + (m.iRva - iTva) + m.iHdr + m.iCs);
        int iOldArea = iAreaEnd - iFirstOff;
        if (iOldArea <= 0) return rgB;
        byte[] rgNewArea = new byte[iOldArea + iGrowth];
        var cumShift = new int[rgS.Count];
        int csum = 0, cur = 0;
        for (int i = 0; i < rgS.Count; i++)
        {
            var m = rgS[i];
            cumShift[i] = csum;
            int iOff = iTroff + (m.iRva - iTva);

            if (rgNewBody.TryGetValue(m.Row, out var body))
            {
                byte[] hdr = new byte[m.iHdr];
                Array.Copy(rgB, iOff, hdr, 0, m.iHdr);

                if (m.iHdr >= 8) BitConverter.GetBytes(body.Length).CopyTo(hdr, 4);
                Array.Copy(hdr, 0, rgNewArea, cur, hdr.Length); cur += hdr.Length;
                Array.Copy(body, 0, rgNewArea, cur, body.Length); cur += body.Length;
                csum += body.Length - m.iCs;
            }
            else
            {
                Array.Copy(rgB, iOff, rgNewArea, cur, m.iHdr + m.iCs); cur += m.iHdr + m.iCs;
            }
            int iGapEnd = (i + 1 < rgS.Count) ? iTroff + (rgS[i + 1].iRva - iTva) : iAreaEnd;
            int gap = iGapEnd - (iOff + m.iHdr + m.iCs);

            if (gap > 0 && i + 1 < rgS.Count && cur + gap <= rgNewArea.Length)
            {
                Array.Copy(rgB, iOff + m.iHdr + m.iCs, rgNewArea, cur, gap);
                cur += gap;
            }
        }
        Array.Copy(rgNewArea, 0, rgOut, iFirstOff, rgNewArea.Length);


        if (iGrowth > 0 && iMd >= 0)
        {
            var rvaMap = new Dictionary<int, int>();

            for (int i = 0; i < rgS.Count; i++)
            {
                rvaMap[rgS[i].Row] = rgS[i].iRva + cumShift[i];

            }

            PatchMethodRvas(rgOut, iMd + iMetaShift, rvaMap);
        }

        Console.WriteLine($"  il_noise: {rgS.Count} methods, move_inject={rgNewBody.Count}, +{iGrowth} bytes");
        return rgOut;
    }

    //IL完整性 指令流连续 全部分支目标在指令边界
    private static bool IlSanity(byte[] rgIl)
    {
        var rgIns = new List<int>();
        int p = 0, n = rgIl.Length;
        while (p < n)
        {
            int l = IlLen(rgIl, p);
            if (l <= 0 || p + l > n) return false;
            rgIns.Add(p);
            p += l;
        }
        if (p != n) return false;
        p = 0;
        while (p < n)
        {
            int l = IlLen(rgIl, p);
            if (IsBranch(rgIl[p]))
            {
                int[] tg = BranchTargets(rgIl, p, l);
                if (tg == null) return false;
                foreach (int t in tg)
                    if (!rgIns.Contains(t)) return false;
            }
            p += l;
        }
        return true;
    }

    private static void PatchMethodRvas(byte[] rgD, int iMetaOff, Dictionary<int, int> rvaMap)
    {
        if (rvaMap == null || rvaMap.Count == 0 || iMetaOff < 0) return;
        if (iMetaOff + 24 > rgD.Length || BitConverter.ToUInt32(rgD, iMetaOff) != 0x424A5342) return;
        int iVerLen = BitConverter.ToInt32(rgD, iMetaOff + 12);
        int iStm = iMetaOff + 16 + iVerLen;
        if (iStm + 4 > rgD.Length) return;
        ushort usStreams = BitConverter.ToUInt16(rgD, iStm + 2);
        int iCur = iStm + 4;
        int iTables = -1;
        for (int i = 0; i < usStreams; i++)
        {
            if (iCur + 8 > rgD.Length) return;
            uint uOff = BitConverter.ToUInt32(rgD, iCur);
            int iNl = 0;
            while (iCur + 8 + iNl < rgD.Length && rgD[iCur + 8 + iNl] != 0) iNl++;
            string sName = Encoding.ASCII.GetString(rgD, iCur + 8, iNl);
            if (sName == "#~") iTables = (int)uOff;
            iCur += 8 + iNl + 1;
            iCur = (iCur + 3) & ~3;
        }
        if (iTables < 0) return;
        int iT = iMetaOff + iTables;
        if (iT + 24 > rgD.Length) return;
        ulong uValid = BitConverter.ToUInt64(rgD, iT + 8);
        if ((uValid & (1UL << 6)) == 0) return;
        int[] rowCount = new int[7], rowSize = new int[7];
        using (var ms = new MemoryStream(rgD, writable: false))
        using (var per = new PEReader(ms))
        {
            var mr = per.GetMetadataReader();
            for (int t = 0; t <= 6; t++)
            {
                if ((uValid & (1UL << t)) == 0) continue;
                rowCount[t] = mr.GetTableRowCount((TableIndex)t);
                rowSize[t] = mr.GetTableRowSize((TableIndex)t);
            }
        }
        int iRows = iT + 24;
        for (int t = 0; t < 64; t++)
            if ((uValid & (1UL << t)) != 0) iRows += 4;
        for (int t = 0; t < 6; t++)
            if ((uValid & (1UL << t)) != 0) iRows += rowCount[t] * rowSize[t];
        int iPatched = 0;
        for (int r = 0; r < rowCount[6]; r++)
        {
            int iRow = iRows + r * rowSize[6];
            if (iRow + 4 > rgD.Length) break;
            if (rvaMap.TryGetValue(r + 1, out int newRva))
            {
                BitConverter.GetBytes(newRva).CopyTo(rgD, iRow);
                iPatched++;
            }
        }

    }

    private static byte[] InjectIlNoise(byte[] rgB, Random rng)
    {
        int iPe = BitConverter.ToInt32(rgB, 0x3C);
        if (iPe + 0x18 > rgB.Length || BitConverter.ToUInt32(rgB, iPe) != 0x4550) return rgB;
        ushort usMagic = BitConverter.ToUInt16(rgB, iPe + 24);
        ushort usNum = BitConverter.ToUInt16(rgB, iPe + 6);
        ushort usOpt = BitConverter.ToUInt16(rgB, iPe + 20);
        int iOpt = iPe + 24;
        int iSec = iOpt + usOpt;
        int iTva = 0, iTroff = 0, iTvs = 0, iTrsz = 0;
        for (int i = 0; i < usNum; i++)
        {
            int o = iSec + i * 40;
            if (o + 40 > rgB.Length) break;
            if (Encoding.ASCII.GetString(rgB, o, 8).TrimEnd('\0') == ".text")
            {
                iTva = BitConverter.ToInt32(rgB, o + 12);
                iTvs = BitConverter.ToInt32(rgB, o + 8);
                iTroff = BitConverter.ToInt32(rgB, o + 20);
                iTrsz = BitConverter.ToInt32(rgB, o + 16);
                break;
            }
        }
        if (iTva == 0 || iTroff == 0) return rgB;
        var rgPlain = new HashSet<string> { "X1","X3","DeriveSeed","RestoreData","GenDisturb","CheckElapsed","SelfCheck","AD" };
        //就地扰动明文方法 加密方法由MethodEncryptor加密 jf方法体哈希锁定AD/X1/X3/F不在范围
        var rgAll = new List<MReloc>();
        using (var ms = new MemoryStream(rgB, writable: false))
        using (var per = new PEReader(ms))
        {
            var mr = per.GetMetadataReader();
            foreach (var mdh in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(mdh);
                int iRva = md.RelativeVirtualAddress;
                if (iRva == 0) continue;
                string sName = mr.GetString(md.Name);
                int iOff = iTroff + (iRva - iTva);
                if (iOff < 0 || iOff + 12 > rgB.Length) continue;
                byte b0 = rgB[iOff];
                int iHdr, iCs;
                if ((b0 & 3) == 2) { iHdr = 1; iCs = b0 >> 2; }
                else if ((b0 & 3) == 3) { iHdr = 12; iCs = BitConverter.ToInt32(rgB, iOff + 4); }
                else continue;
                if (iCs <= 0 || iCs > 0xFFFF) continue;
                int iRow = MetadataTokens.GetRowNumber(mr, mdh);
                bool bHasEh = false;
                int iFull = 0;
                try
                {
                    var mbb = per.GetMethodBody(iRva);
                    iFull = mbb.Size;
                    bHasEh = mbb.ExceptionRegions.Any();
                }
                catch { iFull = iHdr + iCs; }
                if (iFull < iHdr + iCs) iFull = iHdr + iCs;
                var m = new MReloc { Row = iRow, iRva = iRva, iHdr = iHdr, iCs = iCs };
                m.bPlain = rgPlain.Contains(sName) && !bHasEh
                    && sName != "AD" && sName != "X1" && sName != "X3";
                m.rgBody = new byte[iFull];
                Buffer.BlockCopy(rgB, iOff, m.rgBody, 0, iFull);
                rgAll.Add(m);
            }
        }
        if (rgAll.Count == 0) return rgB;
        var rgS = rgAll.OrderBy(m => m.iRva).ToList();
        int iMetaShift = 8;//metadata平移按4对齐 为最后方法就地增长腾空隙
        var rgOut = (byte[])rgB.Clone();
        int iCli = iOpt + (usMagic == 0x20B ? 112 : 96) + 14 * 8;
        int iMd = -1;
        int iCliOff = -1;
        if (iCli + 16 <= rgB.Length)
        {
            uint uCliRva = BitConverter.ToUInt32(rgB, iCli);
            iCliOff = RvaToOffset(rgB, iPe, usOpt, usNum, uCliRva);
            if (iCliOff >= 0)
            {
                uint uMdRva = BitConverter.ToUInt32(rgB, iCliOff + 8);
                if (uMdRva != 0) iMd = RvaToOffset(rgB, iPe, usOpt, usNum, uMdRva);
            }
        }
        if (iMd >= 0)
        {
            int iMetaSize = BitConverter.ToInt32(rgB, iCliOff + 12);
            if (iMetaSize <= 0 || iMd + iMetaSize + iMetaShift > iTroff + iTvs) return rgB;
            byte[] rgMeta = new byte[iMetaSize];
            Buffer.BlockCopy(rgOut, iMd, rgMeta, 0, iMetaSize);
            Buffer.BlockCopy(rgMeta, 0, rgOut, iMd + iMetaShift, iMetaSize);
            Array.Clear(rgOut, iMd, Math.Min(iMetaShift, iMetaSize));
            BitConverter.GetBytes(BitConverter.ToUInt32(rgB, iCliOff + 8) + (uint)iMetaShift).CopyTo(rgOut, iCliOff + 8);
        }
        int iDisturbed = 0, iGrow = 0;
        for (int i = 0; i < rgS.Count; i++)
        {
            var m = rgS[i];
            if (!m.bPlain || m.iHdr != 12) continue;
            int iNextRva = i + 1 < rgS.Count ? rgS[i + 1].iRva : iTva + iTvs;
            int iGap = iNextRva - m.iRva - (m.iHdr + m.iCs);
            if (iGap <= 0) continue;
            int iOff = iTroff + (m.iRva - iTva);
            byte[] rgD = DisturbIl(rgB, iOff + m.iHdr, m.iCs, Math.Min(iGap, 3), rng);
            if (rgD == null) continue;
            if (!IlSanity(rgD)) { Console.WriteLine($"  il_noise: DISTURB-FAIL {m.sName}"); continue; }
            m.rgBody = new byte[m.iHdr + rgD.Length];
            Buffer.BlockCopy(rgB, iOff, m.rgBody, 0, m.iHdr);
            Buffer.BlockCopy(rgD, 0, m.rgBody, m.iHdr, rgD.Length);
            BitConverter.GetBytes(rgD.Length).CopyTo(m.rgBody, 4);
            m.bGrow = true;
            iDisturbed++;
            iGrow += rgD.Length - m.iCs;
        }
        for (int i = 0; i < rgS.Count; i++)
        {
            var m = rgS[i];
            if (m.bGrow)
                Buffer.BlockCopy(m.rgBody, 0, rgOut, iTroff + (m.iRva - iTva), m.rgBody.Length);
        }
        //方法起点RVA不变 零移动 md6表无需更新
        Console.WriteLine($"  il_noise: {rgS.Count} methods, disturb={iDisturbed}, +{iGrow} bytes");
        return rgOut;
    }

    //短指令就地换成2字节长形式 每处+1字节 用方法体后空隙填充 零移动并重定位分支
    private static byte[] DisturbIl(byte[] rgB, int iIl, int iCs, int iGap, Random rng)
    {
        if (iCs <= 8 || iGap <= 0) return null;
        byte[] rgIl = new byte[iCs];
        Array.Copy(rgB, iIl, rgIl, 0, iCs);
        var rgInsn = new List<(int iPos, int iLen)>();
        var rgBr = new List<(int iPos, int iLen, int[] rgT)>();
        int p = 0;
        while (p < iCs)
        {
            int l = IlLen(rgIl, p);
            if (l <= 0 || p + l > iCs) return null;
            rgInsn.Add((p, l));
            if (IsBranch(rgIl[p]))
            {
                int[] rgT = BranchTargets(rgIl, p, l);
                if (rgT != null && rgT.Length > 0) rgBr.Add((p, l, rgT));
            }
            p += l;
        }
        var rgSkip = new HashSet<int>();
        foreach (var br in rgBr) foreach (int tg in br.rgT) rgSkip.Add(tg);
        var rgCand = new List<int>();
        for (int i = 0; i < rgInsn.Count; i++)
        {
            byte op = rgIl[rgInsn[i].iPos];
            bool b1 = (op >= 0x02 && op <= 0x0D) || (op >= 0x15 && op <= 0x1E)
                || (rgInsn[i].iLen == 1 && !IsBranch(op) && op != 0x2A && op != 0x7A && op != 0xFE);
            if (b1 && !rgSkip.Contains(rgInsn[i].iPos)) rgCand.Add(i);
        }
        if (rgCand.Count == 0) return null;
        int iWant = Math.Min(Math.Min(iGap, 3), rgCand.Count);
        var rgSel = new List<int>();
        for (int t = 0; t < iWant; t++)
        {
            int idx = rng.Next(rgCand.Count);
            rgSel.Add(rgInsn[rgCand[idx]].iPos);
            rgCand.RemoveAt(idx);
        }
        rgSel.Sort();
        foreach (var br in rgBr)
        {
            if (br.iLen != 2) continue;
            int iNewPos = br.iPos + CountLess(rgSel, br.iPos);
            foreach (int tg in br.rgT)
            {
                int iNewTgt = tg + CountLess(rgSel, tg);
                int iOff = iNewTgt - (iNewPos + 2);
                if (iOff < -128 || iOff > 127) return null;
            }
        }
        byte[] rgNew = new byte[iCs + rgSel.Count];
        int iSrc = 0, iDst = 0, iSel = 0;
        while (iSrc < iCs)
        {
            if (iSel < rgSel.Count && iSrc == rgSel[iSel])
            {
                byte op = rgIl[iSrc];
                if (op >= 0x02 && op <= 0x0D)
                {
                    byte bNew = op <= 0x05 ? (byte)0x0E : (op <= 0x09 ? (byte)0x11 : (byte)0x13);
                    byte bSub = (byte)(op <= 0x05 ? op - 0x02 : (op <= 0x09 ? op - 0x06 : op - 0x0A));
                    rgNew[iDst++] = bNew;
                    rgNew[iDst++] = bSub;
                }
                else if (op >= 0x15 && op <= 0x1E)
                {
                    rgNew[iDst++] = 0x1F;//ldc.i4.s
                    rgNew[iDst++] = op == 0x15 ? (byte)0xFF : (byte)(op - 0x16);
                }
                else
                {
                    rgNew[iDst++] = op;//1字节指令加nop 语义等价
                    rgNew[iDst++] = 0x00;
                }
                iSel++;
                iSrc += 1;//原指令1字节
                continue;
            }
            int l = IlLen(rgIl, iSrc);
            int iNewPos = iSrc + CountLess(rgSel, iSrc);
            if (IsBranch(rgIl[iSrc]))
            {
                rgNew[iDst] = rgIl[iSrc];
                if (rgIl[iSrc] == 0x45)
                {
                    int n = BitConverter.ToInt32(rgIl, iSrc + 1);
                    int iBase = iSrc + 5 + n * 4;
                    int iNewBase = iBase + CountLess(rgSel, iBase);
                    BitConverter.GetBytes(n).CopyTo(rgNew, iDst + 1);
                    for (int k = 0; k < n; k++)
                    {
                        int iOld = iBase + BitConverter.ToInt32(rgIl, iSrc + 5 + k * 4);
                        int iNew = iOld + CountLess(rgSel, iOld);
                        BitConverter.GetBytes(iNew - iNewBase).CopyTo(rgNew, iDst + 5 + k * 4);
                    }
                }
                else if (l == 2)
                {
                    int iOldT = iSrc + 2 + (sbyte)rgIl[iSrc + 1];
                    int iNewT = iOldT + CountLess(rgSel, iOldT);
                    rgNew[iDst + 1] = (byte)(sbyte)(iNewT - (iNewPos + 2));
                }
                else
                {
                    int iOldT = iSrc + 5 + BitConverter.ToInt32(rgIl, iSrc + 1);
                    int iNewT = iOldT + CountLess(rgSel, iOldT);
                    BitConverter.GetBytes(iNewT - (iNewPos + 5)).CopyTo(rgNew, iDst + 1);
                }
            }
            else
            {
                Buffer.BlockCopy(rgIl, iSrc, rgNew, iDst, l);
            }
            iSrc += l; iDst += l;
        }
        return rgNew;
    }

    //不透明谓词注入 恒假分支 无EH方法
    private static byte[] ObfuscateIl(byte[] rgB, int iIl, int iCs, Random rng)
    {
        byte[] rgIl = new byte[iCs];
        Array.Copy(rgB, iIl, rgIl, 0, iCs);
        var rgInsn = new List<(int iPos, int iLen)>();
        var rgBr = new List<(int iPos, int iLen, int[] rgT)>();
        int p = 0;
        while (p < iCs)
        {
            int l = IlLen(rgIl, p);
            if (l <= 0 || p + l > iCs) return null;
            rgInsn.Add((p, l));
            if (IsBranch(rgIl[p]))
            {
                int[] rgT = BranchTargets(rgIl, p, l);
                if (rgT != null && rgT.Length > 0) rgBr.Add((p, l, rgT));
            }
            p += l;
        }
        if (rgInsn.Count < 6) return null;
        var rgIns = new List<int>();
        int iWant = 1;
        for (int t = 0; t < iWant; t++)
        {
            bool bOk = false;
            for (int a = 0; a < 40 && !bOk; a++)
            {
                int idx = 2 + rng.Next(Math.Max(1, rgInsn.Count - 4));
                int ip = rgInsn[idx].iPos + rgInsn[idx].iLen;
                if (ip <= 1 || ip >= iCs - 1) continue;
                if (rgIns.Contains(ip)) continue;
                //插入点前的指令不能是终止指令(ret/br/switch/leave/throw)
                byte opPrev = rgIl[rgInsn[idx].iPos];
                if (opPrev == 0x2A || opPrev == 0x7A || opPrev == 0xDC || opPrev == 0xDD || opPrev == 0xDE ||
                    (opPrev >= 0x38 && opPrev <= 0x45)) continue;
                if (opPrev == 0xFE && rgIl[rgInsn[idx].iPos + 1] == 0x11) continue;
                bool bTgt = false;
                foreach (var br in rgBr)
                    foreach (int tg in br.rgT)
                        if (tg == ip) { bTgt = true; break; }
                if (bTgt) continue;
                if (ShortOk(rgIns, ip, rgBr)) { rgIns.Add(ip); bOk = true; }
            }
            if (!bOk) break;
        }
        if (rgIns.Count == 0) return null;
        rgIns.Sort();
        int iDelta = rgIns.Count * 6;
        byte[] rgNew = new byte[iCs + iDelta];
        int iSrc = 0, iDst = 0, iInsIdx = 0;
        while (iSrc < iCs)
        {
            if (iInsIdx < rgIns.Count && iSrc == rgIns[iInsIdx])
            {
                //恒假分支恰为6字节 与预留量一致
                rgNew[iDst++] = 0x16;
                rgNew[iDst++] = 0x2C; rgNew[iDst++] = 0x03;
                rgNew[iDst++] = 0x16;
                rgNew[iDst++] = 0x26;
                rgNew[iDst++] = 0x00;
                iInsIdx++;
                continue;
            }
            int l = IlLen(rgIl, iSrc);
            int iNewPos = iSrc + (CountLess(rgIns, iSrc) + (rgIns.Contains(iSrc) ? 1 : 0)) * 6;
            if (IsBranch(rgIl[iSrc]))
            {
                rgNew[iDst] = rgIl[iSrc];
                if (rgIl[iSrc] == 0x45)
                {
                    int n = BitConverter.ToInt32(rgIl, iSrc + 1);
                    int iBase = iSrc + 5 + n * 4;
                    int iNewBase = iBase + CountLess(rgIns, iBase) * 6;
                    BitConverter.GetBytes(n).CopyTo(rgNew, iDst + 1);
                    for (int k = 0; k < n; k++)
                    {
                        int iOld = iBase + BitConverter.ToInt32(rgIl, iSrc + 5 + k * 4);
                        int iNew = iOld + CountLess(rgIns, iOld) * 6;
                        BitConverter.GetBytes(iNew - iNewBase).CopyTo(rgNew, iDst + 5 + k * 4);
                    }
                }
                else if (l == 2)
                {
                    int iOldT = iSrc + 2 + (sbyte)rgIl[iSrc + 1];
                    int iNewT = iOldT + CountLess(rgIns, iOldT) * 6;
                    rgNew[iDst + 1] = (byte)(sbyte)(iNewT - (iNewPos + 2));
                }
                else
                {
                    int iOldT = iSrc + 5 + BitConverter.ToInt32(rgIl, iSrc + 1);
                    int iNewT = iOldT + CountLess(rgIns, iOldT) * 6;
                    BitConverter.GetBytes(iNewT - (iNewPos + 5)).CopyTo(rgNew, iDst + 1);
                }
            }
            else
            {
                Buffer.BlockCopy(rgIl, iSrc, rgNew, iDst, l);
            }
            iSrc += l; iDst += l;
        }
        return rgNew;
    }

    #endregion
    #region IL指令表

    //IL指令长度 ECMA-335
    private static int IlLen(byte[] rgIl, int i)
    {
        byte b = rgIl[i];
        if (b == 0x45)
        {
            int n = BitConverter.ToInt32(rgIl, i + 1);
            return 5 + n * 4;
        }
        int l = b == 0xFE ? rgOpLen[0x100 + rgIl[i + 1]] : rgOpLen[b];
        return l > 0 ? l : 1;
    }

    //IL操作码长度表 运行时从OpCodes反射生成 覆盖全部ECMA-335
    private static readonly int[] rgOpLen = BuildOpLen();
    private static int[] BuildOpLen()
    {
        var rg = new int[0x200];
        foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var op = (System.Reflection.Emit.OpCode)f.GetValue(null);
            int n = OperandLen(op.OperandType);
            if (op.Size == 1 && op.Value >= 0) rg[op.Value] = op.Size + n;
            else if (op.Size == 2) rg[0x100 + (op.Value & 0xFF)] = op.Size + n;
        }
        return rg;
    }
    private static int OperandLen(System.Reflection.Emit.OperandType t)
    {
        switch (t)
        {
            case System.Reflection.Emit.OperandType.InlineNone: return 0;
            case System.Reflection.Emit.OperandType.ShortInlineI:
            case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
            case System.Reflection.Emit.OperandType.ShortInlineVar: return 1;
            case System.Reflection.Emit.OperandType.InlineVar: return 2;
            case System.Reflection.Emit.OperandType.InlineI:
            case System.Reflection.Emit.OperandType.InlineBrTarget:
            case System.Reflection.Emit.OperandType.InlineField:
            case System.Reflection.Emit.OperandType.InlineMethod:
            case System.Reflection.Emit.OperandType.InlineType:
            case System.Reflection.Emit.OperandType.InlineString:
            case System.Reflection.Emit.OperandType.InlineSig:
            case System.Reflection.Emit.OperandType.InlineTok: return 4;
            case System.Reflection.Emit.OperandType.InlineI8:
            case System.Reflection.Emit.OperandType.InlineR: return 8;
            default: return 0;
        }
    }

    private static bool IsBranch(byte b) => (b >= 0x2B && b <= 0x44) || b == 0x45;

    private static int[] BranchTargets(byte[] rgIl, int p, int l)
    {
        if (rgIl[p] == 0x45)
        {
            int n = BitConverter.ToInt32(rgIl, p + 1);
            int iBase = p + 5 + n * 4;
            var rg = new int[n];
            for (int i = 0; i < n; i++) rg[i] = iBase + BitConverter.ToInt32(rgIl, p + 5 + i * 4);
            return rg;
        }
        int iT = l == 2 ? p + 2 + (sbyte)rgIl[p + 1] : p + 5 + BitConverter.ToInt32(rgIl, p + 1);
        return new int[] { iT };
    }

    private static int CountLess(List<int> rgS, int x)
    {
        int n = 0;
        foreach (int s in rgS) if (s < x) n++;
        return n;
    }

    //验证插入后所有短分支偏移不溢出sbyte
    private static bool ShortOk(List<int> rgIns, int ip, List<(int iPos, int iLen, int[] rgT)> rgBr)
    {
        var rgT = new List<int>(rgIns) { ip };
        rgT.Sort();
        foreach (var br in rgBr)
        {
            if (br.iLen != 2) continue;
            int iNewPos = br.iPos + (CountLess(rgT, br.iPos) + (rgT.Contains(br.iPos) ? 1 : 0)) * 6;
            foreach (int tg in br.rgT)
            {
                int iNewTgt = tg + CountLess(rgT, tg) * 6;
                int iOff = iNewTgt - (iNewPos + 2);
                if (iOff < -128 || iOff > 127) return false;
            }
        }
        return true;
    }

    #endregion
    #region 常量对随机化

    //常量对随机化 改名前按原名收集字段token
    private static Dictionary<string, uint> FieldTokens(byte[] rgD, string[] rgNames)
    {
        var rgMap = new Dictionary<string, uint>(StringComparer.Ordinal);
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.FieldDefinitions)
        {
            var fd = mr.GetFieldDefinition(h);
            string s = mr.GetString(fd.Name);
            if (Array.IndexOf(rgNames, s) >= 0) rgMap[s] = (uint)MetadataTokens.GetToken(h);
        }
        return rgMap;
    }

    private static void ReadCctorValues(byte[] rgD, Dictionary<uint, uint> rgVal, out uint uCctor)
    {
        uCctor = 0;
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) != ".cctor") continue;
            uCctor = (uint)MetadataTokens.GetToken(h);
            var mrb = per.GetMethodBody(md.RelativeVirtualAddress);
            if (mrb == null) continue;
            byte[] rgIlBytes = mrb.GetILBytes();
            for (int iP = 0; iP + 5 < rgIlBytes.Length; iP++)
            {
                if (rgIlBytes[iP] != 0x7D) continue;
                uint uTok = BitConverter.ToUInt32(rgIlBytes, iP + 1);
                if (!rgVal.ContainsKey(uTok)) continue;
                if (iP >= 5 && rgIlBytes[iP - 5] == 0x20) rgVal[uTok] = BitConverter.ToUInt32(rgIlBytes, iP - 4);
                else if (iP >= 2 && rgIlBytes[iP - 2] == 0x1F) rgVal[uTok] = (uint)(sbyte)rgIlBytes[iP - 1];
                else if (iP >= 1 && rgIlBytes[iP - 1] >= 0x16 && rgIlBytes[iP - 1] <= 0x1E) rgVal[uTok] = (uint)(rgIlBytes[iP - 1] - 0x16);
            }
        }
    }

    private static void WriteCctorValues(byte[] rgD, uint uCctor, Dictionary<uint, uint> rgNew)
    {
        int iPe = BitConverter.ToInt32(rgD, 0x3C);
        ushort usNumSec = BitConverter.ToUInt16(rgD, iPe + 6);
        ushort usOptSize = BitConverter.ToUInt16(rgD, iPe + 20);
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            if ((uint)MetadataTokens.GetToken(h) != uCctor) continue;
            var md = mr.GetMethodDefinition(h);
            int iRva = md.RelativeVirtualAddress;
            var mrb = per.GetMethodBody(iRva);
            if (mrb == null) continue;
            byte[] rgIlBytes = mrb.GetILBytes();
            bool bChanged = false;
            for (int iP = 0; iP + 5 < rgIlBytes.Length; iP++)
            {
                if (rgIlBytes[iP] != 0x7D) continue;
                uint uTok = BitConverter.ToUInt32(rgIlBytes, iP + 1);
                if (!rgNew.TryGetValue(uTok, out uint uV)) continue;
                if (iP >= 5 && rgIlBytes[iP - 5] == 0x20) { BitConverter.GetBytes(uV).CopyTo(rgIlBytes, iP - 4); bChanged = true; }
            }
            if (!bChanged) continue;
            int iOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, (uint)iRva);
            if (iOff < 0) continue;
            int iHdr = (rgD[iOff] & 3) == 2 ? 1 : 12;
            Array.Copy(rgIlBytes, 0, rgD, iOff + iHdr, rgIlBytes.Length);
        }
    }

    private static List<uint> BootTokens(byte[] rgD)
    {
        var rg = new List<uint>();
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            string sName = mr.GetString(md.Name);
            if (sName == "W1" || sName == "X4" || sName == "X5" || sName == "TCheck" || sName == "X2" || sName == "X3Comp" || sName == "VerifyBodies")
                rg.Add(0x06000000u | (uint)MetadataTokens.GetRowNumber(mr, h));
        }
        return rg;
    }
    #endregion
}
