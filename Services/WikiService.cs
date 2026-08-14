using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc.Services;

public static class WikiService
{
    public static async Task<bool> LoginAsync(string sUser, string sPw)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (string.IsNullOrWhiteSpace(sUser) || string.IsNullOrWhiteSpace(sPw)) return false;
        return await WikiApiService.LoginAsync(sUser, sPw);
    }

    //构建脚本名索引 从Weapon Script Name页拉取
    public static async Task<Dictionary<string, string>?> BuildScriptIndexAsync()
    {
        try
        {
            LogService.Info("Building script name index...");
            string? sIdx = await WikiApiService.GetPageSourceAsync("Weapon Script Name");
            if (sIdx == null)
            {
                LogService.Warn("BuildScriptIndexAsync: 'Weapon Script Name' page not found");
                return null;
            }
            sIdx = sIdx.Replace("\r\n", "\n").Replace('\r', '\n');
            var mpMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            //匹配表格行 捕获|weapon_xxx脚本名及其下方|[[标题]] 用于建立标题->脚本名映射
            foreach (Match m in Regex.Matches(sIdx, @"\|\s*(weapon_[^\s|]+)\s*\n\|\s*\[\[([^\]]+)\]\]"))
                mpMap[m.Groups[2].Value.Trim()] = m.Groups[1].Value;
            LogService.Info($"Script index built: {mpMap.Count} entries");
            return mpMap;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WikiService.BuildScriptIndexAsync");
            return null;
        }
    }

    //包含=[[xxx]]=特征的是汇总表走ConvertSummaryPage 否则走Convert
    public static string ConvertWikiSource(string sInput, string sScriptsDir, Dictionary<string, string>? mpTitleToScript)
    {
        sInput = sInput.Replace("\r\n", "\n").Replace('\r', '\n');
        //检测汇总页 是否存在=[[...]]=形式的标题行
        if (Regex.IsMatch(sInput, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline))
        {
            LogService.Info("ConvertWikiSource: detected summary page, building printname map...");
            var mpMap = mpTitleToScript != null ? new Dictionary<string, string>(mpTitleToScript, StringComparer.OrdinalIgnoreCase) : new();
            foreach (var sPath in Directory.GetFiles(sScriptsDir, "weapon_*.txt"))
            {
                string sSn = Path.GetFileNameWithoutExtension(sPath);
                string sContent = WeaponScriptService.ReadScriptFile(sPath).Replace("\r\n", "\n");
                //从脚本中提取"printname"字段的双引号值
                var mPm = Regex.Match(sContent, @"""printname""\s+""([^""]*)""");
                string sD = mPm.Success ? mPm.Groups[1].Value.TrimStart('#') : sSn;
                if (!mpMap.ContainsKey(sD.Replace("_", " "))) mpMap[sD.Replace("_", " ")] = sSn;
            }
            LogService.Info($"Printname map built: {mpMap.Count} entries");
            return WikiTableConverter.ConvertSummaryPage(sInput, sScriptsDir, mpMap);
        }
        LogService.Info("ConvertWikiSource: single page conversion");
        return WikiTableConverter.Convert(sInput, sScriptsDir);
    }

    public static List<string> ExtractWeaponLinks(string sPageSource, Dictionary<string, string>? mpTitleToScript)
    {
        if (mpTitleToScript == null || mpTitleToScript.Count == 0) return new();
        var hsLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        //匹配wikitext内部链接[[...]] 排除含 | : # < > 的非法链接文本
        foreach (Match m in Regex.Matches(sPageSource, @"\[\[([^\]|:#<>]+)\]\]"))
            if (mpTitleToScript.ContainsKey(m.Groups[1].Value.Trim()))
                hsLinks.Add(m.Groups[1].Value.Trim());
        var rgResult = hsLinks.OrderBy(s => s).ToList();
        LogService.Info($"ExtractWeaponLinks: {rgResult.Count} links found");
        return rgResult;
    }

    public static string GetWikiDir() => Path.Combine(AppContext.BaseDirectory, "wiki");

    public static void SaveToWikiDir(string sFileName, string sContent)
    {
        string sDir = GetWikiDir();
        Directory.CreateDirectory(sDir);
        File.WriteAllText(Path.Combine(sDir, sFileName), sContent);
    }

    //反查脚本名
    public static string? ReverseLookup(string sInput, Dictionary<string, string>? mpIndex)
    {
        if (mpIndex == null || mpIndex.Count == 0) return null;
        string sInputNoExt = Path.GetFileNameWithoutExtension(sInput);
        if (mpIndex.ContainsKey(sInput)) return sInput;
        foreach (var kvp in mpIndex)
        {
            string sSn = kvp.Value;
            string sSnNoExt = Path.GetFileNameWithoutExtension(sSn);
            string sSnStem = sSnNoExt.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? sSnNoExt.Substring(7) : sSnNoExt;
            if (sSn.Equals(sInput, StringComparison.OrdinalIgnoreCase)
                || sSnNoExt.Equals(sInput, StringComparison.OrdinalIgnoreCase)
                || sSnNoExt.Equals(sInputNoExt, StringComparison.OrdinalIgnoreCase)
                || sSnStem.Equals(sInputNoExt, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return mpIndex.Keys.FirstOrDefault(sK => sK.Equals(sInput, StringComparison.OrdinalIgnoreCase));
    }
}