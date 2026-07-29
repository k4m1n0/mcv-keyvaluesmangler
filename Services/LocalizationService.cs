using System;
using System.Collections.Generic;
using System.IO;

namespace WeaponDamageCalc.Services;

public static class LocalizationService
{
    public static Dictionary<string, string> LoadTokens(string filePath)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath))
        {
            LogService.Warn($"Localization file not found: {filePath}");
            return tokens;
        }

        LogService.Info($"Loading localization tokens: {filePath}");
        string content = WeaponScriptService.ReadScriptFile(filePath)
            .Replace("\r\n", "\n").Replace('\r', '\n');
        var stack = new List<string>();
        string? pendingKey = null;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = StripComment(rawLine).Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var inlineOpen = System.Text.RegularExpressions.Regex.Match(line, @"^""([^""]+)""\s*\{");
            if (inlineOpen.Success) { stack.Add(inlineOpen.Groups[1].Value); continue; }
            if (line == "{") { stack.Add(pendingKey ?? "__anon__"); pendingKey = null; continue; }
            if (line == "}") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }

            var kv = System.Text.RegularExpressions.Regex.Match(line, @"""([^""]+)""\s+""([^""]*)""");
            if (kv.Success && IsInsideTokensBlock(stack))
                tokens[kv.Groups[1].Value] = kv.Groups[2].Value;

            var keyOnly = System.Text.RegularExpressions.Regex.Match(line, @"""([^""]+)""$");
            if (keyOnly.Success) pendingKey = keyOnly.Groups[1].Value;
        }
        LogService.Info($"Tokens loaded: {tokens.Count}");
        return tokens;
    }

    private static bool IsInsideTokensBlock(List<string> stack) =>
        stack.Count == 2 && stack[0].Equals("lang", StringComparison.OrdinalIgnoreCase)
                        && stack[1].Equals("Tokens", StringComparison.OrdinalIgnoreCase);

    public static string Lookup(Dictionary<string, string> tokens, string key, string fallback = "")
    {
        if (string.IsNullOrEmpty(key)) return fallback;
        string k = key.StartsWith("#") ? key.Substring(1) : key;
        return tokens.TryGetValue(k, out var v) ? v : fallback;
    }

    public static string StripComment(string line)
    {
        bool inQuote = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            if (!inQuote && line[i] == '/' && line[i + 1] == '/') return line.Substring(0, i);
        }
        return line;
    }
}