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
    private const string DetailTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_New&action=raw";
    private const string ShortTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:WeaponShort&action=raw";

    private static readonly string[] DamageKeys = { "damageheadmultiplier", "damagechestmultiplier", "damagestomachmultiplier", "damagelegmultiplier", "damagearmmultiplier" };

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
        string detailTemplate, string shortTemplate, HashSet<string> existingTitles,
        Dictionary<string, string>? titleToScript = null)
    {
        var pages = new List<GeneratedPage>();
        foreach (string path in Directory.GetFiles(scriptsDir, "weapon_*.txt"))
        {
            string scriptName = Path.GetFileNameWithoutExtension(path);
            if (scriptName.Contains("_zombie") || scriptName.Contains("_cubemap") || scriptName.Contains("_riflegrenade")) continue;
            var page = GenerateSingle(path, scriptName, resourceDir, tokens, loadout, detailTemplate, shortTemplate, titleToScript);
            if (page != null && !existingTitles.Contains(page.Title))
                pages.Add(page);
        }
        return pages;
    }

    public static GeneratedPage? GenerateSingle(string scriptPath, string scriptName,
        string resourceDir, Dictionary<string, string> tokens,
        Dictionary<string, LoadoutInfo> loadout, string detailTemplate, string shortTemplate,
        Dictionary<string, string>? titleToScript = null)
    {
        string content = WeaponScriptService.ReadScriptFile(scriptPath);
        var vals = WeaponScriptService.ParseWeaponDataPairs(content);
        if (vals.Count == 0) return null;

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
            return null;
        string ammoDisplay = LocalizationService.Lookup(tokens, vals.GetValueOrDefault("ammo_id_display", ""));
        string originRaw = vals.GetValueOrDefault("origin", "");
        string origin = LocalizationService.Lookup(tokens, originRaw, originRaw);
        string weaponType = LoadoutService.GetWeaponType(vals, info);

        var page = new GeneratedPage { ScriptName = scriptName, Title = title };
        page.Content = FillDetailTemplate(detailTemplate, scriptName, title, vals, info, tokens, ammoDisplay, origin, weaponType);
        page.ShortContent = FillShortTemplate(shortTemplate, scriptName, title, vals, info, tokens, ammoDisplay, weaponType);
        return page;
    }

    private static string FillDetailTemplate(string tmpl, string scriptName, string title,
        Dictionary<string, string> vals, LoadoutInfo info, Dictionary<string, string> tokens,
        string ammo, string origin, string weaponType)
    {
        string result = tmpl;

        double dg = WeaponScriptService.GetDoubleVal(vals, "damagegeneric");
        double[] multipliers = new double[5];
        for (int i = 0; i < 5; i++) multipliers[i] = Math.Max(WeaponScriptService.GetDoubleVal(vals, DamageKeys[i]), 1.0);

        string fireRate = vals.TryGetValue("firerate", out var fr) && fr != "-1" ? $"{fr} RPM" : "";
        string spread = vals.TryGetValue("bulletspreaddegrees", out var h) ? fmt(h) : "?";
        string spreadAds = vals.TryGetValue("bulletspreaddegreesironsighted", out var a) ? fmt(a) : "?";
        string rangeMod = vals.TryGetValue("rangemodifier", out var rm) ? fmt(rm) : "?";
        string muzzleVel = vals.GetValueOrDefault("muzzle_velocity", vals.GetValueOrDefault("gl_velocity", "?"));
        string bulletWtG = fmt(WeaponScriptService.GetDoubleVal(vals, "bullet_weight") * 1000);
        string bulletWtGr = fmt(WeaponScriptService.GetDoubleVal(vals, "bullet_weight") * 15432.36);
        string weightKg = fmt(WeaponScriptService.GetDoubleVal(vals, "weight"));
        string weightLbs = fmt(WeaponScriptService.GetDoubleVal(vals, "weight") * 2.20462);
        string clipDisplay = WeaponScriptService.FormatClipSize(vals.GetValueOrDefault("clip_size", ""), vals.GetValueOrDefault("extrabulletchamber", "0"));
        string fireModes = vals.GetValueOrDefault("supportedfiremodes", "?");
        string hasBayonet = vals.GetValueOrDefault("hasbayonet", "0") == "1" ? "YES" : "NO";
        string hasRifleGrenade = File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scriptName)) ?? "", scriptName + "_riflegrenade.txt")) ? "YES" : "NO";

        result = result.Replace("[[File:.png|512px]]", $"[[File:{title}.png|512px]]");
        result = result.Replace("[[File:.svg|512px]]", $"[[File:{scriptName}.svg|512px]]");
        result = result.Replace("[[File:Class_.png|50px]]", BuildClassMarkup(info));
        result = result.Replace("'''[[]]'''", $"'''[[{title}]]'''");
        result = result.Replace("<b>[[]]</b>", $"<b>[[{title}]]</b>");
        result = result.Replace("[[]]", $"[[{ammo}]]");
        result = result.Replace("[[+1]] /  ", clipDisplay);
        result = result.Replace("× = ", $"×{fmt(multipliers[0])} = {fmt(dg * multipliers[0])}");
        result = result.Replace("YES NO", $"{hasBayonet}    {hasRifleGrenade}");

        result = Regex.Replace(result, @"\|\[\[.*?\]\]", $"|[[{weaponType}]]", RegexOptions.None);
        result = Regex.Replace(result, @"Auto\+Semi", fireModes, RegexOptions.None);
        result = Regex.Replace(result, @"\d+ RPM", fireRate.Length > 0 ? fireRate : "? RPM", RegexOptions.None);
        result = Regex.Replace(result, @"° & ° \[\[ADS\]\]", $"{spread}° & {spreadAds}° [[ADS]]", RegexOptions.None);
        result = Regex.Replace(result, @"\|RM", $"|{rangeMod}", RegexOptions.None);
        result = Regex.Replace(result, @"\d+ m/s", $"{muzzleVel} m/s", RegexOptions.None);
        result = Regex.Replace(result, @"\d+ g \( \d+ gr\)", $"{bulletWtG} g ({bulletWtGr} gr)", RegexOptions.None);
        result = Regex.Replace(result, @"\d+\.?\d* kg \( \d+\.?\d* lbs\)", $"{weightKg} kg ({weightLbs} lbs)", RegexOptions.None);
        result = result.Replace("|FN||", $"|{title}||");
        result = result.Replace("|CAL||", $"|[[{ammo}]]||");
        result = result.Replace("|[[PoO]]||", $"|[[{origin}]]||");
        result = result.Replace("weapon_", scriptName);

        if (!info.Factions.Contains("US")) result = result.Replace("[[File:Flag_us_new.png|50px]]", "");
        if (!info.Factions.Contains("VC")) result = result.Replace("[[File:Flag_vc_new.png|50px]]", "");

        return result;
    }

    private static string FillShortTemplate(string tmpl, string scriptName, string title,
        Dictionary<string, string> vals, LoadoutInfo info, Dictionary<string, string> tokens,
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

        if (!info.Factions.Contains("US")) result = result.Replace("[[File:Flag_us_new.png|50px]]", "");
        if (!info.Factions.Contains("VC")) result = result.Replace("[[File:Flag_vc_new.png|50px]]", "");
        return result;
    }

    private static string BuildClassMarkup(LoadoutInfo info)
    {
        if (info.Sources.Contains("zombie")) return "[[Zombies|<span style=\"color:#ff6905;\">Zombies</span>]]";
        if (info.Sources.Contains("special")) return "[[Special Loadout]]";
        var sb = new StringBuilder();
        foreach (string cls in LoadoutService.ClassOrder)
            if (info.Classes.Contains(cls) && LoadoutService.ClassImageMap.TryGetValue(cls, out var img))
                sb.Append($"[[File:{img}|50px]] <b>[[{Capitalize(cls)}]]</b><br>");
        return sb.Length > 0 ? sb.ToString() : "''[[WIP]]''";
    }

    private static string fmt(double d) => WeaponScriptService.FormatDouble(d);
    private static string fmt(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? fmt(d) : s;

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
}