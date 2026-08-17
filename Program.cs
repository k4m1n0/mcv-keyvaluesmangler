using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Services;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc;

internal static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AllocConsole();
    [DllImport("kernel32.dll")]
    static extern bool FreeConsole();

    const int iOk = 0;
    const int iErrUsage = 1;
    const int iErrLogin = 2;
    const int iErrPageNotFound = 3;
    const int iErrUnknownCmd = 4;
    const int iErrException = 5;

    [STAThread]
    static int Main(string[] rgArgs)
    {
        var rgCliArgs = new List<string>();
        string? sLogLevelArg = null;
        for (int i = 0; i < rgArgs.Length; i++)
        {
            if (rgArgs[i].Equals("--darkmode", StringComparison.OrdinalIgnoreCase))
                Form1.bForceDarkMode = true;
            else if (rgArgs[i].Equals("--lightmode", StringComparison.OrdinalIgnoreCase))
                Form1.bForceLightMode = true;
            else if (rgArgs[i].Equals("--log-level", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < rgArgs.Length && !rgArgs[i + 1].StartsWith("--"))
                {
                    sLogLevelArg = rgArgs[i + 1];
                    i++;
                }
                continue;
            }
            else
                rgCliArgs.Add(rgArgs[i]);
        }
        var rgCliArgsArr = rgCliArgs.ToArray();

        LogService.Enabled = true;
        LogService.MinLevel = ResolveLogLevel(sLogLevelArg, rgCliArgsArr.Length > 0);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                ?? new Exception($"Non-Exception: {e.ExceptionObject?.GetType().Name ?? "null"}");
            try { LogService.Fatal(ex, "UnhandledException"); }
            catch { }
            Environment.Exit(iErrException);
        };

        string sCurrentDir = AppContext.BaseDirectory;
        string sMutexName = @"WeaponDamageCalc_" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(sCurrentDir.ToLowerInvariant())))[..16];
        using var hMutex = new Mutex(true, sMutexName, out bool bCreatedNew);
        if (!bCreatedNew)
        {
            LogService.Warn("Mutex locked - another instance is already running in this folder");
            if (rgCliArgsArr.Length == 0)
                MessageBox.Show("Only one instance of the same folder can be running at one time.",
                    "Mangler - Warning", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        if (rgCliArgsArr.Length > 0)
        {
            return RunCliMode(rgCliArgsArr, LogService.MinLevel);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            var ex = e.Exception;
            if (ex is TypeLoadException tle &&
                tle.StackTrace?.Contains("ReleaseUiaProvider", StringComparison.OrdinalIgnoreCase) == true)
            {
                LogService.DebugDebounce("uia_flood", $"UIA exception ignored (expected in trimmed build): {tle.Message}");
                if (Application.OpenForms.Count == 0) Environment.Exit(0);
                return;
            }
            LogService.Fatal(ex, "UI ThreadException");
            Application.Exit();
        };
        Application.Run(new Form1());
        return 0;
    }

    static LogService.Level ResolveLogLevel(string? sLogLevelArg, bool bCliMode)
    {
        if (!string.IsNullOrEmpty(sLogLevelArg))
            return ParseLogLevel(sLogLevelArg);
        if (bCliMode)
            return LogService.Level.Info;
    #if DEBUG
        return LogService.Level.Debug;
    #else
        return LogService.Level.Warn;
    #endif
    }

    static LogService.Level ParseLogLevel(string? sLevel) =>
        sLevel?.ToLowerInvariant() switch
        {
            "debug" => LogService.Level.Debug,
            "info"  => LogService.Level.Info,
            "warn"  => LogService.Level.Warn,
            "error" => LogService.Level.Error,
            "fatal" => LogService.Level.Fatal,//bruh
            _       => LogService.Level.Debug
        };

    static int RunCliMode(string[] rgArgs, LogService.Level lvlLog)
    {
        LogService.Enabled = true;
        LogService.MinLevel = lvlLog;
        AllocConsole();
        LogService.Info($"CLI started: {string.Join(" ", rgArgs)} (log level: {lvlLog})");
        int iCode = Task.Run(() => RunCli(rgArgs)).GetAwaiter().GetResult();
        Console.Out.Flush();
        if (!Console.IsOutputRedirected && !Console.IsInputRedirected
            && (rgArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase)
            || rgArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase)
            || rgArgs[0].Equals("/?", StringComparison.OrdinalIgnoreCase)
            || rgArgs[0].Equals("--fuckyou", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        FreeConsole();
        return iCode;
    }

    static void ShowHelp()
    {
        string sExeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "WeaponDamageCalc.exe");
        Console.WriteLine($@"
Keyvalues Mangler(TM) 5000 - MCV Weapon Stats Tool

Usage:
  {sExeName}.exe [command] [options]
  {sExeName}.exe [--darkmode|--lightmode] [--log-level <level>]

  Without arguments, launches the GUI
  --darkmode   Force dark color scheme
  --lightmode  Force light color scheme

Global Options:
  --log-level <debug|info|warn|error>
      Minimum log level written to .\mangler.log
      (default: warn for GUI, info for CLI;
       debug when --log-level is passed without a value)
      DEBUG  Log everything including control value changes and hotkeys
      INFO   Log operations (save, export, import, wiki actions)
      WARN   Log warnings and errors only
             (missing files, failed operations)
      ERROR  Log errors and fatal events only
      FATAL  No need to specify, this program gets fucked
      The log file auto rotates at 5 MiB
      Warn/Error entries include source location in Debug builds
      In CLI mode, --verbose prints progress to console
      the log file independently follows --log-level

Commands:

  --help, -h, /?
      Show this help

  --csv-to-scripts <csv> <dir>
      Export CSV weapon data to script files in <dir>

  --scripts-to-csv <dir> [csv]
      Import script files from <dir> into CSV
      Default: .\weapons.csv

  --convert-templates <dir> [--simple]
      Convert old weapon scripts to preset_file template format
      --simple  Skip empty keys and compress blank lines

  --wiki-dryrun <page> <scripts_dir> [--single] [--verbose]
      Fetch <page> from wiki, convert with scripts,
      save to .\wiki\
      --single   Treat <page> as a single weapon page
                 (not a summary)

  --wiki-upload <page> <scripts_dir> --user <u> --pw <p>
      [--single] [--verbose]
      Fetch, convert, and upload <page> to wiki
      Requires login

  --batch-dryrun <summary_page> <scripts_dir>
      [--verbose] [--skip-cached]
      Batch convert all weapons linked from <summary_page>
      --skip-cached  Skip pages already saved in .\wiki\

  --batch-upload <summary_page> <scripts_dir>
      --user <u> --pw <p> [--verbose]
      Batch upload all weapons linked from <summary_page>

  --generate <scripts_dir> [output_dir]
      [--include-existing] [--check-wiki] [--verbose]
      Generate wiki weapon pages from game scripts
      and resource files
      Default output: .\generated\
      --include-existing  Overwrite even if wiki pages
                          already exist
      --check-wiki        Query wiki API to skip
                          existing pages

Return codes:
  0  Success   1  Usage error   2  Login failed
  3  Page not found   4  Unknown command   5  Internal error

Examples:
  {sExeName}.exe --log-level debug
  {sExeName}.exe --csv-to-scripts weapons.csv
      ""X:\...\vietnam\scripts""
  {sExeName}.exe --wiki-dryrun ""Weapons of Vietnam""
      ""X:\...\vietnam\scripts"" --verbose
  {sExeName}.exe --wiki-upload ""AK-47""
      ""X:\...\vietnam\scripts"" --user user --pw pass
      --single
  {sExeName}.exe --batch-dryrun ""Weapons of Vietnam""
      ""X:\...\vietnam\scripts"" --skip-cached
  {sExeName}.exe --generate ""X:\...\vietnam\scripts""
      ""X:\output"" --check-wiki
  {sExeName}.exe --convert-templates
      ""X:\...\vietnam\scripts"" --simple
  {sExeName}.exe --scripts-to-csv
      ""X:\...\vietnam\scripts""
");
    }

    static bool HasFlag(string[] rgArgs, string sFlag) =>
        Array.Exists(rgArgs, sA => sA.Equals(sFlag, StringComparison.OrdinalIgnoreCase));

    //按索引取位置参数 越界返回null
    static string? Arg(string[] rgArgs, int i) => i < rgArgs.Length ? rgArgs[i] : null;

    //取命名参数的值 如--user xxx返回xxx
    static string? Opt(string[] rgArgs, string sName)
    {
        int iIdx = Array.FindIndex(rgArgs, sA => sA.Equals(sName, StringComparison.OrdinalIgnoreCase));
        if (iIdx < 0 || iIdx + 1 >= rgArgs.Length) return null;
        string sVal = rgArgs[iIdx + 1];
        return sVal.StartsWith("--") ? null : sVal;//value不能是--开头的另一个参数
    }

    static void Verbose(string sMsg, bool bVerbose)
    {
        if (bVerbose) Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {sMsg}");
    }

    //提取wiki-dryrun和wiki-upload的公共逻辑：获取+反查+转换
    static async Task<(string sPage, string? sResult, int iCode)> FetchAndConvertAsync(
        string sPage, string sScripts, bool bSingle, bool bVerbose)
    {
        Verbose($"Fetching: {sPage}", bVerbose);
        var sSource = await WikiApiService.GetPageSourceAsync(sPage);
        if (sSource == null)
        {
            //反查脚本名
            var mpIdx = await WikiService.BuildScriptIndexAsync();
            string? sFound = WikiService.ReverseLookup(sPage, mpIdx);
            if (sFound != null) { sPage = sFound; sSource = await WikiApiService.GetPageSourceAsync(sFound); }
        }
        if (sSource == null) { Console.WriteLine($"Page not found: {sPage}"); return (sPage, null, iErrPageNotFound); }
        Verbose("Converting...", bVerbose);
        var sResult = bSingle ? WikiTableConverter.Convert(sSource, sScripts) : WikiService.ConvertWikiSource(sSource, sScripts, null);
        return (sPage, sResult, iOk);
    }

    static async Task<(List<string>? rgLinks, int iCode)> FetchWeaponLinksAsync(
        string sSummaryPage, bool bVerbose)
    {
        Verbose($"Fetching summary: {sSummaryPage}", bVerbose);
        var sSource = await WikiApiService.GetPageSourceAsync(sSummaryPage);
        if (sSource == null) { Console.WriteLine($"Page not found: {sSummaryPage}"); return (null, iErrPageNotFound); }
        Verbose("Building script index...", bVerbose);
        var mpIndex = await WikiService.BuildScriptIndexAsync();
        if (mpIndex == null) LogService.Warn("FetchWeaponLinksAsync: script index unavailable");
        var rgLinks = WikiService.ExtractWeaponLinks(sSource, mpIndex);
        if (rgLinks.Count == 0) { Console.WriteLine("No weapon links found"); return (null, iOk); }
        return (rgLinks, iOk);
    }

    static async Task<int> RunCli(string[] rgArgs)
    {
        var sCmd = rgArgs[0].ToLowerInvariant();
        bool bVerbose = HasFlag(rgArgs, "--verbose");
        LogService.Info($"CLI command: {sCmd}, verbose={bVerbose}");

        try
        {
            switch (sCmd)
            {
                case "--fuckyou":
                    Console.WriteLine("FUCK YOU TOO");
                    return iOk;
                case "--help":
                case "-h":
                case "/?":
                    ShowHelp();
                    return iOk;

                case "--csv-to-scripts":
                {
                    var sCsv = Arg(rgArgs, 1) ?? "weapons.csv";
                    var sDir = Arg(rgArgs, 2) ?? ".";
                    Console.WriteLine($"CSV -> Scripts: {sCsv} -> {sDir}");
                    var sLog = WeaponScriptService.ExportCsvToScripts(sCsv, sDir);
                    Console.WriteLine(sLog);
                    return iOk;
                }
                case "--scripts-to-csv":
                {
                    var sDir = Arg(rgArgs, 1) ?? ".";
                    var sCsv = Arg(rgArgs, 2) ?? "weapons.csv";
                    Console.WriteLine($"Scripts -> CSV: {sDir} -> {sCsv}");
                    var sLog = WeaponScriptService.ImportScriptsToCsv(sDir, sCsv);
                    Console.WriteLine(sLog);
                    return iOk;
                }
                case "--convert-templates":
                {
                    var sDir = Arg(rgArgs, 1) ?? ".";
                    var bSimple = HasFlag(rgArgs, "--simple");
                    Console.WriteLine($"Convert templates: {sDir} (simple={bSimple})");
                    var sLog = ScriptToTemplateConverter.ConvertAll(sDir, bSimple);
                    Console.WriteLine(sLog);
                    return iOk;
                }
                case "--wiki-dryrun":
                {
                    var sPage = Arg(rgArgs, 1);
                    var sScripts = Arg(rgArgs, 2) ?? ".";
                    bool bSingle = HasFlag(rgArgs, "--single");
                    if (sPage == null) { Console.WriteLine("Usage: --wiki-dryrun <page> <scripts_dir> [--single] [--verbose]"); return iErrUsage; }
                    var (sResolvedPage, sResult, iCode) = await FetchAndConvertAsync(sPage, sScripts, bSingle, bVerbose);
                    if (iCode != iOk || sResult == null) return iCode;
                    string sFn = sResolvedPage.Replace(" ", "_").Replace("/", "_") + ".txt";
                    WikiService.SaveToWikiDir(sFn, sResult);
                    Console.WriteLine($"Saved: {WikiService.GetWikiDir()}\\{sFn}  ({sResult.Split('\n').Length} lines)");
                    return iOk;
                }
                case "--wiki-upload":
                {
                    var sPage = Arg(rgArgs, 1);
                    var sScripts = Arg(rgArgs, 2) ?? ".";
                    var sUser = Opt(rgArgs, "--user");
                    var sPw = Opt(rgArgs, "--pw");
                    bool bSingle = HasFlag(rgArgs, "--single");
                    if (sPage == null || sUser == null || sPw == null) { Console.WriteLine("Usage: --wiki-upload <page> <scripts_dir> --user <u> --pw <p> [--single] [--verbose]"); return iErrUsage; }
                    Verbose("Logging in...", bVerbose);
                    if (!await WikiService.LoginAsync(sUser, sPw)) { Console.WriteLine("Login failed"); return iErrLogin; }
                    var (sResolvedPage, sResult, iCode) = await FetchAndConvertAsync(sPage, sScripts, bSingle, bVerbose);
                    if (iCode != iOk || sResult == null) return iCode;
                    if (await WikiApiService.IsSameContentAsync(sResolvedPage, sResult)) { Console.WriteLine("Unchanged, skip"); return iOk; }
                    Verbose("Uploading...", bVerbose);
                    bool bOk = await WikiApiService.SavePageAsync(sResolvedPage, sResult, "Update weapon data from scripts");
                    Console.WriteLine(bOk ? "Saved!" : "Save failed");
                    return bOk ? iOk : iErrException;
                }
                case "--batch-dryrun":
                {
                    var sPage = Arg(rgArgs, 1);
                    var sScripts = Arg(rgArgs, 2) ?? ".";
                    bool bSkipCached = HasFlag(rgArgs, "--skip-cached");
                    if (sPage == null) { Console.WriteLine("Usage: --batch-dryrun <summary_page> <scripts_dir> [--verbose] [--skip-cached]"); return iErrUsage; }
                    return await RunBatchDryrun(sPage, sScripts, bSkipCached, bVerbose);
                }
                case "--batch-upload":
                {
                    var sPage = Arg(rgArgs, 1);
                    var sScripts = Arg(rgArgs, 2) ?? ".";
                    var sUser = Opt(rgArgs, "--user");
                    var sPw = Opt(rgArgs, "--pw");
                    if (sPage == null || sUser == null || sPw == null) { Console.WriteLine("Usage: --batch-upload <summary_page> <scripts_dir> --user <u> --pw <p> [--verbose]"); return iErrUsage; }
                    Verbose("Logging in...", bVerbose);
                    if (!await WikiService.LoginAsync(sUser, sPw)) { Console.WriteLine("Login failed"); return iErrLogin; }
                    return await RunBatchUpload(sPage, sScripts, bVerbose);
                }
                case "--generate":
                {
                    var sScripts = Arg(rgArgs, 1) ?? ".";
                    var sOutput = Arg(rgArgs, 2) ?? "generated";
                    bool bIncludeExisting = HasFlag(rgArgs, "--include-existing");
                    bool bCheckWiki = HasFlag(rgArgs, "--check-wiki");
                    return await RunGenerate(sScripts, sOutput, bIncludeExisting, bCheckWiki, bVerbose);
                }
                default:
                    Console.WriteLine($"Unknown command: {sCmd}");
                    Console.WriteLine("Use --help for usage info.");
                    return iErrUnknownCmd;
            }
        }
        catch (Exception ex)
        {
            LogService.Fatal(ex, $"RunCli: {sCmd}");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return iErrException;
        }
    }

    static async Task<int> RunBatchDryrun(string sSummaryPage, string sScriptsDir, bool bSkipCached, bool bVerbose)
    {
        var (rgLinks, iCode) = await FetchWeaponLinksAsync(sSummaryPage, bVerbose);
        if (rgLinks == null) return iCode;

        string sWikiDir = WikiService.GetWikiDir();
        Directory.CreateDirectory(sWikiDir);
        int iDone = 0, iFail = 0, iSkipped = 0;

        Console.WriteLine($"Batch DryRun: {rgLinks.Count} pages{(bSkipCached ? " [skip cached]" : "")}");
        Console.WriteLine(new string('-', 40));

        for (int i = 0; i < rgLinks.Count; i++)
        {
            string sLink = rgLinks[i];
            Verbose($"[{i + 1}/{rgLinks.Count}] {sLink}", bVerbose);
            string sFn = sLink.Replace(" ", "_").Replace("/", "_") + ".txt";
            string sFp = Path.Combine(sWikiDir, sFn);

            if (bSkipCached && File.Exists(sFp)) { iSkipped++; Console.WriteLine($"SKIP (cached)  {sLink}"); continue; }

            try
            {
                var sSrc = await WikiApiService.GetPageSourceAsync(sLink);
                if (sSrc == null) { iFail++; Console.WriteLine($"FAIL fetch: {sLink}"); }
                else
                {
                    //单个武器详情页走Convert
                    string sConverted = WikiTableConverter.Convert(sSrc, sScriptsDir);
                    File.WriteAllText(sFp, sConverted);
                    iDone++;
                    Console.WriteLine($"OK  {sLink}");
                }
            }
            catch (Exception ex)
            {
                iFail++;
                Console.WriteLine($"ERR {sLink}: {ex.Message}");
                LogService.Error(ex, $"RunBatchDryrun: {sLink}");
            }
        }

        Console.WriteLine(new string('-', 40));
        string sCachedInfo = iSkipped > 0 ? $", {iSkipped} cached" : "";
        Console.WriteLine($"Done: {iDone} ok, {iFail} fail{sCachedInfo}");
        LogService.Info($"RunBatchDryrun done: {iDone} ok, {iFail} fail, {iSkipped} cached");
        return iFail > 0 ? iErrException : iOk;
    }

    static async Task<int> RunBatchUpload(string sSummaryPage, string sScriptsDir, bool bVerbose)
    {
        var (rgLinks, iCode) = await FetchWeaponLinksAsync(sSummaryPage, bVerbose);
        if (rgLinks == null) return iCode;

        string sWikiDir = WikiService.GetWikiDir();
        if (!Directory.Exists(sWikiDir)) { Console.WriteLine("No wiki folder found. Run --batch-dryrun first."); return iErrUsage; }

        int iDone = 0, iFail = 0, iSkip = 0;
        Console.WriteLine($"Batch Upload: {rgLinks.Count} pages");
        Console.WriteLine(new string('-', 40));

        for (int i = 0; i < rgLinks.Count; i++)
        {
            string sLink = rgLinks[i];
            Verbose($"[{i + 1}/{rgLinks.Count}] {sLink}", bVerbose);
            string sFp = Path.Combine(sWikiDir, sLink.Replace(" ", "_").Replace("/", "_") + ".txt");

            if (!File.Exists(sFp)) { iSkip++; Console.WriteLine($"SKIP no file: {sLink}"); continue; }
            string sContent = File.ReadAllText(sFp);
            if (await WikiApiService.IsSameContentAsync(sLink, sContent)) { iSkip++; Console.WriteLine($"SKIP unchanged: {sLink}"); continue; }

            try
            {
                bool bOk = await WikiApiService.SavePageAsync(sLink, sContent, "Update weapon data from scripts");
                if (bOk) { iDone++; Console.WriteLine($"OK  {sLink}"); }
                else { iFail++; Console.WriteLine($"FAIL upload: {sLink}"); }
            }
            catch (Exception ex)
            {
                iFail++;
                Console.WriteLine($"ERR {sLink}: {ex.Message}");
                LogService.Error(ex, $"RunBatchUpload: {sLink}");
            }
        }

        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"Done: {iDone} ok, {iFail} fail, {iSkip} skip");
        LogService.Info($"RunBatchUpload done: {iDone} ok, {iFail} fail, {iSkip} skip");
        return iFail > 0 ? iErrException : iOk;
    }

    static async Task<int> RunGenerate(string sScriptsDir, string sOutputDir, bool bIncludeExisting, bool bCheckWiki, bool bVerbose)
    {
        Console.WriteLine($"Generate: {sScriptsDir} -> {sOutputDir}");

        //scripts/../resource
        string sResourceDir = LoadoutService.GetResourceDir(sScriptsDir);
        if (!Directory.Exists(sResourceDir)) { Console.WriteLine($"Resource folder not found: {sResourceDir}"); return iErrUsage; }

        Verbose("Loading tokens...", bVerbose);
        var mpTokens = LocalizationService.LoadTokens(Path.Combine(sResourceDir, "vietnam_english.txt"));
        Verbose($"Tokens: {mpTokens.Count}", bVerbose);

        Verbose("Loading loadout...", bVerbose);
        var mpLoadout = LoadoutService.LoadAll(sResourceDir);
        Verbose($"Loadout: {mpLoadout.Count}", bVerbose);

        Verbose("Fetching templates...", bVerbose);
        string sDefaultTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.sDefaultTemplateUrl) ?? "";
        string sLmgTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.sLmgTemplateUrl) ?? sDefaultTemplate;
        string sPistolTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.sPistolTemplateUrl) ?? sDefaultTemplate;
        string sShortTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.sShortTemplateUrl) ?? "";

        //构建索引用于token查找的fallback和wiki查重
        Verbose("Building script index...", bVerbose);
        Dictionary<string, string>? mpIndex = await WikiService.BuildScriptIndexAsync();
        if (mpIndex == null)
            LogService.Warn("RunGenerate: script index unavailable, title mapping may be inaccurate");
        var mpScriptToTitle = WikiService.BuildScriptToTitleIndex(mpIndex);
        Verbose("Generating pages...", bVerbose);
        var rgGenerated = WikiPageGenerator.GenerateAll(sScriptsDir, sResourceDir, mpTokens, mpLoadout,
            sDefaultTemplate, sLmgTemplate, sPistolTemplate, sShortTemplate, new HashSet<string>(), mpIndex);
        Verbose($"Generated: {rgGenerated.Count} pages", bVerbose);

        //获取wiki真实标题 否则用生成器标题
        string GetWikiTitle(WikiPageGenerator.GeneratedPage gpPage)
        {
            if (mpScriptToTitle.TryGetValue(gpPage.ScriptName, out string? sTitle) && !string.IsNullOrEmpty(sTitle))
                return sTitle;
            return gpPage.Title;
        }

        //--check-wiki时批量查询已存在页面
        HashSet<string> hsExisting = new();
        if (bCheckWiki)
        {
            Verbose("Checking wiki for existing pages...", bVerbose);
            var rgTitles = rgGenerated.Select(GetWikiTitle).ToList();
            hsExisting = await WikiApiService.GetExistingTitlesAsync(rgTitles);
            Verbose($"Existing on wiki: {hsExisting.Count}", bVerbose);
        }

        Directory.CreateDirectory(sOutputDir);
        int iWritten = 0;
        foreach (var gpPage in rgGenerated)
        {
            if (!bIncludeExisting && bCheckWiki)
            {
                if (hsExisting.Contains(GetWikiTitle(gpPage))) continue;
            }

            string sFn = Path.Combine(sOutputDir, gpPage.Title.Replace(" ", "_").Replace("/", "_") + ".txt");
            File.WriteAllText(sFn, gpPage.Content);
            iWritten++;
            if (bVerbose) Console.WriteLine($"OK  {gpPage.ScriptName} -> {gpPage.Title}");
        }

        string sStatus = bCheckWiki ? $", {hsExisting.Count} existing" : "";
        Console.WriteLine($"Done: {iWritten} written{sStatus} -> {sOutputDir}");
        LogService.Info($"RunGenerate done: {iWritten} written{sStatus} -> {sOutputDir}");
        return iOk;
    }
}