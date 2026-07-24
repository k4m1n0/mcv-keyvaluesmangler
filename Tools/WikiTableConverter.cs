using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WeaponDamageCalc.Tools;

public static class WikiTableConverter
{
    private static readonly Regex ScriptKvRegex = new(
        @"^[ \t]*""([^""]+)""\s+""([^""]*)""", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ScriptNameRegex = new(
        @"weapon_[^\s|]+", RegexOptions.Compiled);

    private static readonly string[] DamageMultiplierKeys = {
        "damagegeneric", "damageheadmultiplier", "damagechestmultiplier",
        "damagestomachmultiplier", "damagelegmultiplier", "damagearmmultiplier"
    };

    #region 入口

    public static string Convert(string wikiText, string scriptsDir)
    {
        var scripts = LoadAllScripts(scriptsDir);
        wikiText = wikiText.Replace("\r\n", "\n");
        var tables = SplitTables(wikiText);
        var result = new StringBuilder();

        var scriptNames = ExtractScriptNames(tables);
        foreach (var table in tables)
        {
            if (!table.IsTable) { result.Append(table.Content); continue; }
            var rows = SplitRows(table.Content);
            result.Append(IsIndexTable(rows) || scriptNames.Count == 0
                ? table.Content
                : ProcessDataTable(table.Content, scripts, scriptNames));
        }
        return result.ToString();
    }

    private static List<string> ExtractScriptNames(List<TableSegment> tables)
    {
        foreach (var table in tables)
        {
            if (!table.IsTable) continue;
            var rows = SplitRows(table.Content);
            if (!IsIndexTable(rows)) continue;

            var names = new List<string>();
            foreach (var row in rows)
            {
                if (row.TrimStart().StartsWith("!")) continue;
                var cells = ParseRow(row);
                if (cells.Count == 0) continue;
                var m = ScriptNameRegex.Match(StripWikiMarkup(cells[cells.Count - 1]));
                if (m.Success) names.Add(m.Value);
            }
            return names;
        }
        return new List<string>();
    }

    #endregion
    #region 表格处理

    private struct TableSegment { public bool IsTable; public string Content; }

    private static List<TableSegment> SplitTables(string text)
    {
        var segs = new List<TableSegment>();
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf("{|", i, StringComparison.Ordinal);
            if (start < 0) { if (i < text.Length) segs.Add(new TableSegment { IsTable = false, Content = text[i..] }); break; }
            if (start > i) segs.Add(new TableSegment { IsTable = false, Content = text[i..start] });
            int end = FindTableEnd(text, start);
            if (end < 0) end = text.Length;
            segs.Add(new TableSegment { IsTable = true, Content = text[start..end] });
            i = end;
        }
        return segs;
    }

    private static int FindTableEnd(string text, int start)
    {
        int depth = 0;
        for (int j = start; j < text.Length - 1; j++)
        {
            if (text[j] == '{' && text[j + 1] == '|') { depth++; j++; }
            else if (text[j] == '|' && text[j + 1] == '}') { depth--; j++; if (depth == 0) return j + 1; }
        }
        return -1;
    }

    private static bool IsIndexTable(List<string> rows)
    {
        foreach (var row in rows)
        {
            if (!row.TrimStart().StartsWith("!")) continue;
            var cells = ParseRow(row);
            if (cells.Count > 0 && StripWikiMarkup(cells[cells.Count - 1]).Contains("Script"))
                return true;
        }
        return false;
    }

    private static string ProcessDataTable(string content,
        Dictionary<string, Dictionary<string, string>> scripts, List<string> scriptNames)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder();
        int rowIdx = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i], trimmed = line.TrimStart();
            bool isData = trimmed.StartsWith("|") && !trimmed.StartsWith("|-") && !trimmed.StartsWith("{|") && !trimmed.StartsWith("|}");
            if (isData && rowIdx < scriptNames.Count && scripts.TryGetValue(scriptNames[rowIdx], out var v))
                sb.Append(RewriteDataLine(line, v));
            else
                sb.Append(line);
            if (isData) rowIdx++;
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string RewriteDataLine(string line, Dictionary<string, string> values)
    {
        var parts = SplitByDelim(line, "||");
        for (int i = 0; i < parts.Count; i++)
            parts[i] = UpdateCell(parts[i], values, i);
        return string.Join("||", parts);
    }

    #endregion
    #region 行解析

    private static List<string> SplitRows(string content)
    {
        var rows = new List<string>();
        var sb = new StringBuilder();
        foreach (var line in content.Split('\n'))
        {
            string t = line.TrimStart();
            if (t.StartsWith("{|") || t.StartsWith("|}") || t.StartsWith("|-"))
            { if (sb.Length > 0) { rows.Add(sb.ToString().TrimEnd()); sb.Clear(); } continue; }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        if (sb.Length > 0) rows.Add(sb.ToString().TrimEnd());
        return rows;
    }

    private static List<string> ParseRow(string row)
    {
        string s = row.TrimStart();
        while (s.StartsWith("!") || s.StartsWith("|")) s = s[1..].TrimStart();
        var cells = SplitByDelim(s, "!!");
        return cells.Count > 0 ? cells : SplitByDelim(s, "||");
    }

    private static List<string> SplitByDelim(string text, string delim)
    {
        var parts = new List<string>();
        int last = 0;
        bool inLink = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '[' && i + 1 < text.Length && text[i + 1] == '[') inLink = true;
            if (text[i] == ']' && i + 1 < text.Length && text[i + 1] == ']') inLink = false;
            if (!inLink && i + 1 < text.Length && text[i..(i + 2)] == delim)
            { parts.Add(text[last..i]); last = i + 2; i++; }
        }
        if (last < text.Length) parts.Add(text[last..]);
        return parts;
    }

    #endregion
    #region 单元格更新

    private static string UpdateCell(string cell, Dictionary<string, string> v, int col)
    {
        string clean = StripWikiMarkup(cell).Trim();
        if (clean.StartsWith("|") && !clean.StartsWith("||")) clean = clean[1..].TrimStart();

        //x2.75 = 110
        var dmgMatch = Regex.Match(clean, @"^x(\d+\.?\d*)\s*=\s*(\d+\.?\d*)$");
        if (dmgMatch.Success)
        {
            double mult = double.Parse(dmgMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (col < DamageMultiplierKeys.Length && GetDouble(v, DamageMultiplierKeys[col]) is double sm && sm > 0) mult = sm;
            if (GetDouble(v, "damagegeneric") is double bd && bd > 0)
                return Regex.Replace(cell, @"x\d+\.?\d*\s*=\s*\d+\.?\d*",
                    $"x{FormatDouble(mult)} = {FormatDouble(Math.Round(bd * mult, 2))}");
            return cell;
        }

        //7.5 / 1.5 [[ADS]]
        var spreadMatch = Regex.Match(clean, @"^(\d+\.?\d*)\s*/\s*(\d+\.?\d*)\s*(ADS|\[\[ADS\]\])$");
        if (spreadMatch.Success)
        {
            if (GetDouble(v, "bulletspreaddegrees") is double h && GetDouble(v, "bulletspreaddegreesironsighted") is double a && (h > 0 || a > 0))
                return Regex.Replace(cell, @"\d+\.?\d*\s*/\s*\d+\.?\d*\s*\[\[ADS\]\]", $"{FormatDouble(h)} / {FormatDouble(a)} [[ADS]]");
            return cell;
        }

        //0.94
        if (Regex.IsMatch(clean, @"^0\.\d+$") && GetDouble(v, "rangemodifier") is double rm && rm > 0)
            return Regex.Replace(cell, @"0\.\d+", FormatDouble(rm));

        //600 RPM
        var rpmMatch = Regex.Match(clean, @"^(\d+)\s*RPM$");
        if (rpmMatch.Success && v.TryGetValue("firerate", out var fr) && int.TryParse(fr, out int ir) && ir > 0)
            return Regex.Replace(cell, @"\d+\s*RPM", $"{ir} RPM");

        //3.8 kg (8.38 lbs)
        var weightLbsMatch = Regex.Match(clean, @"^(\d+\.?\d*)\s*kg\s*\((\d+\.?\d*)\s*lbs\)$");
        if (weightLbsMatch.Success && GetDouble(v, "weight") is double wk && wk > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*kg\s*\(\d+\.?\d*\s*lbs\)",
                $"{FormatDouble(wk)} kg ({FormatDouble(Math.Round(wk * 2.20462, 2))} lbs)");

        //3.8 kg
        var weightMatch = Regex.Match(clean, @"^(\d+\.?\d*)\s*kg$");
        if (weightMatch.Success && GetDouble(v, "weight") is double w && w > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*kg", $"{FormatDouble(w)} kg");

        //7.9 g (121.92 gr)
        var bulletWeightMatch = Regex.Match(clean, @"^(\d+\.?\d*)\s*g\s*\((\d+\.?\d*)\s*gr\)$");
        if (bulletWeightMatch.Success && GetDouble(v, "bullet_weight") is double bwk && bwk > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*g\s*\(\d+\.?\d*\s*gr\)",
                $"{FormatDouble(Math.Round(bwk * 1000, 1))} g ({FormatDouble(Math.Round(bwk * 15432.36, 2))} gr)");

        //30 / 90
        var clipMatch = Regex.Match(clean, @"^(\d+).*?/\s*(\d+)$");
        if (clipMatch.Success && v.TryGetValue("clip_size", out var clip))
        {
            var parts = clip.Split('/');
            if (parts.Length == 2)
            {
                string extra = v.TryGetValue("extrabulletchamber", out var exc) && exc == "1" ? "[[+1]]" : "";
                return Regex.Replace(cell, @"\d+.*?/\s*\d+", $"{parts[0]}{extra} / {parts[1]}");
            }
        }

        //41
        if (Regex.IsMatch(clean, @"^[1-9]\d*\.?\d*$")
            && GetDouble(v, "damagegeneric") is double dg && dg > 0
            && Math.Abs(double.Parse(clean, CultureInfo.InvariantCulture) - dg) < 50)
            return Regex.Replace(cell, @"\d+\.?\d*", FormatDouble(dg));

        return cell;
    }

    #endregion
    #region 脚本加载

    private static Dictionary<string, Dictionary<string, string>> LoadAllScripts(string scriptsDir)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(scriptsDir)) return result;
        foreach (var path in Directory.GetFiles(scriptsDir, "weapon_*.txt"))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string content = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            int wd = content.IndexOf("WeaponData", StringComparison.Ordinal);
            if (wd < 0) continue;
            int bs = content.IndexOf('{', wd);
            if (bs < 0) continue;
            int be = FindMatchingBrace(content, bs);
            if (be < 0) continue;
            foreach (Match m in ScriptKvRegex.Matches(content.Substring(bs + 1, be - bs - 1)))
                values[m.Groups[1].Value] = m.Groups[2].Value;
            if (values.Count > 0) result[Path.GetFileNameWithoutExtension(path)] = values;
        }
        return result;
    }

    private static int FindMatchingBrace(string text, int start)
    {
        int depth = 0; bool inStr = false;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\')) inStr = !inStr;
            if (!inStr) { if (text[i] == '{') depth++; else if (text[i] == '}') { depth--; if (depth == 0) return i; } }
        }
        return -1;
    }

    #endregion
    #region 辅助

    private static double GetDouble(Dictionary<string, string> v, string key) =>
        v.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    private static string FormatDouble(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    private static string StripWikiMarkup(string cell)
    {
        string s = Regex.Replace(cell, @"\[\[[^\]|]+\|([^\]]+)\]\]", "$1");
        s = Regex.Replace(s, @"\[\[([^\]]+)\]\]", "$1");
        return Regex.Replace(s, @"<[^>]+>", "");
    }

    #endregion
}