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
    public static async Task<bool> LoginAsync(string user, string pw)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pw)) return false;
        return await WikiApiService.LoginAsync(user, pw);
    }

    //构建脚本名索引 从Weapon Script Name页拉取
    public static async Task<Dictionary<string, string>?> BuildScriptIndexAsync()
    {
        try
        {
            LogService.Info("Building script name index...");
            string? idx = await WikiApiService.GetPageSourceAsync("Weapon Script Name");
            if (idx == null)
            {
                LogService.Warn("BuildScriptIndexAsync: 'Weapon Script Name' page not found");
                return null;
            }
            idx = idx.Replace("\r\n", "\n").Replace('\r', '\n');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(idx, @"\|\s*(weapon_[^\s|]+)\s*\n\|\s*\[\[([^\]]+)\]\]"))
                map[m.Groups[2].Value.Trim()] = m.Groups[1].Value;
            LogService.Info($"Script index built: {map.Count} entries");
            return map;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WikiService.BuildScriptIndexAsync");
            return null;
        }
    }

    //包含=[[xxx]]=特征的是汇总表走ConvertSummaryPage 否则走Convert
    public static string ConvertWikiSource(string input, string scriptsDir, Dictionary<string, string>? titleToScript)
    {
        input = input.Replace("\r\n", "\n").Replace('\r', '\n');
        if (Regex.IsMatch(input, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline))
        {
            LogService.Info("ConvertWikiSource: detected summary page, building printname map...");
            var map = titleToScript != null ? new Dictionary<string, string>(titleToScript, StringComparer.OrdinalIgnoreCase) : new();
            foreach (var path in Directory.GetFiles(scriptsDir, "weapon_*.txt"))
            {
                string sn = Path.GetFileNameWithoutExtension(path);
                string c = WeaponScriptService.ReadScriptFile(path).Replace("\r\n", "\n");
                var pm = Regex.Match(c, @"""printname""\s+""([^""]*)""");
                string d = pm.Success ? pm.Groups[1].Value.TrimStart('#') : sn;
                if (!map.ContainsKey(d.Replace("_", " "))) map[d.Replace("_", " ")] = sn;
            }
            LogService.Info($"Printname map built: {map.Count} entries");
            return WikiTableConverter.ConvertSummaryPage(input, scriptsDir, map);
        }
        LogService.Info("ConvertWikiSource: single page conversion");
        return WikiTableConverter.Convert(input, scriptsDir);
    }

    public static List<string> ExtractWeaponLinks(string pageSource, Dictionary<string, string>? titleToScript)
    {
        if (titleToScript == null || titleToScript.Count == 0) return new();
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(pageSource, @"\[\[([^\]|:#<>]+)\]\]"))
            if (titleToScript.ContainsKey(m.Groups[1].Value.Trim()))
                links.Add(m.Groups[1].Value.Trim());
        var result = links.OrderBy(x => x).ToList();
        LogService.Info($"ExtractWeaponLinks: {result.Count} links found");
        return result;
    }

    public static string GetWikiDir() => Path.Combine(AppContext.BaseDirectory, "wiki");

    public static void SaveToWikiDir(string fileName, string content)
    {
        string dir = GetWikiDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    //反查脚本名
    public static string? ReverseLookup(string input, Dictionary<string, string>? index)
    {
        if (index == null || index.Count == 0) return null;
        string inputNoExt = Path.GetFileNameWithoutExtension(input);
        if (index.ContainsKey(input)) return input;
        foreach (var kv in index)
        {
            string sn = kv.Value;
            string snNoExt = Path.GetFileNameWithoutExtension(sn);
            string snStem = snNoExt.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? snNoExt.Substring(7) : snNoExt;
            if (sn.Equals(input, StringComparison.OrdinalIgnoreCase)
                || snNoExt.Equals(input, StringComparison.OrdinalIgnoreCase)
                || snNoExt.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase)
                || snStem.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return index.Keys.FirstOrDefault(k => k.Equals(input, StringComparison.OrdinalIgnoreCase));
    }
}