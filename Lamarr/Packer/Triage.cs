namespace Lamarr.Packer;

//triage旋钮只在-p:LamarrTriageKnobs=1的构建里编入 发行打包器不带该属性 因此环境变量削弱不了防御
internal static class Triage
{
    internal static readonly string[] rgKnobs =//顺序即打印顺序
    {
        "LAMARR_TRIAGE_KNOBS", "LAMARR_NOSEAL", "LAMARR_NO_MUTATE", "LAMARR_NO_RENAME", "LAMARR_NO_IDX_PERM",
        "LAMARR_NO_ENGINE_SUM", "LAMARR_ENGINE_SUM_BAD", "LAMARR_NO_STUB_FP", "LAMARR_NO_STUB_SCRAMBLE",
        "LAMARR_NO_STUB_APISTR", "LAMARR_MOVE_INJECT", "LAMARR_MOVE_ONLY", "LAMARR_MOVE_NOP", "LAMARR_MOVE_NOPONLY",
        "LAMARR_IL_KILL", "LAMARR_EXP_TAMPER", "LAMARR_EXP_TAMPER2", "LAMARR_TRIAGE_SHIM",
        "LAMARR_LAYOUT", "LAMARR_DUMP_BOOT", "LAMARR_DUMP_BOOT_FINAL", "LAMARR_MORPH_TRACE", "LAMARR_VARIANT_SHIFT"
    };

#if LAMARR_TRIAGE
    internal static readonly bool bEnabled = true;
#else
    internal static readonly bool bEnabled = false;
#endif

    internal static string Flag() => bEnabled ? "1" : "0";

    internal static void Banner(string[] rgArgs)
    {
        bool bPackCmd = false;//横幅只在会产生产物的命令上打印 其余命令的stdout要留给脚本取首行的机器可读值
        for (int i = 0; i < rgArgs.Length; i++)
        {
            if (rgArgs[i].Equals("--pack", StringComparison.OrdinalIgnoreCase) || rgArgs[i].Equals("--replay", StringComparison.OrdinalIgnoreCase)) { bPackCmd = true; break; }
        }
        if (!bPackCmd) return;
        var rgSet = new List<string>();
        foreach (string s in rgKnobs)
        {
            string? sVal = Environment.GetEnvironmentVariable(s);
            if (!string.IsNullOrEmpty(sVal)) rgSet.Add(s + "=" + sVal);
        }
        if (rgSet.Count == 0) return;
        string sList = string.Join(", ", rgSet);
        Console.WriteLine(bEnabled//写stdout而不是stderr 否则把错误偏好设为Stop的脚本会中止
            ? $"[triage] WARNING triage build (LAMARR_TRIAGE): active knobs: {sList} -> artifacts from this run are NOT release artifacts"
            : $"[triage] NOTE release build (no LAMARR_TRIAGE): these knobs are IGNORED: {sList} (rebuild with -p:LamarrTriageKnobs=1 to use them)");
    }
}
