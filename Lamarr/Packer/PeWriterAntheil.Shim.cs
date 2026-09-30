namespace Lamarr.Packer;

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

internal partial class PeWriterAntheil
{
    private static readonly string[] rgShimForbidden =//入口层不该出现的字符串与开关名 命中即Debug构建或误配
    {
        "LAMARR_BOOT_LOG", "marker not found", "bad header offset", "bad entry count",
        "bad entry bounds", "bad entry tail", "entry check failed", "no entry point",
        "MyApplication.app"
    };

    private static readonly HashSet<string> rgShimKeepNames = new(StringComparer.Ordinal)//改名器按CLR约定跳过的名字 Main/.ctor/.cctor 不算残留
    {
        "Main", ".ctor", ".cctor"
    };

    private void PrepareShim()
    {
        if (rgShim == null) return;
        PeScrub.Scrub(rgShim);//时间戳/CheckSum/Rich/调试目录 按boot载荷内程序集同一规则处理
        var rgBefore = DescriptiveNames(rgShim);//改名前的描述性名字 与改名后集合求交才能判残留
        if (!bNoRename)
        {
            BootRenamer.SetSeed(uVmSeed);//boot改名由该种子派生
            rgShim = BootRenamer.Rename(rgShim);//不传词表 入口层没有需要长名掩护的校验方法
        }
        CheckShimHygiene(rgBefore);
    }

    private void CheckShimHygiene(HashSet<string> rgBefore)//改名后自查 不许残留原始描述性名字与调试/身份线索 命中即打包失败
    {
        if (bNoRename)
        {
            Console.WriteLine("[entrylayer] STATUS triage: rename disabled (LAMARR_NO_RENAME=1), hygiene checks skipped");
            return;
        }
        bool bTriageShim = Environment.GetEnvironmentVariable("LAMARR_TRIAGE_SHIM") == "1";//构建期二分用 带Debug特征 不参与发行
        string sText = Encoding.ASCII.GetString(rgShim!);
        string sUtf16 = Encoding.Unicode.GetString(rgShim!);
        foreach (string s in rgShimForbidden)
        {
            if (!sText.Contains(s, StringComparison.Ordinal) && !sUtf16.Contains(s, StringComparison.Ordinal)) continue;
            if (bTriageShim) { Console.WriteLine($"[entrylayer] WARN triage: residue '{s}' kept (LAMARR_TRIAGE_SHIM=1)"); continue; }
            throw new InvalidOperationException($"entry layer residue: '{s}' (rebuild the entry layer in Release; see Native\\ShimAntheil)");
        }
        var rgAfter = new HashSet<string>(DescriptiveNames(rgShim!), StringComparer.Ordinal);
        var rgLeft = rgBefore.Where(s => rgAfter.Contains(s)).ToList();
        if (rgLeft.Count > 0)
        {
            string sLeft = string.Join(", ", rgLeft.Distinct());//构建期二分用 列全部候选而不直接失败
            if (bTriageShim)
            {
                Console.WriteLine($"[entrylayer] WARN triage: kept descriptive names: {sLeft}");
            }
            else
            {
                throw new InvalidOperationException(
                    $@"entry layer kept descriptive names (the renamer cannot touch a name whose #Strings span is shared with a framework name): {sLeft} -- shorten those identifiers in the entry layer source (see Native\ShimAntheil), or add them to rgShimKeepNames here with a reason");
            }
        }
        var rgNames = ReadMetaNames(rgShim!, false);
        rgNames.Sort(StringComparer.Ordinal);
        ulong uDigest = 0xCBF29CE484222325UL;
        foreach (string s in rgNames)
        {
            foreach (char ch in s) { uDigest ^= ch; uDigest *= 0x100000001B3UL; }
            uDigest ^= 0x2D;
            uDigest *= 0x100000001B3UL;
        }
        Console.WriteLine($"[entrylayer] hygiene ok: names={rgNames.Count} digest={uDigest:X16} ts={PeScrub.TimeStamp(rgShim!)} size={rgShim!.Length}");
    }

    private static HashSet<string> DescriptiveNames(byte[] rg)//描述性名字 >2字符且不在改名器跳过表内
    {
        var rgOut = new HashSet<string>(StringComparer.Ordinal);
        foreach (string s in ReadMetaNames(rg, false))
        {
            if (s.Length > 2 && !rgShimKeepNames.Contains(s)) rgOut.Add(s);
        }
        return rgOut;
    }

    internal static void DumpNames(string sPath)//列出清理检查要看的名字集合 定位改名器不改的标识符
    {
        var rg = new List<string>(DescriptiveNames(File.ReadAllBytes(sPath)));
        rg.Sort(StringComparer.Ordinal);
        foreach (string s in rg) Console.WriteLine(s);
    }

    private static List<string> ReadMetaNames(byte[] rg, bool bDescriptiveOnly = true)//类型/方法/字段/参数名 关闭bDescriptiveOnly时含1-2字符名 命名空间一并随机化
    {
        var rgOut = new List<string>();
        using var ms = new MemoryStream(rg, writable: false);
        using var per = new PEReader(ms);
        var mr = per.GetMetadataReader();
        void Add(StringHandle h)
        {
            if (h.IsNil) return;
            string s = mr.GetString(h);
            if (!bDescriptiveOnly || s.Length > 2) rgOut.Add(s);
        }
        foreach (var h in mr.TypeDefinitions) Add(mr.GetTypeDefinition(h).Name);
        foreach (var h in mr.MethodDefinitions)
        {
            var md = mr.GetMethodDefinition(h);
            Add(md.Name);
            foreach (var ph in md.GetParameters()) Add(mr.GetParameter(ph).Name);
        }
        foreach (var h in mr.FieldDefinitions) Add(mr.GetFieldDefinition(h).Name);
        return rgOut;
    }
}
