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

    const int OK = 0;
    const int ERR_USAGE = 1;
    const int ERR_LOGIN = 2;
    const int ERR_PAGE_NOT_FOUND = 3;
    const int ERR_UNKNOWN_CMD = 4;
    const int ERR_EXCEPTION = 5;

    [STAThread]
    static int Main(string[] args)
    {
        string currentDir = AppContext.BaseDirectory;
        string mutexName = @"WeaponDamageCalc_" + Convert.ToHexString(MD5.HashData(
            Encoding.UTF8.GetBytes(currentDir.ToLowerInvariant())));
        using var mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            LogService.Info("Mutex locked - another instance is already running in this folder");
            MessageBox.Show("Only one instance of the same folder can be running at once time.",
                "Mangler - Warning", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        var logLevelArg = Opt(args, "--log-level");

        if (args.Length > 0 && args[0].Equals("--log-level", StringComparison.OrdinalIgnoreCase) && args.Length > 1)
        {
            var guiLogLevel = args[1].ToLowerInvariant() switch
            {
                "debug" => LogService.Level.Debug,
                "info"  => LogService.Level.Info,
                "warn"  => LogService.Level.Warn,
                "error" => LogService.Level.Error,
                _       => LogService.Level.Debug
            };
            var remaining = args.Skip(2).ToArray();
            if (remaining.Length > 0)
            {
                return RunCliMode(remaining, guiLogLevel);
            }
            LogService.Enabled = true;
            LogService.MinLevel = guiLogLevel;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            return 0;
        }

        if (args.Length > 0)
        {
            return RunCliMode(args, logLevelArg != null ? ParseLogLevel(logLevelArg) : LogService.Level.Warn);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Form1());
        return 0;
    }

    static LogService.Level ParseLogLevel(string level) => level.ToLowerInvariant() switch
    {
        "debug" => LogService.Level.Debug,
        "info"  => LogService.Level.Info,
        "warn"  => LogService.Level.Warn,
        "error" => LogService.Level.Error,
        _       => LogService.Level.Warn
    };

    static int RunCliMode(string[] args, LogService.Level logLevel)
    {
        LogService.Enabled = true;
        LogService.MinLevel = logLevel;
        AllocConsole();
        LogService.Info($"CLI started: {string.Join(" ", args)} (log level: {logLevel})");
        int code = Task.Run(() => RunCli(args)).GetAwaiter().GetResult();
        Console.Out.Flush();
        if (!Console.IsOutputRedirected && (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)
                                        || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)
                                        || args[0].Equals("/?", StringComparison.OrdinalIgnoreCase)
                                        || args[0].Equals("--fuckyou", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        FreeConsole();
        return code;
    }

    static void ShowHelp()
    {
        string exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "WeaponDamageCalc.exe");
        Console.WriteLine($@"
Keyvalues Mangler™ 5000 — MCV Weapon Stats Tool

Usage:
  {exeName}.exe [command] [options]
  {exeName}.exe --log-level <level>           (GUI with logging)
  Without arguments, launches the GUI

Global Options:
  --log-level <debug|info|warn|error>
      Minimum log level written to mangler.log (default: GUI=debug, CLI=warn)
      DEBUG  Log everything including control value changes and hotkeys
      INFO   Log operations (save, export, import, wiki actions)
      WARN   Log warnings and errors only (missing files, failed operations)
      ERROR  Log errors and fatal events only
      FATAL  No need to specify, this program gets fucked
      The log file auto-rotates at 5 MiB. Warn/Error entries include source
      location in Debug builds. In CLI mode, --verbose prints progress to
      console; the log file independently follows --log-level

Commands:

  --help, -h, /?
      Show this help

  --csv-to-scripts <csv> <dir>
      Export CSV weapon data to script files in <dir>

  --scripts-to-csv <dir> [csv]
      Import script files from <dir> into CSV. Default: .\weapons.csv

  --convert-templates <dir> [--simple]
      Convert old weapon scripts to preset_file template format
      --simple  Skip empty keys and compress blank lines

  --wiki-dryrun <page> <scripts_dir> [--single] [--verbose]
      Fetch <page> from wiki, convert with scripts, save to .\wiki\
      --single   Treat <page> as a single weapon page (not a summary)

  --wiki-upload <page> <scripts_dir> --user <u> --pw <p> [--single] [--verbose]
      Fetch, convert, and upload <page> to wiki. Requires login

  --batch-dryrun <summary_page> <scripts_dir> [--verbose] [--skip-cached]
      Batch convert all weapons linked from <summary_page>
      --skip-cached  Skip pages already saved in .\wiki\

  --batch-upload <summary_page> <scripts_dir> --user <u> --pw <p> [--verbose]
      Batch upload all weapons linked from <summary_page>

  --generate <scripts_dir> [output_dir] [--include-existing] [--check-wiki] [--verbose]
      Generate wiki weapon pages from game scripts and resource files
      Default output: .\generated\
      --include-existing  Overwrite even if wiki pages already exist
      --check-wiki        Query wiki API to skip existing pages

Return codes:
  0  Success   1  Usage error   2  Login failed
  3  Page not found   4  Unknown command   5  Internal error

Examples:
  {exeName}.exe --log-level debug
  {exeName}.exe --csv-to-scripts weapons.csv ""X:\...\vietnam\scripts""
  {exeName}.exe --wiki-dryrun ""Weapons of Vietnam"" ""X:\...\vietnam\scripts"" --verbose
  {exeName}.exe --wiki-upload ""AK-47"" ""X:\...\vietnam\scripts"" --user user --pw pass --single
  {exeName}.exe --batch-dryrun ""Weapons of Vietnam"" ""X:\...\vietnam\scripts"" --skip-cached
  {exeName}.exe --generate ""X:\...\vietnam\scripts"" ""X:\output"" --check-wiki
  {exeName}.exe --convert-templates ""X:\...\vietnam\scripts"" --simple
  {exeName}.exe --scripts-to-csv ""X:\...\vietnam\scripts""
");
    }

    static bool HasFlag(string[] args, string flag) =>
        Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    //按索引取位置参数 越界返回null
    static string? Arg(string[] args, int i) => i < args.Length ? args[i] : null;

    //取命名参数的值 如--user xxx返回xxx
    static string? Opt(string[] args, string name) =>
        Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) is int idx && idx >= 0 ? args[idx + 1] : null;

    static void Verbose(string msg, bool verbose)
    {
        if (verbose) Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {msg}");
    }

    static async Task<int> RunCli(string[] args)
    {
        var cmd = args[0].ToLowerInvariant();
        bool verbose = HasFlag(args, "--verbose");
        LogService.Info($"CLI command: {cmd}, verbose={verbose}");

        try
        {
            switch (cmd)
            {
                case "--fuckyou":
                    Console.WriteLine("FUCK YOU TOO");
                    return OK;
                case "--help":
                case "-h":
                case "/?":
                    ShowHelp();
                    return OK;

                case "--csv-to-scripts":
                {
                    var csv = Arg(args, 1) ?? "weapons.csv";
                    var dir = Arg(args, 2) ?? ".";
                    Console.WriteLine($"CSV -> Scripts: {csv} -> {dir}");
                    var log = WeaponScriptService.ExportCsvToScripts(csv, dir);
                    Console.WriteLine(log);
                    return OK;
                }
                case "--scripts-to-csv":
                {
                    var dir = Arg(args, 1) ?? ".";
                    var csv = Arg(args, 2) ?? "weapons.csv";
                    Console.WriteLine($"Scripts -> CSV: {dir} -> {csv}");
                    var log = WeaponScriptService.ImportScriptsToCsv(dir, csv);
                    Console.WriteLine(log);
                    return OK;
                }
                case "--convert-templates":
                {
                    var dir = Arg(args, 1) ?? ".";
                    var simple = HasFlag(args, "--simple");
                    Console.WriteLine($"Convert templates: {dir} (simple={simple})");
                    var log = ScriptToTemplateConverter.ConvertAll(dir, simple);
                    Console.WriteLine(log);
                    return OK;
                }
                case "--wiki-dryrun":
                {
                    var page = Arg(args, 1);
                    var scripts = Arg(args, 2) ?? ".";
                    bool single = HasFlag(args, "--single");
                    if (page == null) { Console.WriteLine("Usage: --wiki-dryrun <page> <scripts_dir> [--single] [--verbose]"); return ERR_USAGE; }
                    Verbose($"Fetching: {page}", verbose);
                    var source = await WikiApiService.GetPageSourceAsync(page);
                    if (source == null)
                    {
                        //反查脚本名
                        var idx = await WikiService.BuildScriptIndexAsync();
                        string? found = WikiService.ReverseLookup(page, idx);
                        if (found != null) { page = found; source = await WikiApiService.GetPageSourceAsync(found); }
                    }
                    if (source == null) { Console.WriteLine($"Page not found: {page}"); return ERR_PAGE_NOT_FOUND; }
                    Verbose("Converting...", verbose);
                    var result = single ? WikiTableConverter.Convert(source, scripts) : WikiService.ConvertWikiSource(source, scripts, null);
                    string fn = page.Replace(" ", "_").Replace("/", "_") + ".txt";
                    WikiService.SaveToWikiDir(fn, result);
                    Console.WriteLine($"Saved: {WikiService.GetWikiDir()}\\{fn}  ({result.Split('\n').Length} lines)");
                    return OK;
                }
                case "--wiki-upload":
                {
                    var page = Arg(args, 1);
                    var scripts = Arg(args, 2) ?? ".";
                    var user = Opt(args, "--user");
                    var pw = Opt(args, "--pw");
                    bool single = HasFlag(args, "--single");
                    if (page == null || user == null || pw == null) { Console.WriteLine("Usage: --wiki-upload <page> <scripts_dir> --user <u> --pw <p> [--single] [--verbose]"); return ERR_USAGE; }
                    Verbose("Logging in...", verbose);
                    if (!await WikiService.LoginAsync(user, pw)) { Console.WriteLine("Login failed"); return ERR_LOGIN; }
                    Verbose($"Fetching: {page}", verbose);
                    var source = await WikiApiService.GetPageSourceAsync(page);
                    if (source == null)
                    {
                        //反查
                        var idx = await WikiService.BuildScriptIndexAsync();
                        string? found = WikiService.ReverseLookup(page, idx);
                        if (found != null) { page = found; source = await WikiApiService.GetPageSourceAsync(found); }
                    }
                    if (source == null) { Console.WriteLine($"Page not found: {page}"); return ERR_PAGE_NOT_FOUND; }
                    Verbose("Converting...", verbose);
                    var result = single ? WikiTableConverter.Convert(source, scripts) : WikiService.ConvertWikiSource(source, scripts, null);
                    if (await WikiApiService.IsSameContentAsync(page, result)) { Console.WriteLine("Unchanged, skip"); return OK; }
                    Verbose("Uploading...", verbose);
                    bool ok = await WikiApiService.SavePageAsync(page, result, "Update weapon data from scripts");
                    Console.WriteLine(ok ? "Saved!" : "Save failed");
                    return ok ? OK : ERR_EXCEPTION;
                }
                case "--batch-dryrun":
                {
                    var page = Arg(args, 1);
                    var scripts = Arg(args, 2) ?? ".";
                    bool skipCached = HasFlag(args, "--skip-cached");
                    if (page == null) { Console.WriteLine("Usage: --batch-dryrun <summary_page> <scripts_dir> [--verbose] [--skip-cached]"); return ERR_USAGE; }
                    return await RunBatchDryrun(page, scripts, skipCached, verbose);
                }
                case "--batch-upload":
                {
                    var page = Arg(args, 1);
                    var scripts = Arg(args, 2) ?? ".";
                    var user = Opt(args, "--user");
                    var pw = Opt(args, "--pw");
                    if (page == null || user == null || pw == null) { Console.WriteLine("Usage: --batch-upload <summary_page> <scripts_dir> --user <u> --pw <p> [--verbose]"); return ERR_USAGE; }
                    Verbose("Logging in...", verbose);
                    if (!await WikiService.LoginAsync(user, pw)) { Console.WriteLine("Login failed"); return ERR_LOGIN; }
                    return await RunBatchUpload(page, scripts, verbose);
                }
                case "--generate":
                {
                    var scripts = Arg(args, 1) ?? ".";
                    var output = Arg(args, 2) ?? "generated";
                    bool includeExisting = HasFlag(args, "--include-existing");
                    bool checkWiki = HasFlag(args, "--check-wiki");
                    return await RunGenerate(scripts, output, includeExisting, checkWiki, verbose);
                }
                default:
                    Console.WriteLine($"Unknown command: {cmd}");
                    Console.WriteLine("Use --help for usage info.");
                    return ERR_UNKNOWN_CMD;
            }
        }
        catch (Exception ex)
        {
            LogService.Fatal(ex, $"RunCli: {cmd}");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ERR_EXCEPTION;
        }
    }

    static async Task<int> RunBatchDryrun(string summaryPage, string scriptsDir, bool skipCached, bool verbose)
    {
        Verbose($"Fetching summary: {summaryPage}", verbose);
        var source = await WikiApiService.GetPageSourceAsync(summaryPage);
        if (source == null) { Console.WriteLine($"Page not found: {summaryPage}"); return ERR_PAGE_NOT_FOUND; }
        Verbose("Building script index...", verbose);
        var index = await WikiService.BuildScriptIndexAsync();
        var links = WikiService.ExtractWeaponLinks(source, index);
        if (links.Count == 0) { Console.WriteLine("No weapon links found"); return OK; }

        string wikiDir = WikiService.GetWikiDir();
        Directory.CreateDirectory(wikiDir);
        int done = 0, fail = 0, skipped = 0;

        Console.WriteLine($"Batch DryRun: {links.Count} pages{(skipCached ? " [skip cached]" : "")}");
        Console.WriteLine(new string('-', 40));

        for (int i = 0; i < links.Count; i++)
        {
            string link = links[i];
            Verbose($"[{i + 1}/{links.Count}] {link}", verbose);
            string fn = link.Replace(" ", "_").Replace("/", "_") + ".txt";
            string fp = Path.Combine(wikiDir, fn);

            if (skipCached && File.Exists(fp)) { skipped++; Console.WriteLine($"SKIP (cached)  {link}"); continue; }

            try
            {
                var src = await WikiApiService.GetPageSourceAsync(link);
                if (src == null) { fail++; Console.WriteLine($"FAIL fetch: {link}"); }
                else
                {
                    //单个武器详情页走Convert
                    string converted = WikiTableConverter.Convert(src, scriptsDir);
                    File.WriteAllText(fp, converted);
                    done++;
                    Console.WriteLine($"OK  {link}");
                }
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine($"ERR {link}: {ex.Message}");
                LogService.Error(ex, $"RunBatchDryrun: {link}");
            }
        }

        Console.WriteLine(new string('-', 40));
        string cachedInfo = skipped > 0 ? $", {skipped} cached" : "";
        Console.WriteLine($"Done: {done} ok, {fail} fail{cachedInfo}");
        LogService.Info($"RunBatchDryrun done: {done} ok, {fail} fail, {skipped} cached");
        return fail > 0 ? ERR_EXCEPTION : OK;
    }

    static async Task<int> RunBatchUpload(string summaryPage, string scriptsDir, bool verbose)
    {
        Verbose($"Fetching summary: {summaryPage}", verbose);
        var source = await WikiApiService.GetPageSourceAsync(summaryPage);
        if (source == null) { Console.WriteLine($"Page not found: {summaryPage}"); return ERR_PAGE_NOT_FOUND; }
        Verbose("Building script index...", verbose);
        var index = await WikiService.BuildScriptIndexAsync();
        var links = WikiService.ExtractWeaponLinks(source, index);
        if (links.Count == 0) { Console.WriteLine("No weapon links found"); return OK; }

        string wikiDir = WikiService.GetWikiDir();
        if (!Directory.Exists(wikiDir)) { Console.WriteLine("No wiki folder found. Run --batch-dryrun first."); return ERR_USAGE; }

        int done = 0, fail = 0, skip = 0;
        Console.WriteLine($"Batch Upload: {links.Count} pages");
        Console.WriteLine(new string('-', 40));

        for (int i = 0; i < links.Count; i++)
        {
            string link = links[i];
            Verbose($"[{i + 1}/{links.Count}] {link}", verbose);
            string fp = Path.Combine(wikiDir, link.Replace(" ", "_").Replace("/", "_") + ".txt");

            if (!File.Exists(fp)) { skip++; Console.WriteLine($"SKIP no file: {link}"); continue; }
            string content = File.ReadAllText(fp);
            if (await WikiApiService.IsSameContentAsync(link, content)) { skip++; Console.WriteLine($"SKIP unchanged: {link}"); continue; }

            try
            {
                bool ok = await WikiApiService.SavePageAsync(link, content, "Update weapon data from scripts");
                if (ok) { done++; Console.WriteLine($"OK  {link}"); }
                else { fail++; Console.WriteLine($"FAIL upload: {link}"); }
            }
            catch (Exception ex)
            {
                fail++;
                Console.WriteLine($"ERR {link}: {ex.Message}");
                LogService.Error(ex, $"RunBatchUpload: {link}");
            }
        }

        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"Done: {done} ok, {fail} fail, {skip} skip");
        LogService.Info($"RunBatchUpload done: {done} ok, {fail} fail, {skip} skip");
        return fail > 0 ? ERR_EXCEPTION : OK;
    }

    static async Task<int> RunGenerate(string scriptsDir, string outputDir, bool includeExisting, bool checkWiki, bool verbose)
    {
        Console.WriteLine($"Generate: {scriptsDir} -> {outputDir}");

        //scripts/../resource
        string resourceDir = LoadoutService.GetResourceDir(scriptsDir);
        if (!Directory.Exists(resourceDir)) { Console.WriteLine($"Resource folder not found: {resourceDir}"); return ERR_USAGE; }

        Verbose("Loading tokens...", verbose);
        var tokens = LocalizationService.LoadTokens(Path.Combine(resourceDir, "vietnam_english.txt"));
        Verbose($"Tokens: {tokens.Count}", verbose);

        Verbose("Loading loadout...", verbose);
        var loadout = LoadoutService.LoadAll(resourceDir);
        Verbose($"Loadout: {loadout.Count}", verbose);

        Verbose("Fetching templates...", verbose);
        string defaultTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.DefaultTemplateUrl) ?? "";
        string lmgTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.LmgTemplateUrl) ?? defaultTemplate;
        string pistolTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.PistolTemplateUrl) ?? defaultTemplate;
        string shortTemplate = await WikiApiService.FetchTemplateAsync(WikiPageGenerator.ShortTemplateUrl) ?? "";

        //构建索引用于token查找的fallback和wiki查重
        Verbose("Building script index...", verbose);
        Dictionary<string, string>? index = await WikiService.BuildScriptIndexAsync();
        Verbose("Generating pages...", verbose);
        var generated = WikiPageGenerator.GenerateAll(scriptsDir, resourceDir, tokens, loadout,
            defaultTemplate, lmgTemplate, pistolTemplate, shortTemplate, new HashSet<string>(), index);
        Verbose($"Generated: {generated.Count} pages", verbose);

        //--check-wiki时批量查询已存在页面
        HashSet<string> existing = new();
        if (checkWiki)
        {
            Verbose("Checking wiki for existing pages...", verbose);
            //用索引映射获取wiki真实标题 否则用生成器标题
            var titles = generated.Select(p =>
            {
                if (index != null)
                {
                    var match = index.FirstOrDefault(kv => kv.Value.Equals(p.ScriptName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Key)) return match.Key;
                }
                return p.Title;
            }).ToList();
            existing = await WikiApiService.GetExistingTitlesAsync(titles);
            Verbose($"Existing on wiki: {existing.Count}", verbose);
        }

        Directory.CreateDirectory(outputDir);
        int written = 0;
        foreach (var p in generated)
        {
            if (!includeExisting && checkWiki)
            {
                string wikiTitle = p.Title;
                if (index != null)
                {
                    var match = index.FirstOrDefault(kv => kv.Value.Equals(p.ScriptName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match.Key)) wikiTitle = match.Key;
                }
                if (existing.Contains(wikiTitle)) continue;
            }

            string fn = Path.Combine(outputDir, p.Title.Replace(" ", "_").Replace("/", "_") + ".txt");
            File.WriteAllText(fn, p.Content);
            written++;
            if (verbose) Console.WriteLine($"OK  {p.ScriptName} -> {p.Title}");
        }

        string status = checkWiki ? $", {existing.Count} existing" : "";
        Console.WriteLine($"Done: {written} written{status} -> {outputDir}");
        LogService.Info($"RunGenerate done: {written} written{status} -> {outputDir}");
        return OK;
    }
}