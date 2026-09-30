namespace Lamarr.Packer;

using A0;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal partial class PeWriterAntheil
{
    private static readonly string[] rgExpFields = { "uLK0A", "uLK0B", "uLK1A", "uLK1B", "uHs" };//改名后按名取就晚了 这些输入在LoadBoot按原名一次采齐
    private uint uExpK0, uExpK1, uExpHs;
    private uint uExpTokAd, uExpTokX1, uExpTokX3, uExpTokX6;

    private void CaptureExpInputs(byte[] rgD)//常量对改写保持异或不变 改名前读到的密钥仍有效
    {
        var rgTok = FieldTokens(rgD, rgExpFields);
        foreach (var sName in rgExpFields) if (!rgTok.ContainsKey(sName)) throw new InvalidOperationException($"boot field missing: {sName}");
        uExpK0 = CctorImm(rgD, rgTok["uLK0A"]) ^ CctorImm(rgD, rgTok["uLK0B"]);
        uExpK1 = CctorImm(rgD, rgTok["uLK1A"]) ^ CctorImm(rgD, rgTok["uLK1B"]);
        uExpHs = CctorImm(rgD, rgTok["uHs"]);
        uExpTokAd = MethodToken(rgD, "AD");
        uExpTokX1 = MethodToken(rgD, "X1");
        uExpTokX3 = MethodToken(rgD, "X3");
        uExpTokX6 = MethodToken(rgD, "X6");
    }

    private static uint CctorImm(byte[] rgD, uint uTok)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) != ".cctor") continue;
            byte[]? rgIl = per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
            if (rgIl == null) continue;
            for (int iP = 0; iP + 5 < rgIl.Length; iP++)
            {
                if (rgIl[iP] != 0x80 && rgIl[iP] != 0x7D) continue;
                if (BitConverter.ToUInt32(rgIl, iP + 1) != uTok) continue;
                if (iP >= 5 && rgIl[iP - 5] == 0x20) return BitConverter.ToUInt32(rgIl, iP - 4);
                if (iP >= 2 && rgIl[iP - 2] == 0x1F) return (uint)(sbyte)rgIl[iP - 1];
                if (iP >= 1 && rgIl[iP - 1] >= 0x16 && rgIl[iP - 1] <= 0x1E) return (uint)(rgIl[iP - 1] - 0x16);
            }
        }
        throw new InvalidOperationException($"cctor immediate missing token=0x{uTok:X8}");
    }

    private static int ExpTamperSlot()//LAMARR_EXP_TAMPER sentinel表示不写数字表示把该槽位异或1
    {
        string? sTamper = Environment.GetEnvironmentVariable("LAMARR_EXP_TAMPER");
        if (string.IsNullOrEmpty(sTamper)) return -2;
        int iSlot;
        if (sTamper.Equals("sentinel", StringComparison.OrdinalIgnoreCase)) iSlot = -1;//槽位块保持哨兵
        else if (int.TryParse(sTamper, out int iT) && iT >= 0 && iT < BootExp.SlotCount) iSlot = iT;//该槽位异或1
        else throw new InvalidOperationException($"LAMARR_EXP_TAMPER invalid: {sTamper}");
        Console.WriteLine(iSlot < 0
            ? "[exp] TAMPER sentinel: write skipped (build regression only)"
            : $"[exp] TAMPER slot={iSlot}: one slot corrupted (build regression only)");
        return iSlot;
    }

    private void WriteBootExpectations(byte[] rgD)
    {
        uint uK0 = uExpK0, uK1 = uExpK1, uHs = uExpHs;//输入已按原名采好 这里只用令牌 改名与搬移都不影响
        if (uK0 == 0 || uK1 == 0 || uHs == 0) throw new InvalidOperationException("expectation inputs not captured (LoadBoot must run the BootExp contract)");

        uint uAd = IlHash(rgD, uExpTokAd, uK0, uK1);//原始值共四项 含方法体摘要与两个text摘要与三项已知答案
        uint uX1 = IlHash(rgD, uExpTokX1, uK0, uK1);
        uint uX3 = IlHash(rgD, uExpTokX3, uK0, uK1);
        uint uX6 = IlHash(rgD, uExpTokX6, uK0, uK1);
        uint uFanRaw = uJitSum ^ uDecSum ^ uAd ^ uX1 ^ uX3 ^ uX6
                     ^ BootExp.RawKatA ^ BootExp.RawKatB ^ BootExp.RawKatC;

        int iTamper = ExpTamperSlot();

        var rgSlot = new uint[BootExp.SlotCount];
        rgSlot[(int)ExpSlot.Jit] = uJitSum;
        rgSlot[(int)ExpSlot.Dec] = uDecSum;
        rgSlot[(int)ExpSlot.Ad] = uHs ^ uAd;
        rgSlot[(int)ExpSlot.X1] = uHs ^ uX1;
        rgSlot[(int)ExpSlot.X3] = uHs ^ uX3;
        rgSlot[(int)ExpSlot.X6] = uHs ^ uX6;
        rgSlot[(int)ExpSlot.KatA] = uHs ^ BootExp.RawKatA;
        rgSlot[(int)ExpSlot.KatB] = uHs ^ BootExp.RawKatB;
        rgSlot[(int)ExpSlot.KatC] = uHs ^ BootExp.RawKatC;
        rgSlot[(int)ExpSlot.Fan] = uHs ^ uFanRaw;

        int iAt = FindSentinel(rgD);
        if (iTamper == -1)
        {
            Console.WriteLine($"[exp] TAMPER: sentinel kept @0x{iAt:X}");//故意不写哨兵留在产物里
        }
        else
        {
            if (iTamper >= 0) rgSlot[iTamper] ^= 1u;
            for (int i = 0; i < BootExp.SlotCount; i++) BitConverter.GetBytes(rgSlot[i]).CopyTo(rgD, iAt + i * 4);
            for (int i = 0; i < BootExp.SlotCount; i++)//回读覆盖位置 写错槽位或漏写都在这里暴露
                if (BitConverter.ToUInt32(rgD, iAt + i * 4) != rgSlot[i]) throw new InvalidOperationException($"expectation readback mismatch slot={i}");
        }
        Console.WriteLine($"  exp: {BootExp.SlotCount} slots @0x{iAt:X} jit=0x{uJitSum:X8} dec=0x{uDecSum:X8} fan=0x{rgSlot[(int)ExpSlot.Fan]:X8}");
    }

    private static uint IlHash(byte[] rgD, uint uTok, uint uK0, uint uK1)//方法体摘要逐字节异或乘 量的是镜像里的最终IL
    {
        byte[] rgIl = MethodIlByToken(rgD, uTok) ?? throw new InvalidOperationException($"boot method body missing token=0x{uTok:X8}");
        return IlHash(rgIl, uK0, uK1);
    }

    private static uint IlHash(byte[] rgIl, uint uK0, uint uK1)
    {
        uint uHash = uK0;
        foreach (byte b in rgIl) { uHash ^= b; uHash *= uK1; }
        return uHash;
    }

    private static uint MethodToken(byte[] rgD, string sName)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            if (mr.GetString(md.Name) == sName) return (uint)MetadataTokens.GetToken(h);
        }
        throw new InvalidOperationException($"boot method missing: {sName}");
    }

    private static byte[]? MethodIlByToken(byte[] rgD, uint uTok)
    {
        using var ms = new MemoryStream(rgD, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        foreach (var h in mr.MethodDefinitions)
        {
            if ((uint)MetadataTokens.GetToken(h) != uTok) continue;
            var md = mr.GetMethodDefinition(h);
            return per.GetMethodBody(md.RelativeVirtualAddress)?.GetILBytes();
        }
        return null;
    }

    private static int FindSentinel(byte[] rgD)//占位块整段哨兵 命中数必须为1 否则定位不可信
    {
        int iAt = -1;
        for (int i = 0; i + BootExp.BlobBytes <= rgD.Length; i++)
        {
            if (rgD[i] != BootExp.Sentinel) continue;
            bool bOk = true;
            for (int k = 1; k < BootExp.BlobBytes; k++) if (rgD[i + k] != BootExp.Sentinel) { bOk = false; break; }
            if (!bOk) continue;
            if (iAt >= 0) throw new InvalidOperationException("expectation placeholder block found twice (location untrustworthy)");
            iAt = i;
        }
        if (iAt < 0) throw new InvalidOperationException("expectation placeholder block not found (boot not built from the BootExp contract)");
        return iAt;
    }
}
