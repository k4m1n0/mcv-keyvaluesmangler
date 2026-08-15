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
    public const string sDefaultTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_New&action=raw";
    public const string sLmgTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_LMG&action=raw";
    public const string sPistolTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:Pistol&action=raw";
    public const string sShortTemplateUrl = "https://wiki.militaryconflictvietnam.com/index.php?title=Template:WeaponShort&action=raw";

    public static int GetTemplateIndex(Dictionary<string, string> mpVals)
    {
        string sWt = mpVals.TryGetValue("WeaponType", out var sT) ? sT : "";
        if (sWt.Equals("Machinegun", StringComparison.OrdinalIgnoreCase)) return 1;
        string sBucket = mpVals.TryGetValue("bucket", out var sB) ? sB : "";
        if (sBucket == "1") return 2;
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
    public static List<GeneratedPage> GenerateAll(string sScriptsDir, string sResourceDir,
        Dictionary<string, string> mpTokens, Dictionary<string, LoadoutInfo> mpLoadout,
        string sDefaultTemplate, string sLmgTemplate, string sPistolTemplate, string sShortTemplate,
        HashSet<string> hsExistingTitles, Dictionary<string, string>? mpTitleToScript = null)
    {
        var rgPages = new List<GeneratedPage>();
        var rgFiles = Directory.GetFiles(sScriptsDir, "weapon_*.txt");
        LogService.Info($"GenerateAll: {rgFiles.Length} weapon scripts found, {hsExistingTitles.Count} existing titles");
        int iSkipped = 0;

        //构建一次反向索引 所有武器共享
        var mpScriptToTitle = WikiService.BuildScriptToTitleIndex(mpTitleToScript);

        foreach (string sPath in rgFiles)
        {
            string sScriptName = Path.GetFileNameWithoutExtension(sPath);
            if (sScriptName.Contains("_zombie") || sScriptName.Contains("_cubemap") || sScriptName.Contains("_riflegrenade"))
            {
                iSkipped++;
                continue;
            }
            var gpPage = GenerateSingle(sPath, sScriptName, sResourceDir, mpTokens, mpLoadout,
                sDefaultTemplate, sLmgTemplate, sPistolTemplate, sShortTemplate, mpScriptToTitle);
            if (gpPage != null)
                rgPages.Add(gpPage);
        }

        LogService.Info($"GenerateAll: {rgPages.Count} pages generated, {iSkipped} skipped");
        return rgPages;
    }

    public static GeneratedPage? GenerateSingle(string sScriptPath, string sScriptName,
        string sResourceDir, Dictionary<string, string> mpTokens,
        Dictionary<string, LoadoutInfo> mpLoadout,
        string sDefaultTemplate, string sLmgTemplate, string sPistolTemplate, string sShortTemplate,
        Dictionary<string, string>? mpScriptToTitle = null)
    {
        if (string.IsNullOrEmpty(sScriptName))
        {
            LogService.Warn("GenerateSingle: sScriptName is null or empty");
            return null;
        }

        string sContent = WeaponScriptService.ReadScriptFile(sScriptPath);
        var mpVals = WeaponScriptService.ParseWeaponDataPairs(sContent);
        if (mpVals.Count == 0)
        {
            LogService.Warn($"GenerateSingle: no WeaponData KV pairs in {sScriptName}");
            return null;
        }

        var liInfo = mpLoadout.TryGetValue(sScriptName, out var liExisting) ? liExisting : new LoadoutInfo();
        string sPrintName = mpVals.TryGetValue("printname", out var sPn) ? sPn : sScriptName;
        //token查找>脚本名索引>跳过无翻译的武器

        string sTitle = LocalizationService.Lookup(mpTokens, sPrintName, "");
        if (string.IsNullOrEmpty(sTitle) && mpScriptToTitle != null)
        {
            if (mpScriptToTitle.TryGetValue(sScriptName, out string? sWikiTitle) && !string.IsNullOrEmpty(sWikiTitle))
                sTitle = sWikiTitle;
        }
        if (string.IsNullOrEmpty(sTitle))
        {
            LogService.Warn($"GenerateSingle: no title found for {sScriptName} (printname: {sPrintName})");
            return null;
        }

        string sAmmoDisplay = LocalizationService.Lookup(mpTokens, mpVals.GetValueOrDefault("ammo_id_display", ""));
        string sOriginRaw = mpVals.GetValueOrDefault("origin", "");
        string sOrigin = LocalizationService.Lookup(mpTokens, sOriginRaw, sOriginRaw);
        string sWeaponType = LoadoutService.GetWeaponType(mpVals, liInfo);
        int iTemplateIdx = GetTemplateIndex(mpVals);
        string sDetailTemplate = iTemplateIdx == 1 ? sLmgTemplate : iTemplateIdx == 2 ? sPistolTemplate : sDefaultTemplate;

        var gpPage = new GeneratedPage { ScriptName = sScriptName, Title = sTitle };
        gpPage.Content = FillDetailTemplate(sDetailTemplate, sScriptName, sTitle, mpVals, liInfo, sAmmoDisplay, sOrigin, sWeaponType, iTemplateIdx);
        gpPage.ShortContent = FillShortTemplate(sShortTemplate, sScriptName, sTitle, mpVals, liInfo);
        return gpPage;
    }

    private static string FillDetailTemplate(string sTmpl, string sScriptName, string sTitle,
        Dictionary<string, string> mpVals, LoadoutInfo liInfo,
        string sAmmo, string sOrigin, string sWeaponType, int iTemplateIdx)
    {
        string sResult = sTmpl;
        bool bIsLmg = iTemplateIdx == 1;
        bool bIsPistol = iTemplateIdx == 2;

        double dDg = WeaponScriptService.GetDoubleVal(mpVals, "damagegeneric");
        string[] rgDmgKeys = { "damageheadmultiplier", "damagechestmultiplier", "damagestomachmultiplier", "damagelegmultiplier", "damagearmmultiplier" };
        double[] rgMults = rgDmgKeys.Select(sK => Math.Max(WeaponScriptService.GetDoubleVal(mpVals, sK), 1.0)).ToArray();

        string sFireRate = mpVals.TryGetValue("firerate", out var sFr) && sFr != "-1" && sFr != "0" ? sFr : "N/A";
        string sSpread = mpVals.TryGetValue("bulletspreaddegrees", out var sH) ? fmt(sH) : "?";
        string sSpreadAds = mpVals.TryGetValue("bulletspreaddegreesironsighted", out var sA) ? fmt(sA) : "?";
        string sSpreadBipod = mpVals.TryGetValue("bulletspreaddegreesbipod", out var sBh) ? fmt(sBh) : "?";
        string sSpreadBipodAds = mpVals.TryGetValue("bulletspreaddegreesbipodironsighted", out var sBa) ? fmt(sBa) : "?";
        string sRangeMod = mpVals.TryGetValue("rangemodifier", out var sRm) ? fmt(sRm) : "?";
        string sMuzzleVel = mpVals.GetValueOrDefault("muzzle_velocity", mpVals.GetValueOrDefault("gl_velocity", "?"));
        double dBulletWt = WeaponScriptService.GetDoubleVal(mpVals, "bullet_weight");
        double dWeight = WeaponScriptService.GetDoubleVal(mpVals, "weight");
        string sClipDisplay = WeaponScriptService.FormatClipSize(mpVals.GetValueOrDefault("clip_size", ""), mpVals.GetValueOrDefault("extrabulletchamber", "0"));
        string sFireModes = mpVals.GetValueOrDefault("supportedfiremodes", "?");
        string sHasBayonet = mpVals.GetValueOrDefault("hasbayonet", "0") == "1" ? "YES" : "NO";

        string sFactionText = liInfo.Factions.Count > 0 ? string.Join("/", liInfo.Factions) : "USVC";
        //替换阵营占位符[[USVC]]为实际阵营
        sResult = Regex.Replace(sResult, @"\[\[USVC\]\]", sFactionText);
        if (liInfo.Factions.Count > 0)
        {
            if (!liInfo.Factions.Contains("US")) sResult = sResult.Replace("[[File:Flag_us_new.png|50px]]", "");
            if (!liInfo.Factions.Contains("VC")) sResult = sResult.Replace("[[File:Flag_vc_new.png|50px]]", "");
        }

        sResult = sResult.Replace("[[File:.png|512px]]", $"[[File:{sTitle}.png|512px]]");
        sResult = sResult.Replace("[[File:.svg|512px]]", $"[[File:{sScriptName}.svg|512px]]");
        //替换加粗的空链接占位符 <b>[[]]</b>为实际标题链接
        sResult = Regex.Replace(sResult, @"<b>\s*\[\[\]\]\s*</b>", $"<b>[[{sTitle}]]</b>");
        //替换兵种图片占位符 [[File:Class_.png|50px]]为实际兵种图标
        sResult = Regex.Replace(sResult, @"\[\[File:Class_\.png\|50px\]\]", BuildClassMarkup(liInfo));
        sResult = sResult.Replace("| [[]]", $"| [[{sAmmo}]]");
        sResult = sResult.Replace("[[+1]] /  ", sClipDisplay);

        string sDmgLine = $"| {fmt(dDg)}";
        for (int i = 0; i < 5; i++)
            sDmgLine += $"||x{fmt(rgMults[i])} = {fmt(dDg * rgMults[i])}";
        //替换伤害倍率占位行 | ||× = ||× = ...为实际伤害倍率行
        sResult = Regex.Replace(sResult, @"\| \|\|× = \|\|× = \|\|× = \|\|× = \|\|× = ", sDmgLine);

        if (!bIsPistol)
        {
            //匹配刺刀占位符 ||YES NO替换为是否带刺刀
            var mBayonet = Regex.Match(sResult, @"\|\|YES NO");
            if (mBayonet.Success)
                sResult = sResult.Substring(0, mBayonet.Index + 2) + sHasBayonet + sResult.Substring(mBayonet.Index + 2 + "YES NO".Length);
            //匹配导轨占位符 ||YES NO替换为NO
            var mRail = Regex.Match(sResult, @"\|\|YES NO");
            if (mRail.Success)
                sResult = sResult.Substring(0, mRail.Index + 2) + "NO" + sResult.Substring(mRail.Index + 2 + "YES NO".Length);
        }

        //替换武器类型空链接占位符 |[[]]为实际类型链接
        sResult = Regex.Replace(sResult, @"\|\[\[\]\]", $"|[[{sWeaponType}]]");
        sResult = sResult.Replace("Auto+Semi", sFireModes);
        sResult = sResult.Replace("|| RPM", $"||{sFireRate} RPM");

        if (bIsLmg)
        {
            //匹配散布占位符 ° & ° [[ADS]]替换为hip&ads散布
            var mSpread1 = Regex.Match(sResult, @"° & ° \[\[ADS\]\]");
            if (mSpread1.Success)
                sResult = sResult.Substring(0, mSpread1.Index) + $"{sSpread}° & {sSpreadAds}° [[ADS]]" + sResult.Substring(mSpread1.Index + mSpread1.Length);
            //同上 替换为bipod&bipodAds散布
            var mSpread2 = Regex.Match(sResult, @"° & ° \[\[ADS\]\]");
            if (mSpread2.Success)
                sResult = sResult.Substring(0, mSpread2.Index) + $"{sSpreadBipod}° & {sSpreadBipodAds}° [[ADS]]" + sResult.Substring(mSpread2.Index + mSpread2.Length);
        }
        else
        {
            sResult = sResult.Replace("° & ° [[ADS]]", $"{sSpread}° & {sSpreadAds}° [[ADS]]");
        }

        sResult = sResult.Replace("||RM", $"||{sRangeMod}");
        sResult = sResult.Replace("|| m/s", $"||{sMuzzleVel} m/s");
        sResult = sResult.Replace("|| g ( gr)", $"||{fmt(dBulletWt * 1000)} g ({fmt(dBulletWt * 15432.36)} gr)");
        sResult = sResult.Replace("|| kg ( lbs)", $"||{fmt(dWeight)} kg ({fmt(dWeight * 2.20462)} lbs)");

        sResult = sResult.Replace("|FN||", $"|{sTitle}||");
        sResult = sResult.Replace("|CAL||", $"|[[{sAmmo}]]||");
        sResult = sResult.Replace("|[[PoO]]||", "||||");
        sResult = sResult.Replace("||D8||", "||||");
        sResult = sResult.Replace("||ARM||", "||||");
        sResult = sResult.Replace("|weapon_", $"|{sScriptName}");

        return sResult;
    }

    private static string FillShortTemplate(string sTmpl, string sScriptName, string sTitle,
        Dictionary<string, string> mpVals, LoadoutInfo liInfo)
    {
        string sResult = sTmpl;
        double dDg = WeaponScriptService.GetDoubleVal(mpVals, "damagegeneric");
        double dHm = Math.Max(WeaponScriptService.GetDoubleVal(mpVals, "damageheadmultiplier"), 1.0);
        double dCm = Math.Max(WeaponScriptService.GetDoubleVal(mpVals, "damagechestmultiplier"), 1.0);
        string sClipDisplay = WeaponScriptService.FormatClipSize(mpVals.GetValueOrDefault("clip_size", ""), mpVals.GetValueOrDefault("extrabulletchamber", "0"));

        sResult = sResult.Replace("[[File:_3d_t.png|250px]]", $"[[File:{sTitle}.png|250px]]");
        sResult = sResult.Replace("[[File:_ki.svg|250px]]", $"[[File:{sScriptName}.svg|250px]]");
        sResult = sResult.Replace("[[File:Class_.png|50px]]", BuildClassMarkup(liInfo));
        sResult = sResult.Replace("<b>[[]]</b>", $"<b>[[{sTitle}]]</b>");
        sResult = sResult.Replace("[[+1]] /  ", sClipDisplay);
        sResult = sResult.Replace("||  || ", $"|| {fmt(dDg * dCm)} || {fmt(dDg * dHm)} || ");

        if (liInfo.Factions.Count > 0)
        {
            if (!liInfo.Factions.Contains("US")) sResult = sResult.Replace("[[File:Flag_us_new.png|50px]]", "");
            if (!liInfo.Factions.Contains("VC")) sResult = sResult.Replace("[[File:Flag_vc_new.png|50px]]", "");
        }
        return sResult;
    }

    private static readonly string[] rgAllMainClasses = { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };

    private static string BuildClassMarkup(LoadoutInfo liInfo)
    {
        if (liInfo.Sources.Contains("main"))
        {
            if (liInfo.Classes.Count == 0) return "''[[WIP]]''";

            int iMissingCount = rgAllMainClasses.Length - liInfo.Classes.Count;
            if (iMissingCount <= 2 && iMissingCount > 0)
            {
                var rgMissing = rgAllMainClasses.Where(sC => !liInfo.Classes.Contains(sC)).ToList();
                return $"<b>Everyone Except {string.Join(" and ", rgMissing.Select(Capitalize))}</b><br>";
            }

            var sb = new StringBuilder();
            foreach (string sCls in LoadoutService.rgClassOrder)
                if (liInfo.Classes.Contains(sCls) && LoadoutService.mpClassImage.TryGetValue(sCls, out var sImg))
                    sb.Append($"[[File:{sImg}|50px]] <b>[[{Capitalize(sCls)}]]</b><br>");
            return sb.Length > 0 ? sb.ToString() : "''[[WIP]]''";
        }

        if ((liInfo.Sources.Contains("zombie") || liInfo.Sources.Contains("special")) && liInfo.Classes.Count == 0)
        {
            var rgParts = new List<string>();
            if (liInfo.Sources.Contains("special")) rgParts.Add("[[Special Loadout]]");
            if (liInfo.Sources.Contains("zombie")) rgParts.Add("[[Zombies|<span style=\"color:#ff6905;\">Zombies</span>]]");
            if (rgParts.Count > 0) return string.Join("<br>", rgParts);
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