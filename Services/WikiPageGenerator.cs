using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc.Tools;

public static class WikiPageGenerator
{
    public const string DefaultTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_New&action=raw";
    public const string LmgTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_LMG&action=raw";
    public const string PistolTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Pistol&action=raw";
    public const string ShortTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:WeaponShort&action=raw";

    public static int GetTemplateIndex(Dictionary<string, string> vals)
    {
        string wt = vals.TryGetValue("WeaponType", out var t) ? t : "";
        if (wt.Equals("Machinegun", StringComparison.OrdinalIgnoreCase)) return 1;
        string bucket = vals.TryGetValue("bucket", out var b) ? b : "";
        if (bucket == "1") return 2;
        return 0;
    }

    public class GeneratedPage
    {
        public string ScriptName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string ShortContent { get; set; } = "";
    }

    //从脚本目录批量生成所有武器页面
    public static List<GeneratedPage> GenerateAll(string scriptsDir, string resourceDir,
        Dictionary<string, string> tokens, Dictionary<string, LoadoutInfo> loadout,
        string defaultTemplate, string lmgTemplate, string pistolTemplate, string shortTemplate,
        HashSet<string> existingTitles, Dictionary<string, string>? titleToScript = null)
    {
        var pages = new List<GeneratedPage>();
        var files = Directory.GetFiles(scriptsDir, "weapon_*.txt");
        LogService.Info($"GenerateAll: {files.Length} weapon scripts found, {existingTitles.Count} existing titles");
        int skipped = 0;

        foreach (string path in files)
        {
            string scriptName = Path.GetFileNameWithoutExtension(path);
            if (scriptName.Contains("_zombie") || scriptName.Contains("_cubemap") || scriptName.Contains("_riflegrenade"))
            {
                skipped++;
                continue;
            }
            var page = GenerateSingle(path, scriptName, resourceDir, tokens, loadout,
                defaultTemplate, lmgTemplate, pistolTemplate, shortTemplate, titleToScript);
            if (page != null && !existingTitles.Contains(page.Title))
                pages.Add(page);
        }

        LogService.Info($"GenerateAll: {pages.Count} pages generated, {skipped} skipped");
        return pages;
    }

    public static GeneratedPage? GenerateSingle(string scriptPath, string scriptName,
        string resourceDir, Dictionary<string, string> tokens,
        Dictionary<string, LoadoutInfo> loadout,
        string defaultTemplate, string lmgTemplate, string pistolTemplate, string shortTemplate,
        Dictionary<string, string>? titleToScript = null)
    {
        string content = WeaponScriptService.ReadScriptFile(scriptPath);
        var vals = WeaponScriptService.ParseWeaponDataPairs(content);
        if (vals.Count == 0)
        {
            LogService.Warn($"GenerateSingle: no WeaponData KV pairs in {scriptName}");
            return null;
        }

        var info = loadout.TryGetValue(scriptName, out var li) ? li : new LoadoutInfo();
        string printName = vals.TryGetValue("printname", out var pn) ? pn : scriptName;
        //token查找>脚本名索引>跳过无翻译的武器

        string title = LocalizationService.Lookup(tokens, printName, "");
        if (string.IsNullOrEmpty(title) && titleToScript != null)
        {
            var match = titleToScript.FirstOrDefault(kv => kv.Value.Equals(scriptName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key)) title = match.Key;
        }
        if (string.IsNullOrEmpty(title))
        {
            LogService.Warn($"GenerateSingle: no title found for {scriptName} (printname: {printName})");
            return null;
        }

        string ammoDisplay = LocalizationService.Lookup(tokens, vals.GetValueOrDefault("ammo_id_display", ""));
        string originRaw = vals.GetValueOrDefault("origin", "");
        string origin = LocalizationService.Lookup(tokens, originRaw, originRaw);
        string weaponType = LoadoutService.GetWeaponType(vals, info);
        int templateIdx = GetTemplateIndex(vals);
        string detailTemplate = templateIdx == 1 ? lmgTemplate : templateIdx == 2 ? pistolTemplate : defaultTemplate;

        var page = new GeneratedPage { ScriptName = scriptName, Title = title };
        page.Content = FillDetailTemplate(detailTemplate, scriptName, title, vals, info, ammoDisplay, origin, weaponType);
        page.ShortContent = FillShortTemplate(shortTemplate, scriptName, title, vals, info, ammoDisplay, weaponType);
        return page;
    }

    private static string FillDetailTemplate(string tmpl, string scriptName, string title,
        Dictionary<string, string> vals, LoadoutInfo info,
        string ammo, string origin, string weaponType)
    {
        string result = tmpl;
        bool isLmg = result.Contains("[[Bipod]]");
        bool isPistol = !result.Contains("[[Bayonet]]");

        double dg = WeaponScriptService.GetDoubleVal(vals, "damagegeneric");
        string[] dmgKeys = { "damageheadmultiplier", "damagechestmultiplier", "damagestomachmultiplier", "damagelegmultiplier", "damagearmmultiplier" };
        double[] mults = dmgKeys.Select(k => Math.Max(WeaponScriptService.GetDoubleVal(vals, k), 1.0)).ToArray();

        string fireRate = vals.TryGetValue("firerate", out var fr) && fr != "-1" && fr != "0" ? fr : "N/A";
        string spread = vals.TryGetValue("bulletspreaddegrees", out var h) ? fmt(h) : "?";
        string spreadAds = vals.TryGetValue("bulletspreaddegreesironsighted", out var a) ? fmt(a) : "?";
        string spreadBipod = vals.TryGetValue("bulletspreaddegreesbipod", out var bh) ? fmt(bh) : "?";
        string spreadBipodAds = vals.TryGetValue("bulletspreaddegreesbipodironsighted", out var ba) ? fmt(ba) : "?";
        string rangeMod = vals.TryGetValue("rangemodifier", out var rm) ? fmt(rm) : "?";
        string muzzleVel = vals.GetValueOrDefault("muzzle_velocity", vals.GetValueOrDefault("gl_velocity", "?"));
        double bulletWt = WeaponScriptService.GetDoubleVal(vals, "bullet_weight");
        double weight = WeaponScriptService.GetDoubleVal(vals, "weight");
        string clipDisplay = WeaponScriptService.FormatClipSize(vals.GetValueOrDefault("clip_size", ""), vals.GetValueOrDefault("extrabulletchamber", "0"));
        string fireModes = vals.GetValueOrDefault("supportedfiremodes", "?");
        string hasBayonet = vals.GetValueOrDefault("hasbayonet", "0") == "1" ? "YES" : "NO";

        string factionText = info.Factions.Count > 0 ? string.Join("/", info.Factions) : "USVC";
        result = Regex.Replace(result, @"\[\[USVC\]\]", factionText);
        if (info.Factions.Count > 0)
        {
            if (!info.Factions.Contains("US")) result = result.Replace("[[File:Flag_us_new.png|50px]]", "");
            if (!info.Factions.Contains("VC")) result = result.Replace("[[File:Flag_vc_new.png|50px]]", "");
        }

        result = result.Replace("[[File:.png|512px]]", $"[[File:{title}.png|512px]]");
        result = result.Replace("[[File:.svg|512px]]", $"[[File:{scriptName}.svg|512px]]");
        result = Regex.Replace(result, @"<b>\s*\[\[\]\]\s*</b>", $"<b>[[{title}]]</b>");
        result = Regex.Replace(result, @"\[\[File:Class_\.png\|50px\]\]", BuildClassMarkup(info));
        result = result.Replace("| [[]]", $"| [[{ammo}]]");
        result = result.Replace("[[+1]] /  ", clipDisplay);

        string dmgLine = $"| {fmt(dg)}";
        for (int i = 0; i < 5; i++)
            dmgLine += $"||x{fmt(mults[i])} = {fmt(dg * mults[i])}";
        result = Regex.Replace(result, @"\| \|\|× = \|\|× = \|\|× = \|\|× = \|\|× = ", dmgLine);

        if (!isPistol)
        {
            var bMatch = Regex.Match(result, @"\|\|YES NO");
            if (bMatch.Success)
                result = result.Substring(0, bMatch.Index + 2) + hasBayonet + result.Substring(bMatch.Index + 2 + "YES NO".Length);
            var rMatch = Regex.Match(result, @"\|\|YES NO");
            if (rMatch.Success)
                result = result.Substring(0, rMatch.Index + 2) + "NO" + result.Substring(rMatch.Index + 2 + "YES NO".Length);
        }

        result = Regex.Replace(result, @"\|\[\[\]\]", $"|[[{weaponType}]]");
        result = result.Replace("Auto+Semi", fireModes);
        result = result.Replace("|| RPM", $"||{fireRate} RPM");

        if (isLmg)
        {
            var sMatch = Regex.Match(result, @"° & ° \[\[ADS\]\]");
            if (sMatch.Success)
                result = result.Substring(0, sMatch.Index) + $"{spread}° & {spreadAds}° [[ADS]]" + result.Substring(sMatch.Index + sMatch.Length);
            sMatch = Regex.Match(result, @"° & ° \[\[ADS\]\]");
            if (sMatch.Success)
                result = result.Substring(0, sMatch.Index) + $"{spreadBipod}° & {spreadBipodAds}° [[ADS]]" + result.Substring(sMatch.Index + sMatch.Length);
        }
        else
        {
            result = result.Replace("° & ° [[ADS]]", $"{spread}° & {spreadAds}° [[ADS]]");
        }

        result = result.Replace("||RM", $"||{rangeMod}");
        result = result.Replace("|| m/s", $"||{muzzleVel} m/s");
        result = result.Replace("|| g ( gr)", $"||{fmt(bulletWt * 1000)} g ({fmt(bulletWt * 15432.36)} gr)");
        result = result.Replace("|| kg ( lbs)", $"||{fmt(weight)} kg ({fmt(weight * 2.20462)} lbs)");

        result = result.Replace("|FN||", $"|{title}||");
        result = result.Replace("|CAL||", $"|[[{ammo}]]||");
        result = result.Replace("|[[PoO]]||", "||||");
        result = result.Replace("||D8||", "||||");
        result = result.Replace("||ARM||", "||||");
        result = result.Replace("|weapon_", $"|{scriptName}");

        return result;
    }

    private static string FillShortTemplate(string tmpl, string scriptName, string title,
        Dictionary<string, string> vals, LoadoutInfo info,
        string ammo, string weaponType)
    {
        string result = tmpl;
        double dg = WeaponScriptService.GetDoubleVal(vals, "damagegeneric");
        double hm = Math.Max(WeaponScriptService.GetDoubleVal(vals, "damageheadmultiplier"), 1.0);
        double cm = Math.Max(WeaponScriptService.GetDoubleVal(vals, "damagechestmultiplier"), 1.0);
        string clipDisplay = WeaponScriptService.FormatClipSize(vals.GetValueOrDefault("clip_size", ""), vals.GetValueOrDefault("extrabulletchamber", "0"));

        result = result.Replace("[[File:_3d_t.png|250px]]", $"[[File:{title}.png|250px]]");
        result = result.Replace("[[File:_ki.svg|250px]]", $"[[File:{scriptName}.svg|250px]]");
        result = result.Replace("[[File:Class_.png|50px]]", BuildClassMarkup(info));
        result = result.Replace("<b>[[]]</b>", $"<b>[[{title}]]</b>");
        result = result.Replace("[[+1]] /  ", clipDisplay);
        result = result.Replace("||  || ", $"|| {fmt(dg * cm)} || {fmt(dg * hm)} || ");

        if (info.Factions.Count > 0)
        {
            if (!info.Factions.Contains("US")) result = result.Replace("[[File:Flag_us_new.png|50px]]", "");
            if (!info.Factions.Contains("VC")) result = result.Replace("[[File:Flag_vc_new.png|50px]]", "");
        }
        return result;
    }

    private static readonly string[] AllMainClasses = { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };

    private static string BuildClassMarkup(LoadoutInfo info)
    {
        if (info.Sources.Contains("main"))
        {
            if (info.Classes.Count == 0) return "''[[WIP]]''";

            int missingCount = AllMainClasses.Length - info.Classes.Count;
            if (missingCount <= 2 && missingCount > 0)
            {
                var missing = AllMainClasses.Where(c => !info.Classes.Contains(c)).ToList();
                return $"<b>Everyone Except {string.Join(" and ", missing.Select(Capitalize))}<br>";
            }

            var sb = new StringBuilder();
            foreach (string cls in LoadoutService.ClassOrder)
                if (info.Classes.Contains(cls) && LoadoutService.ClassImageMap.TryGetValue(cls, out var img))
                    sb.Append($"[[File:{img}|50px]] <b>[[{Capitalize(cls)}]]</b><br>");
            return sb.Length > 0 ? sb.ToString() : "''[[WIP]]''";
        }

        if ((info.Sources.Contains("zombie") || info.Sources.Contains("special")) && info.Classes.Count == 0)
        {
            var parts = new List<string>();
            if (info.Sources.Contains("special")) parts.Add("[[Special Loadout]]");
            if (info.Sources.Contains("zombie")) parts.Add("[[Zombies|<span style=\"color:#ff6905;\">Zombies</span>]]");
            if (parts.Count > 0) return string.Join("<br>", parts);
        }

        return "''[[WIP]]''";
    }

    private static string fmt(double d) => WeaponScriptService.FormatDouble(d);

    private static string fmt(string s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return fmt(d);
        return s;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}