using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WeaponDamageCalc.Services;

public static class LocalizationService
{
    public static Dictionary<string, string> LoadTokens(string sFilePath)
    {
        var mpTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(sFilePath))
        {
            LogService.Warn($"Localization file not found: {sFilePath}");
            return mpTokens;
        }

        LogService.Info($"Loading localization tokens: {sFilePath}");
        string sContent = WeaponScriptService.ReadScriptFile(sFilePath)
            .Replace("\r\n", "\n").Replace('\r', '\n');
        var rgStack = new List<string>();
        string? sPendingKey = null;

        foreach (string sRawLine in sContent.Split('\n'))
        {
            string sLine = StripComment(sRawLine).Trim();
            if (string.IsNullOrEmpty(sLine)) continue;

            //匹配行首带引号的块名后跟{ 如"Foo" { 用于压栈块名
            var mInlineOpen = Regex.Match(sLine, @"^""([^""]+)""\s*\{");
            if (mInlineOpen.Success) { rgStack.Add(mInlineOpen.Groups[1].Value); continue; }
            if (sLine == "{") { rgStack.Add(sPendingKey ?? "__anon__"); sPendingKey = null; continue; }
            if (sLine == "}") { if (rgStack.Count > 0) rgStack.RemoveAt(rgStack.Count - 1); continue; }

            //匹配"key" "value"键值对 捕获键和值
            var mKv = Regex.Match(sLine, @"""([^""]+)""\s+""([^""]*)""");
            if (mKv.Success && IsInsideTokensBlock(rgStack))
                mpTokens[mKv.Groups[1].Value] = mKv.Groups[2].Value;

            //匹配行尾单独的"key" 用于记录待下一个{解析的块名
            var mKeyOnly = Regex.Match(sLine, @"""([^""]+)""$");
            if (mKeyOnly.Success) sPendingKey = mKeyOnly.Groups[1].Value;
        }
        LogService.Info($"Tokens loaded: {mpTokens.Count}");
        return mpTokens;
    }

    private static bool IsInsideTokensBlock(List<string> rgStack) =>
        rgStack.Count == 2 && rgStack[0].Equals("lang", StringComparison.OrdinalIgnoreCase)
                           && rgStack[1].Equals("Tokens", StringComparison.OrdinalIgnoreCase);

    public static string Lookup(Dictionary<string, string> mpTokens, string sKey, string sFallback = "")
    {
        if (string.IsNullOrEmpty(sKey)) return sFallback;
        string sK = sKey.StartsWith("#") ? sKey.Substring(1) : sKey;
        return mpTokens.TryGetValue(sK, out var sVal) ? sVal : sFallback;
    }

    public static string StripComment(string sLine)
    {
        bool bInQuote = false;
        for (int i = 0; i < sLine.Length - 1; i++)
        {
            if (sLine[i] == '"') bInQuote = !bInQuote;
            if (!bInQuote && sLine[i] == '/' && sLine[i + 1] == '/') return sLine.Substring(0, i);
        }
        return sLine;
    }
}