using Lamarr;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Lamarr.Packer;

internal partial class PeWriterAntheil
{
    #region 加密体排序校验(A)

    //加密体在jit钩子就绪前不得被调用(JIT拿到密文) 违规即打包失败
    //就绪点=RunBoot里调用_fnJitInstall的callvirt 钩子从此刻起才在JIT路径上
    //_bJitReady的stsfld更晚 其后还有WarmExitPath这类合法预热 只用于报告
    //S=从入口Main与相关类型.cctor出发 call/callvirt/newobj目标在就绪点前可达的闭包 加密体自身不展开
    //S成员在就绪点前调用加密体=VIOLATION 之后=ok 线程/定时器/回调静态无法定序只报告 MemberRef/calli只报计数
    internal static void CheckEncryptOrder(byte[] rgD)
    {
        var rgEncToks = BootTokens(rgD);
        if (rgEncToks.Count == 0)
        {
            Console.WriteLine("  boot_order: encrypted body list is empty -> skip");
            return;
        }
        var setEnc = new HashSet<uint>(rgEncToks);

        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        var mapName = new Dictionary<uint, string>();
        var mapIl = new Dictionary<uint, byte[]>();
        var mapOwner = new Dictionary<uint, uint>();
        foreach (var h in mr.MethodDefinitions)
        {
            uint uTok = (uint)MetadataTokens.GetToken(h);
            var md = mr.GetMethodDefinition(h);
            string sType = mr.GetString(mr.GetTypeDefinition(md.GetDeclaringType()).Name);
            mapName[uTok] = $"{sType}.{mr.GetString(md.Name)}";
            mapOwner[uTok] = (uint)MetadataTokens.GetToken(md.GetDeclaringType());
            if (md.RelativeVirtualAddress == 0) continue;
            var mbb = per.GetMethodBody(md.RelativeVirtualAddress);
            mapIl[uTok] = mbb.GetILBytes() ?? Array.Empty<byte>();
        }

        uint uFldInstall = FieldOf(rgD, "_fnJitInstall");
        uint uFldReady = FieldOf(rgD, "_bJitReady");
        if (uFldInstall == 0 && uFldReady == 0)
        {
            Console.WriteLine("  boot_order: ready anchor not found (_fnJitInstall/_bJitReady) -> skip");
            return;
        }
        var mapAnchor = new Dictionary<uint, int>();
        var mapReady = new Dictionary<uint, int>();
        foreach (var kv in mapIl)
        {
            byte[] rgIl = kv.Value;
            int iLdsfld = -1, iA = -1, iR = -1;
            foreach (var op in Ops(rgIl, rgIl.Length))
            {
                if (uFldInstall != 0 && rgIl[op.iP] == 0x7E && op.iP + 4 < rgIl.Length && BitConverter.ToUInt32(rgIl, op.iP + 1) == uFldInstall) iLdsfld = op.iP;
                if (iA < 0 && iLdsfld >= 0 && op.uOp == 0x6Fu) iA = op.iP + op.iLen;
                if (uFldReady != 0 && rgIl[op.iP] == 0x80 && op.iP + 4 < rgIl.Length && BitConverter.ToUInt32(rgIl, op.iP + 1) == uFldReady) iR = op.iP;
            }
            if (iA >= 0) mapAnchor[kv.Key] = iA;
            if (iR >= 0) mapReady[kv.Key] = iR;
        }

        int LimitOf(uint uTok)
        {
            int i = int.MaxValue;
            if (mapAnchor.TryGetValue(uTok, out int iA)) i = Math.Min(i, iA);
            if (mapReady.TryGetValue(uTok, out int iR)) i = Math.Min(i, iR);
            return i;
        }

        var setS = new HashSet<uint>();
        var mpParent = new Dictionary<uint, (uint uCaller, int iOff, uint uOp)>();
        var q = new Queue<uint>();
        void Seed(uint uTok)
        {
            if (uTok == 0 || setEnc.Contains(uTok) || setS.Contains(uTok) || !mapIl.ContainsKey(uTok)) return;
            setS.Add(uTok);
            q.Enqueue(uTok);
        }
        int iEp = per.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (iEp != 0) Seed((uint)iEp);
        //cctor在首次使用前自行调用 必须显式入种子 否则提前调用查不出来
        var setSeedType = new HashSet<uint>();
        foreach (var kv in mapOwner)
            if (setEnc.Contains(kv.Key) || mapAnchor.ContainsKey(kv.Key) || mapReady.ContainsKey(kv.Key)) setSeedType.Add(kv.Value);
        foreach (var kv in mapOwner)
            if (setSeedType.Contains(kv.Value) && mapName[kv.Key].EndsWith("..cctor", StringComparison.Ordinal)) Seed(kv.Key);
        var setDeferPre = new HashSet<uint>();//就绪前启动的体 可能并发于钩子就绪之前
        var qDefer = new Queue<uint>();
        while (q.Count > 0)
        {
            uint uM = q.Dequeue();
            byte[] rgIl = mapIl[uM];
            int iEnd = Math.Min(rgIl.Length, LimitOf(uM));
            foreach (var op in Ops(rgIl, iEnd))
            {
                if (op.uOp != 0x28u && op.uOp != 0x6Fu && op.uOp != 0x73u) continue;
                if ((op.uTarget & 0xFF000000u) != 0x06000000u) continue;
                if (setEnc.Contains(op.uTarget) || setS.Contains(op.uTarget) || !mapIl.ContainsKey(op.uTarget)) continue;
                setS.Add(op.uTarget);
                mpParent[op.uTarget] = (uM, op.iP, op.uOp);
                q.Enqueue(op.uTarget);
            }
            //指针创建点在就绪点之前归入延迟前序 之后按普通可达展开整段体
            foreach (var op in Ops(rgIl, rgIl.Length))
            {
                if (op.uOp != 0xFE06u && op.uOp != 0xFE07u) continue;
                if ((op.uTarget & 0xFF000000u) != 0x06000000u) continue;
                if (setEnc.Contains(op.uTarget) || !mapIl.ContainsKey(op.uTarget)) continue;
                if (op.iP < iEnd)
                {
                    if (setDeferPre.Add(op.uTarget)) qDefer.Enqueue(op.uTarget);
                }
                else if (!setS.Contains(op.uTarget) && !setDeferPre.Contains(op.uTarget))
                {
                    setS.Add(op.uTarget);
                    mpParent[op.uTarget] = (uM, op.iP, op.uOp);
                    q.Enqueue(op.uTarget);
                }
            }
        }
        while (qDefer.Count > 0)
        {
            uint uD = qDefer.Dequeue();
            foreach (var op in Ops(mapIl[uD], mapIl[uD].Length))
            {
                if (op.uOp != 0x28u && op.uOp != 0x6Fu && op.uOp != 0x73u) continue;
                if ((op.uTarget & 0xFF000000u) != 0x06000000u) continue;
                if (setEnc.Contains(op.uTarget) || setS.Contains(op.uTarget) || !mapIl.ContainsKey(op.uTarget)) continue;
                if (setDeferPre.Add(op.uTarget)) qDefer.Enqueue(op.uTarget);
            }
        }

        //就绪方法安装点之后的调用只能在就绪后
        var setPost = new HashSet<uint>();
        var qPost = new Queue<uint>();
        void SeedPost(uint uTok)
        {
            if (uTok == 0 || setEnc.Contains(uTok) || setS.Contains(uTok) || setPost.Contains(uTok) || !mapIl.ContainsKey(uTok)) return;
            setPost.Add(uTok);
            qPost.Enqueue(uTok);
        }
        foreach (var kv in mapIl)
        {
            if (!setS.Contains(kv.Key)) continue;
            byte[] rgIl = kv.Value;
            int iLim = LimitOf(kv.Key);
            if (iLim == int.MaxValue || iLim >= rgIl.Length) continue;
            foreach (var op in Ops(rgIl, rgIl.Length))
            {
                if (op.iP < iLim) continue;
                if (op.uOp != 0x28u && op.uOp != 0x6Fu && op.uOp != 0x73u) continue;
                if ((op.uTarget & 0xFF000000u) != 0x06000000u) continue;
                SeedPost(op.uTarget);
            }
        }
        while (qPost.Count > 0)
        {
            uint uP = qPost.Dequeue();
            foreach (var op in Ops(mapIl[uP], mapIl[uP].Length))
            {
                if (op.uOp != 0x28u && op.uOp != 0x6Fu && op.uOp != 0x73u) continue;
                if ((op.uTarget & 0xFF000000u) != 0x06000000u) continue;
                SeedPost(op.uTarget);
            }
        }

        int nViol = 0, nOk = 0, nGate = 0, nUnres = 0, nAll = 0, nDefer = 0, nNested = 0, nPost = 0;
        var rgViol = new List<string>();
        var rgGate = new List<string>();
        var rgWarn = new List<string>();
        var rgRes = new List<string>();
        foreach (var kv in mapIl)
        {
            if (setEnc.Contains(kv.Key)) continue;//加密体内部不算入口
            byte[] rgIl = kv.Value;
            bool bInS = setS.Contains(kv.Key);
            int iLim = bInS ? LimitOf(kv.Key) : -1;
            bool bDeferPre = setDeferPre.Contains(kv.Key);
            bool bPost = setPost.Contains(kv.Key);
            bool bNested = NameInsideEnc(mapName[kv.Key]);
            bool bLdftnEnc = false, bInvoke = false;
            var rgSites = new List<(int iOff, uint uCallee, uint uOp)>();
            foreach (var op in Ops(rgIl, rgIl.Length))
            {
                if (op.uOp == 0x6Fu && (op.uTarget & 0xFF000000u) == 0x0A000000u)
                {
                    string sMr = MemberRefName(mr, op.uTarget);
                    if (sMr == "Invoke" || sMr == "BeginInvoke") bInvoke = true;
                }
                if (op.uOp == 0x29u)
                {
                    nAll++;
                    if (bInS) { nUnres++; if (rgRes.Count < 6) rgRes.Add($"{mapName[kv.Key]}+0x{op.iP:X} -> calli"); }
                    continue;
                }
                if ((op.uTarget & 0xFF000000u) == 0x0A000000u)
                {
                    nAll++;
                    if (bInS) { nUnres++; if (rgRes.Count < 6) rgRes.Add($"{mapName[kv.Key]}+0x{op.iP:X} -> {MemberRefName(mr, op.uTarget)}"); }
                    continue;
                }
                if ((op.uTarget & 0xFF000000u) != 0x06000000u || !setEnc.Contains(op.uTarget)) continue;
                if (op.uOp == 0xFE06u || op.uOp == 0xFE07u)
                {
                    bLdftnEnc = true;
                    rgWarn.Add($"ldftn {mapName[kv.Key]}+0x{op.iP:X} -> {mapName[op.uTarget]} [{op.uTarget:X8}]");
                    continue;
                }
                rgSites.Add((op.iP, op.uTarget, op.uOp));
            }
            foreach (var st in rgSites)
            {
                string sSite = $"{mapName[kv.Key]}+0x{st.iOff:X} -> {mapName[st.uCallee]} [{st.uCallee:X8}]";
                if (!bInS)
                {
                    if (bNested) nNested++;
                    else if (bPost) nPost++;
                    else if (bDeferPre)
                    {
                        nDefer++;
                        if (rgGate.Count < 8) rgGate.Add($"defer-pre {sSite}");
                    }
                    else
                    {
                        nGate++;
                        if (rgGate.Count < 8) rgGate.Add(sSite);
                    }
                    continue;
                }
                if (st.iOff < iLim)
                {
                    nViol++;
                    rgViol.Add($"VIOLATION {OpName(st.uOp)} {sSite} path: {PathOf(mpParent, mapName, kv.Key)}");
                }
                else nOk++;
            }
            if (bInS && bLdftnEnc && bInvoke)
            {
                nViol++;
                rgViol.Add($"VIOLATION(delegate) {mapName[kv.Key]} ldftn of an encrypted body plus a delegate Invoke in one method, path: {PathOf(mpParent, mapName, kv.Key)}");
            }
        }

        string sAnchor = mapAnchor.Count > 0 ? $"install@{AnchorText(mapName, mapAnchor)}" : "<none>";
        string sReady = mapReady.Count > 0 ? $" ready@{AnchorText(mapName, mapReady)}" : "";
        Console.WriteLine($"  boot_order: anchor={sAnchor}{sReady} walked={setS.Count} encrypted={setEnc.Count} sites={nViol + nOk + nGate}");
        foreach (var s in rgWarn) Console.WriteLine($"  boot_order: warn (reference only, not a failure) {s}");
        foreach (var s in rgGate) Console.WriteLine($"  boot_order: gate {s}   (not in the pre-ready closure: start point before ready / thread / callback)");
        string sExtra = (nPost + nDefer + nNested + nGate) > 0 ? $"  (plus reachable only after ready {nPost} / defer-pre {nDefer} / lambda inside an encrypted body {nNested} / unordered {nGate})" : "";
        if (nOk > 0) Console.WriteLine($"  boot_order: ok {nOk} call sites into encrypted bodies sit after the ready point{sExtra}");
        string sUnres = rgRes.Count > 0 ? $" | {string.Join(" | ", rgRes)}" : "";
        if (nUnres > 0 || nAll > 0) Console.WriteLine($"  boot_order: unresolved in closure {nUnres} (whole module {nAll} calli/external MemberRef){sUnres}");
        if (nViol > 0)
        {
            foreach (var s in rgViol) Console.WriteLine($"  boot_order: {s}");
            throw new InvalidOperationException($"boot_order: encrypted body called before the jit hook is ready ({nViol} site(s)); first {rgViol[0]}");
        }
        Console.WriteLine("  boot_order: ok 0 violations");
    }

    private static uint FieldOf(byte[] rgD, string sName)
    {
        foreach (var kv in FieldTokens(rgD, new[] { sName })) return kv.Value;
        return 0;
    }

    private static string AnchorText(Dictionary<uint, string> mapName, Dictionary<uint, int> map)
    {
        var rg = new List<string>();
        foreach (var kv in map) rg.Add($"{(mapName.TryGetValue(kv.Key, out string? s) ? s : kv.Key.ToString("X8"))}+0x{kv.Value:X}");
        return string.Join(",", rg);
    }

    //调用者是加密体内编译器生成的匿名函数 进入过该体才会执行
    private static bool NameInsideEnc(string sName)
    {
        foreach (string s in BootNames.Encrypted)
        {
            if (sName.Contains($"<{s}>", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string MemberRefName(MetadataReader mr, uint uTok)
    {
        try
        {
            var h = MetadataTokens.MemberReferenceHandle((int)(uTok & 0x00FFFFFFu));
            return mr.GetString(mr.GetMemberReference(h).Name);
        }
        catch { return "?"; }
    }

    private static string OpName(uint uOp)
    {
        switch (uOp)
        {
            case 0x28u: return "call";
            case 0x6Fu: return "callvirt";
            case 0x73u: return "newobj";
            case 0xFE06u: return "ldftn";
            case 0xFE07u: return "ldvirtftn";
            default: return "0x" + uOp.ToString("X2");
        }
    }

    private static string PathOf(Dictionary<uint, (uint uCaller, int iOff, uint uOp)> mpParent, Dictionary<uint, string> mapName, uint uTok)
    {
        var rg = new List<string>();
        uint u = uTok;
        for (int i = 0; i < 12; i++)
        {
            string s = mapName.TryGetValue(u, out string? sName) ? sName : u.ToString("X8");
            if (!mpParent.TryGetValue(u, out var p)) { rg.Add(s); break; }
            rg.Add($"{s}+0x{p.iOff:X}");
            u = p.uCaller;
        }
        rg.Reverse();
        return string.Join(" > ", rg);
    }

    private static IEnumerable<(int iP, int iLen, uint uOp, uint uTarget)> Ops(byte[] rgIl, int iEnd)
    {
        for (int iP = 0; iP + 1 < iEnd;)
        {
            int l = IlLen(rgIl, iP);
            if (l <= 0) yield break;
            uint uOp = 0, uTarget = 0;
            byte b = rgIl[iP];
            if (b == 0x28 || b == 0x6F || b == 0x73 || b == 0x29) uOp = b;
            else if (b == 0xFE && (rgIl[iP + 1] == 0x06 || rgIl[iP + 1] == 0x07)) uOp = 0xFE00u | rgIl[iP + 1];
            if (uOp != 0 && iP + 4 < iEnd) uTarget = BitConverter.ToUInt32(rgIl, iP + 1);
            yield return (iP, l, uOp, uTarget);
            iP += l;
        }
    }

    #endregion
}
