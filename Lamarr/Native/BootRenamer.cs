using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.IO;

namespace Lamarr.NativePack;

public static class BootRenamer
{
    private static readonly char[] rgFirst = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
    private static readonly char[] rgChar = "abcdefghijklmnopqrstuvwxyz123456789".ToCharArray();
    private static readonly Random rnd = new Random();
    private static readonly string[] rgSelfCheck = { "TCheck", "CheckElapsed", "VerifyBodies" };

    public static byte[] Rename(byte[] rgD, string sDictPath = "")
    {
        var rgDict = LoadDict(sDictPath);
        uint uLfa = BitConverter.ToUInt32(rgD, 0x3C);
        int iPe = (int)uLfa;
        if (iPe <= 0 || iPe + 0x60 >= rgD.Length || BitConverter.ToUInt32(rgD, iPe) != 0x4550)
            throw new InvalidDataException("no PE signature");
        ushort usNumSec = BitConverter.ToUInt16(rgD, iPe + 6);
        ushort usOptSize = BitConverter.ToUInt16(rgD, iPe + 20);
        ushort usMagic = BitConverter.ToUInt16(rgD, iPe + 24);
        int iDdStart = iPe + 24 + (usMagic == 0x20B ? 112 : 96);
        uint uCliRva = BitConverter.ToUInt32(rgD, iDdStart + 14 * 8);
        if (uCliRva == 0) throw new InvalidDataException("no CLI header");
        int iCliOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uCliRva);
        uint uMetaRva = BitConverter.ToUInt32(rgD, iCliOff + 8);
        int iMetaOff = RvaToOffset(rgD, iPe, usOptSize, usNumSec, uMetaRva);
        if (iMetaOff <= 0 || BitConverter.ToUInt32(rgD, iMetaOff) != 0x424A5342)
            throw new InvalidDataException("no BSJB root");

        int iVerLen = BitConverter.ToInt32(rgD, iMetaOff + 12);
        int iStm = iMetaOff + 16 + iVerLen;
        ushort usStreams = BitConverter.ToUInt16(rgD, iStm + 2);
        int iCur = iStm + 4;
        int iStrings = -1, iTables = -1;
        for (int i = 0; i < usStreams; i++)
        {
            uint uOff = BitConverter.ToUInt32(rgD, iCur);
            int iNl = 0;
            while (rgD[iCur + 8 + iNl] != 0) iNl++;
            string sName = Encoding.ASCII.GetString(rgD, iCur + 8, iNl);
            if (sName == "#Strings") iStrings = (int)uOff;
            if (sName == "#~") iTables = (int)uOff;
            iCur += 8 + iNl + 1;
            iCur = (iCur + 3) & ~3;
        }
        if (iStrings < 0 || iTables < 0) throw new InvalidDataException("no #Strings/#~ stream");

        int iHeap = iMetaOff + iStrings;

        //框架引用不可改名
        var rgProt = new HashSet<int>();
        using (var ms = new MemoryStream(rgD, writable: false))
        using (var per = new PEReader(ms))
        {
            var mr = per.GetMetadataReader();
            foreach (var h in mr.TypeReferences)
            {
                var tr = mr.GetTypeReference(h);
                rgProt.Add(MetadataTokens.GetHeapOffset(tr.Name));
                if (!tr.Namespace.IsNil) rgProt.Add(MetadataTokens.GetHeapOffset(tr.Namespace));
            }
            foreach (var h in mr.MemberReferences)
            {
                var mref = mr.GetMemberReference(h);
                rgProt.Add(MetadataTokens.GetHeapOffset(mref.Name));
            }
        }

        //PInvoke/Runtime/构造器名受CLR约束
        var rgCand = new List<int>();
        using (var ms = new MemoryStream(rgD, writable: false))
        using (var per = new PEReader(ms))
        {
            var mr = per.GetMetadataReader();
            foreach (var h in mr.MethodDefinitions)
            {
                var md = mr.GetMethodDefinition(h);
                if ((md.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                if (((int)md.ImplAttributes & 3) == 3) continue;
                string sM = mr.GetString(md.Name);
                if (sM == "Main") continue;
                if (sM == ".ctor" || sM == ".cctor") continue;
                int iOff = MetadataTokens.GetHeapOffset(md.Name);
                if (!rgProt.Contains(iOff)) rgCand.Add(iOff);
            }
            foreach (var h in mr.FieldDefinitions)
            {
                var fd = mr.GetFieldDefinition(h);
                int iOff = MetadataTokens.GetHeapOffset(fd.Name);
                if (!rgProt.Contains(iOff)) rgCand.Add(iOff);
            }
            foreach (var h in mr.MethodDefinitions)
            {
                var md2 = mr.GetMethodDefinition(h);
                foreach (var ph in md2.GetParameters())
                {
                    var pd = mr.GetParameter(ph);
                    if (pd.Name.IsNil) continue;
                    int iOff = MetadataTokens.GetHeapOffset(pd.Name);
                    if (!rgProt.Contains(iOff)) rgCand.Add(iOff);
                }
            }
            foreach (var h in mr.TypeDefinitions)
            {
                var td = mr.GetTypeDefinition(h);
                int iOff = MetadataTokens.GetHeapOffset(td.Name);
                if (!rgProt.Contains(iOff)) rgCand.Add(iOff);
                if (!td.Namespace.IsNil)
                {
                    int iNs = MetadataTokens.GetHeapOffset(td.Namespace);
                    if (!rgProt.Contains(iNs)) rgCand.Add(iNs);
                }
            }
        }

        //共享偏移只处理一次
        var rgSeen = new HashSet<int>();
        var rgList = new List<int>();
        foreach (int c in rgCand) if (rgSeen.Add(c)) rgList.Add(c);

        //覆盖前量取长度
        var rgAll = new Dictionary<int, int>();
        foreach (int p in rgProt) if (!rgAll.ContainsKey(p)) rgAll[p] = StrLen(rgD, iHeap, p);
        foreach (int c in rgList) if (!rgAll.ContainsKey(c)) rgAll[c] = StrLen(rgD, iHeap, c);

        //#Strings后缀共享 与保护串冲突者保留 与候选冲突者按分量紧凑覆盖
        var rgSkip = new HashSet<int>();
        var rgL = new List<int>(rgList);
        rgL.Sort();
        foreach (int x in rgL)
        {
            int lx = rgAll[x];
            foreach (var kv in rgAll)
            {
                int y = kv.Key;
                if (y == x || !rgProt.Contains(y)) continue;
                int ly = kv.Value;
                if ((y < x && x < y + ly) || (x < y && y < x + lx)) { rgSkip.Add(x); break; }
            }
        }
        var rgGrp = new List<List<int>>();
        for (int gi = 0; gi < rgL.Count; gi++)
        {
            int x = rgL[gi];
            if (rgSkip.Contains(x)) continue;
            var g = new List<int> { x };
            int gEnd = x + rgAll[x];
            for (int gj = gi + 1; gj < rgL.Count; gj++)
            {
                int y = rgL[gj];
                if (rgSkip.Contains(y) || y >= gEnd) break;
                g.Add(y);
                gEnd = Math.Max(gEnd, y + rgAll[y]);
                gi = gj;
            }
            rgGrp.Add(g);
        }
        var rgUsed = new HashSet<string>();
        foreach (var g in rgGrp)
        {
            int iBound = g[g.Count - 1] + rgAll[g[g.Count - 1]];
            for (int gi = g.Count - 1; gi >= 0; gi--)
            {
                int iOff = g[gi];
                int iLen = rgAll[iOff];
                int iMax = iBound - iOff;
                if (iMax < 2) continue;
                string sOld = Encoding.ASCII.GetString(rgD, iHeap + iOff, Math.Min(iLen, 64)).TrimEnd('\0');
                string sNew = (rgDict.Length > 0 && Array.IndexOf(rgSelfCheck, sOld) >= 0)
                    ? PickDict(rgDict, rgUsed) : MakeName(Math.Min(iLen, iMax), rgUsed);
                byte[] rgB = Encoding.ASCII.GetBytes(sNew);
                if (rgB.Length + 1 > iMax)
                {
                    sNew = MakeName(1, rgUsed);
                    rgB = Encoding.ASCII.GetBytes(sNew);
                }
                Array.Copy(rgB, 0, rgD, iHeap + iOff, rgB.Length);
                for (int i = rgB.Length; i <= iLen && iHeap + iOff + i < rgD.Length && i < iMax; i++)
                    rgD[iHeap + iOff + i] = 0;
                iBound = iOff;
            }
        }
        return rgD;
    }

    private static string[] LoadDict(string sPath)
    {
        if (string.IsNullOrEmpty(sPath) || !File.Exists(sPath)) return Array.Empty<string>();
        try
        {
            var rg = new List<string>();
            foreach (var l in File.ReadAllLines(sPath))
            {
                string t = l.Trim();
                if (t.Length > 0 && !t.StartsWith('#')) rg.Add(t);
            }
            return rg.ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string PickDict(string[] rgDict, HashSet<string> rgUsed)
    {
        int n = rgDict.Length;
        int st = rnd.Next(n);
        for (int t = 0; t < n; t++)
        {
            string s = rgDict[(st + t) % n].Trim();
            if (s.Length > 0 && rgUsed.Add(s)) return s;
        }
        return MakeName(2, rgUsed);
    }

    private static int StrLen(byte[] rgD, int iHeap, int iOff)
    {
        int iLen = 0;
        while (iHeap + iOff + iLen < rgD.Length && rgD[iHeap + iOff + iLen] != 0) iLen++;
        return iLen;
    }

    private static string MakeName(int iMaxLen, HashSet<string> rgUsed)
    {
        int iL = Math.Min(iMaxLen, 2);
        for (int l = iL; l >= 1; l--)//单字符名留给真正的短名
        {
            for (int t = 0; t < 4000; t++)
            {
                var sb = new StringBuilder(l);
                sb.Append(rgFirst[rnd.Next(rgFirst.Length)]);//数字开头名触发TypeLoadException
                for (int k = 1; k < l; k++) sb.Append(rgChar[rnd.Next(rgChar.Length)]);
                string s = sb.ToString();
                if (rgUsed.Add(s)) return s;
            }
        }
        return "_";
    }

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
}

