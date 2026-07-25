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

    public static string ConvertSummaryPage(string wikiText, string scriptsDir,
        Dictionary<string, string> titleToScript)
    {
        var scripts = LoadAllScripts(scriptsDir);
        wikiText = wikiText.Replace("\r\n", "\n");
        var tables = SplitTables(wikiText);
        var result = new StringBuilder();

        foreach (var table in tables)
        {
            if (!table.IsTable) { result.Append(table.Content); continue; }
            var rows = SplitRows(table.Content);
            if (IsIndexTable(rows) || IsMetaTable(rows)) { result.Append(table.Content); continue; }
            result.Append(ProcessSummaryTable(table.Content, scripts, titleToScript));
        }
        return result.ToString();
    }

    private static bool IsMetaTable(List<string> rows)
    {
        foreach (var row in rows)
        {
            if (!row.TrimStart().StartsWith("!")) continue;
            foreach (var cell in ParseRow(row))
            {
                string clean = StripWikiMarkup(cell);
                if (clean.Contains("Script") || clean.Contains("Icon")) return false;
            }
        }
        return true;
    }

    private static string ProcessSummaryTable(string tableContent,
        Dictionary<string, Dictionary<string, string>> scripts,
        Dictionary<string, string> titleToScript)
    {
        var lines = tableContent.Split('\n');
        var result = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i], trimmed = line.TrimStart();
            bool isDataRow = trimmed.StartsWith("|") && !trimmed.StartsWith("|-")
                && !trimmed.StartsWith("{|") && !trimmed.StartsWith("|}");

            if (isDataRow && TryMatchWeaponRow(line, titleToScript, scripts, out var values))
                result.Append(RewriteDataLine(line, values));
            else
                result.Append(line);

            if (i < lines.Length - 1) result.Append('\n');
        }
        return result.ToString();
    }

    private static bool TryMatchWeaponRow(string line,
        Dictionary<string, string> titleToScript,
        Dictionary<string, Dictionary<string, string>> scripts,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>();
        var nameMatch = Regex.Match(line, @"<b>\[\[([^\]|]+)");
        if (!nameMatch.Success) return false;
        string wikiTitle = nameMatch.Groups[1].Value.Trim();
        if (!titleToScript.TryGetValue(wikiTitle, out var sn) || sn == null) return false;
        if (line.Contains("_riflegrenade") && scripts.TryGetValue(sn + "_riflegrenade", out var rgV))
            sn += "_riflegrenade";
        if (!scripts.TryGetValue(sn, out var v)) return false;
        values = new Dictionary<string, string>(v, StringComparer.OrdinalIgnoreCase);

        PrecomputeDamageValues(values);
        return true;
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

    //匹配嵌套表格的{| |}对
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
            if (row.TrimStart().StartsWith("!") && ParseRow(row) is var cells
                && cells.Count > 0 && StripWikiMarkup(cells[cells.Count - 1]).Contains("Script"))
                return true;
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
            bool isData = trimmed.StartsWith("|") && !trimmed.StartsWith("|-")
                && !trimmed.StartsWith("{|") && !trimmed.StartsWith("|}");
            if (isData && rowIdx < scriptNames.Count && scripts.TryGetValue(scriptNames[rowIdx], out var v))
                sb.Append(RewriteDataLine(line, v));
            else sb.Append(line);
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

    //跳过[[链接]]内的分隔符防止误切
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

        //提取已有的zmstats橙字 计算完新值后重新拼接
        bool hasZombie = false;
        string zombieSuffix = "";
        var zMatch = Regex.Match(cell, @"<br>\s*<span style=""color:#ff6905;"">([^<]*)</span>");
        if (zMatch.Success) { hasZombie = true; zombieSuffix = zMatch.Value; cell = cell.Replace(zombieSuffix, ""); }

        //多弹药武器保留原始格式不转换 如下挂榴弹用<br>分隔
        if (cell.Contains("<br>"))
            return hasZombie ? cell + zombieSuffix : cell;

        double pellets = Math.Max(GetDouble(v, "bullets_per_shot"), 1.0);
        if (pellets > 1 && col == 0)
        {
            double dgVal = GetDouble(v, "damagegeneric");
            if (dgVal > 0)
            {
                return Regex.Replace(cell, @"\d+\.?\d*[Xx]?\d*\.?\d*",
                    $"{FormatDouble(dgVal)}x{FormatDouble(pellets)}");
            }
        }
        else if (pellets > 1 && col == 5 && Regex.IsMatch(clean, @"^\d+\.?\d*[Xx]\d+\.?\d*$"))
        {
            double dgVal = GetDouble(v, "damagegeneric");
            if (dgVal > 0)
            {
                return Regex.Replace(cell, @"\d+\.?\d*[Xx]\d+\.?\d*",
                    $"{FormatDouble(dgVal)}x{FormatDouble(pellets)}");
            }
        }
        else if (col == 0)
        {
            double dgVal = GetDouble(v, "damagegeneric");
            if (dgVal > 0 && Regex.IsMatch(clean, @"^[1-9]\d*\.?\d*$")
                && Math.Abs(double.Parse(clean, CultureInfo.InvariantCulture) - dgVal) < 100)
            {
                return Regex.Replace(cell, @"\d+\.?\d*", FormatDouble(dgVal));
            }
        }

        bool isExplosive = GetDouble(v, "explosiondamage") > 0;

        var dmgMatch = Regex.Match(clean, @"^[x×](\d+\.?\d*)\s*=\s*(\d+\.?\d*)$");
        if (dmgMatch.Success)
        {
            double mult = double.Parse(dmgMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (col < DamageMultiplierKeys.Length && GetDouble(v, DamageMultiplierKeys[col]) is double sm && sm > 0) mult = sm;
            if (GetDouble(v, "damagegeneric") is double bd && bd > 0)
            {
                double totalDmg = Math.Round(bd * mult, 2);
                return Regex.Replace(cell, @"[x×]\d+\.?\d*\s*=\s*\d+\.?\d*",
                    $"x{FormatDouble(mult)} = {FormatDouble(totalDmg)}")
                    + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">x{0} = {1}</span>",
                        FormatDouble(GetDouble(v, "zombie_" + DamageMultiplierKeys[Math.Min(col, DamageMultiplierKeys.Length - 1)])),
                        FormatDouble(Math.Round(
                            Math.Max(GetDouble(v, "zombie_damagegeneric"), 0)
                            * Math.Max(GetDouble(v, "zombie_" + DamageMultiplierKeys[Math.Min(col, DamageMultiplierKeys.Length - 1)]), mult), 2)));
            }
            return cell + (hasZombie ? zombieSuffix : "");
        }

        var spreadMatch = Regex.Match(clean, @"^(\d+\.?\d*)\s*/\s*(\d+\.?\d*)\s*(ADS|\[\[ADS\]\])$");
        if (spreadMatch.Success && GetDouble(v, "bulletspreaddegrees") is double h && GetDouble(v, "bulletspreaddegreesironsighted") is double a && (h > 0 || a > 0))
            return Regex.Replace(cell, @"\d+\.?\d*\s*/\s*\d+\.?\d*\s*\[\[ADS\]\]", $"{FormatDouble(h)} / {FormatDouble(a)} [[ADS]]")
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0} / {1} [[ADS]]</span>",
                    FormatDouble(GetDouble(v, "zombie_bulletspreaddegrees")),
                    FormatDouble(GetDouble(v, "zombie_bulletspreaddegreesironsighted")));

        //机枪散布 Hip° & BipodHip° [[ADS]] 或 ADS° & BipodADS° [[ADS]]
        var bipodMatch = Regex.Match(clean, @"^(\d+\.?\d*)°?\s*&\s*(\d+\.?\d*)°?\s*\[\[ADS\]\]$");
        if (bipodMatch.Success)
        {
            double v1 = double.Parse(bipodMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            double hip = GetDouble(v, "bulletspreaddegrees");
            double ads = GetDouble(v, "bulletspreaddegreesironsighted");
            //用第一个数值更接近hip还是ads来判断行类型
            if (Math.Abs(v1 - hip) <= Math.Abs(v1 - ads) && hip > 0)
            {
                double bipod = GetDouble(v, "bulletspreaddegreesbipod");
                return Regex.Replace(cell, @"\d+\.?\d*°?\s*&\s*\d+\.?\d*°?\s*\[\[ADS\]\]",
                    $"{FormatDouble(hip)}° & {FormatDouble(bipod)}° [[ADS]]");
            }
            else if (ads > 0)
            {
                double bipodAds = GetDouble(v, "bulletspreaddegreesbipodironsighted");
                return Regex.Replace(cell, @"\d+\.?\d*°?\s*&\s*\d+\.?\d*°?\s*\[\[ADS\]\]",
                    $"{FormatDouble(ads)}° & {FormatDouble(bipodAds)}° [[ADS]]");
            }
        }

        if (Regex.IsMatch(clean, @"^0\.\d+$") && GetDouble(v, "rangemodifier") is double rm && rm > 0)
            return Regex.Replace(cell, @"0\.\d+", FormatDouble(rm))
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0}</span>", FormatDouble(GetDouble(v, "zombie_rangemodifier")));

        var rpmMatch = Regex.Match(clean, @"^(\d+)\s*RPM$");
        if (rpmMatch.Success && v.TryGetValue("firerate", out var fr) && int.TryParse(fr, out int ir) && ir > 0)
            return Regex.Replace(cell, @"\d+\s*RPM", $"{ir} RPM")
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0} RPM</span>", v.TryGetValue("zombie_firerate", out var zfr) ? zfr : "");

        var wlm = Regex.Match(clean, @"^(\d+\.?\d*)\s*kg\s*\((\d+\.?\d*)\s*lbs\)$");
        if (wlm.Success && GetDouble(v, "weight") is double wk && wk > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*kg\s*\(\d+\.?\d*\s*lbs\)",
                $"{FormatDouble(wk)} kg ({FormatDouble(Math.Round(wk * 2.20462, 2))} lbs)")
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0} kg ({1} lbs)</span>",
                    FormatDouble(GetDouble(v, "zombie_weight")),
                    FormatDouble(Math.Round(GetDouble(v, "zombie_weight") * 2.20462, 2)));

        var wm = Regex.Match(clean, @"^(\d+\.?\d*)\s*kg$");
        if (wm.Success && GetDouble(v, "weight") is double w && w > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*kg", $"{FormatDouble(w)} kg")
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0} kg</span>", FormatDouble(GetDouble(v, "zombie_weight")));

        var bwm = Regex.Match(clean, @"^(\d+\.?\d*)\s*g\s*\((\d+\.?\d*)\s*gr\)$");
        if (bwm.Success && GetDouble(v, "bullet_weight") is double bwk && bwk > 0)
            return Regex.Replace(cell, @"\d+\.?\d*\s*g\s*\(\d+\.?\d*\s*gr\)",
                $"{FormatDouble(Math.Round(bwk * 1000, 1))} g ({FormatDouble(Math.Round(bwk * 15432.36, 2))} gr)")
                + MakeZombie(v, hasZombie, "<br><span style=\"color:#ff6905;\">{0} g ({1} gr)</span>",
                    FormatDouble(Math.Round(GetDouble(v, "zombie_bullet_weight") * 1000, 1)),
                    FormatDouble(Math.Round(GetDouble(v, "zombie_bullet_weight") * 15432.36, 2)));

        var fireModeMatch = Regex.Match(clean, @"^[A-Za-z]+(\+[A-Za-z]+)*$");
        if (fireModeMatch.Success && v.TryGetValue("SupportedFireModes", out var fm) && !string.IsNullOrEmpty(fm))
        {
            if (clean != fm)
                return Regex.Replace(cell, @"[A-Za-z]+(\+[A-Za-z]+)*", fm);
        }

        if (!isExplosive)
        {
            var clipMatch = Regex.Match(clean, @"^(\d+).*?/\s*(\d+)$");
            if (clipMatch.Success && v.TryGetValue("clip_size", out var clip) && !string.IsNullOrEmpty(clip) && clip != "-1" && clip != "0/0" && !clip.StartsWith("-1/") && clip.Contains('/'))
            {
                var parts = clip.Split('/');
                if (parts.Length == 2)
                {
                    string extra = v.TryGetValue("extrabulletchamber", out var exc) && exc == "1" ? "[[+1]]" : "";
                    return Regex.Replace(cell, @"\d+\[\[.*?\]\]?\s*/\s*\d+|\d+\s*/\s*\d+", $"{parts[0].Trim()}{extra} / {parts[1].Trim()}")
                        + MakeZombieClip(v, hasZombie);
                }
            }
        }

        bool isStandardGun = GetDouble(v, "damagegeneric") > 0 && GetDouble(v, "damageheadmultiplier") > 0;
        bool isDamageColumn = col == 5 || col == 6;
        if (isDamageColumn && (isExplosive || isStandardGun) && Regex.IsMatch(clean, @"^[1-9]\d*\.?\d*$"))
        {
            //容差<100防止替换弹药数量等非伤害数字
            string key = (col == 6 && v.ContainsKey("__head_dmg")) ? "__head_dmg" : "__chest_dmg";
            if (v.TryGetValue(key, out var dmgStr) && double.TryParse(dmgStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dmgVal)
                && Math.Abs(double.Parse(clean, CultureInfo.InvariantCulture) - dmgVal) < 100)
                return Regex.Replace(cell, @"\d+\.?\d*", dmgStr)
                    + MakeZombiePure(v, hasZombie, key);
        }

        return hasZombie ? cell + zombieSuffix : cell;
    }

    #endregion
    #region 僵尸橙字辅助

    private static string MakeZombie(Dictionary<string, string> v, bool hasZombie, string fmt, params string[] vals)
    {
        if (!hasZombie) return "";
        if (vals.All(x => string.IsNullOrEmpty(x) || x == "0" || x == "-1")) return "";
        return string.Format(fmt, vals.Select(x => (object)x).ToArray());
    }

    private static string MakeZombieClip(Dictionary<string, string> v, bool hasZombie)
    {
        if (!hasZombie || !v.TryGetValue("zombie_clip_size", out var zc) || string.IsNullOrEmpty(zc) || zc == "-1" || !zc.Contains('/'))
            return "";
        var parts = zc.Split('/');
        if (parts.Length != 2) return "";
        string extra = v.TryGetValue("extrabulletchamber", out var exc) && exc == "1" ? "[[+1]]" : "";
        return $"<br><span style=\"color:#ff6905;\">{parts[0].Trim()}{extra} / {parts[1].Trim()}</span>";
    }

    //使用预计算的__z_chest_dmg/__z_head_dmg值
    private static string MakeZombiePure(Dictionary<string, string> v, bool hasZombie, string key)
    {
        if (!hasZombie) return "";
        string zKey = key == "__chest_dmg" ? "__z_chest_dmg" : "__z_head_dmg";
        if (v.TryGetValue(zKey, out var zv) && !string.IsNullOrEmpty(zv) && zv != "0")
            return $"<br><span style=\"color:#ff6905;\">{zv}</span>";
        return "";
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
            string block = content.Substring(bs + 1, be - bs - 1);
            foreach (Match m in ScriptKvRegex.Matches(block))
            {
                //只收集顶层键值对 通过大括号计数跳过嵌套块内的同名键
                string before = block.Substring(0, m.Index);
                int ob = 0, cb = 0;
                for (int j = 0; j < before.Length; j++)
                { if (before[j] == '{') ob++; else if (before[j] == '}') cb++; }
                if (ob == cb) values[m.Groups[1].Value] = m.Groups[2].Value;
            }
            int zi = content.IndexOf("zombie_stats", be, StringComparison.Ordinal);
            if (zi >= 0) { LoadSubBlock(values, content, zi, "zombie_"); }

            if (values.Count > 0)
            {
                PrecomputeDamageValues(values);
                result[Path.GetFileNameWithoutExtension(path)] = values;
            }
        }
        return result;
    }

    private static void PrecomputeDamageValues(Dictionary<string, string> values)
    {
        double dg = GetDouble(values, "damagegeneric");
        double ed = GetDouble(values, "explosiondamage");
        if (ed > 0)
        {
            values["__chest_dmg"] = FormatDouble(ed);
            values["__head_dmg"] = FormatDouble(GetDouble(values, "explosionradius"));
            double zed = GetDouble(values, "zombie_explosiondamage");
            if (zed > 0)
            {
                values["__z_chest_dmg"] = FormatDouble(zed);
                values["__z_head_dmg"] = FormatDouble(GetDouble(values, "zombie_explosionradius"));
            }
        }
        else if (dg > 0)
        {
            double cm = Math.Max(GetDouble(values, "damagechestmultiplier"), 1.0);
            double hm = Math.Max(GetDouble(values, "damageheadmultiplier"), 1.0);
            double pellets = Math.Max(GetDouble(values, "bullets_per_shot"), 1.0);
            values["__chest_dmg"] = FormatDouble(Math.Round(dg * cm * pellets, 2));
            values["__head_dmg"] = FormatDouble(Math.Round(dg * hm * pellets, 2));
            double zdg = GetDouble(values, "zombie_damagegeneric");
            if (zdg > 0)
            {
                double zcm = Math.Max(GetDouble(values, "zombie_damagechestmultiplier"), cm);
                double zhm = Math.Max(GetDouble(values, "zombie_damageheadmultiplier"), hm);
                double zpellets = Math.Max(GetDouble(values, "zombie_bullets_per_shot"), pellets);
                values["__z_chest_dmg"] = FormatDouble(Math.Round(zdg * zcm * zpellets, 2));
                values["__z_head_dmg"] = FormatDouble(Math.Round(zdg * zhm * zpellets, 2));
            }
        }
    }

    private static void LoadSubBlock(Dictionary<string, string> values, string content, int blockIdx, string prefix)
    {
        int bs = content.IndexOf('{', blockIdx);
        if (bs < 0) return;
        int be = FindMatchingBrace(content, bs);
        if (be < 0) return;
        foreach (Match m in ScriptKvRegex.Matches(content.Substring(bs + 1, be - bs - 1)))
            values[prefix + m.Groups[1].Value] = m.Groups[2].Value;
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