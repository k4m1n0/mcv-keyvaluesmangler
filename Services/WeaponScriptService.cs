using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Services;

public static class WeaponScriptService
{
    #region 字段映射表

    internal static string ReadScriptFile(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ReadScriptFile: {path}");
            return string.Empty;
        }
    }

    public enum AltStatMode { Dov, Zombie }

    private static readonly Dictionary<AltStatMode, string> AltStatBlockNames = new()
    {
        [AltStatMode.Dov] = "dov_stats",
        [AltStatMode.Zombie] = "zombie_stats",
    };

    private static readonly HashSet<string> NonFirearmTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "GrenadeLauncher", "RocketLauncher", "Melee", "Equipment",
        "SmokeGrenade", "Grenade", "RifleGrenade", "C4",
        "Crossbow", "Flaregun", "Flamethrower", "Incendiary", "Fists", "Mine"
    };

    private static readonly Dictionary<string, string> CsvToScriptMap = new()
    {
        ["SupportedFireModes"] = "SupportedFireModes",
        ["default_clip"] = "default_clip",
        ["ExtraBulletChamber"] = "ExtraBulletChamber",
        ["bullets_per_shot"] = "bullets_per_shot",
        ["FireRate"] = "FireRate",
        ["BulletSpreadDegrees"] = "BulletSpreadDegrees",
        ["BulletSpreadDegreesIronsighted"] = "BulletSpreadDegreesIronsighted",
        ["BulletSpreadDegreesBipod"] = "BulletSpreadDegreesBipod",
        ["BulletSpreadDegreesBipodIronsighted"] = "BulletSpreadDegreesBipodIronsighted",
        ["rangemodifier"] = "rangemodifier",
        ["IronsightSpeedScale"] = "IronsightSpeedScale",
        ["CrouchSpreadMultiplier"] = "CrouchSpreadMultiplier",
        ["ProneSpreadMultiplier"] = "ProneSpreadMultiplier",
        ["StandMoveSpreadMultiplier"] = "StandMoveSpreadMultiplier",
        ["SneakMoveSpreadMultiplier"] = "SneakMoveSpreadMultiplier",
        ["CrouchMoveSpreadMultiplier"] = "CrouchMoveSpreadMultiplier",
        ["JumpSpreadMultiplier"] = "JumpSpreadMultiplier",
        ["DamageHeadMultiplier"] = "DamageHeadMultiplier",
        ["DamageChestMultiplier"] = "DamageChestMultiplier",
        ["DamageStomachMultiplier"] = "DamageStomachMultiplier",
        ["DamageLegMultiplier"] = "DamageLegMultiplier",
        ["DamageArmMultiplier"] = "DamageArmMultiplier",
        ["DamageGeneric"] = "DamageGeneric",
        ["ShakeScale"] = "ShakeScale",
        ["ShakeFreq"] = "ShakeFreq",
        ["ShakeDuration"] = "ShakeDuration",
        ["CrosshairMinDistance"] = "CrosshairMinDistance",
        ["CrosshairDeltaDistance"] = "CrosshairDeltaDistance",
        ["weight"] = "weight",
        ["ZMBuyPrice"] = "ZMBuyPrice",
        ["ZMWeight"] = "ZMWeight",
        ["recoilpushbackvalue"] = "recoilpushbackvalue",
        ["ironsightwalkbobbingstrength"] = "ironsightwalkbobbingstrength",
        ["MetalPenetrationDepth"] = "MetalPenetrationDepth",
        ["GlassPenetrationDepth"] = "GlassPenetrationDepth",
        ["ConcretePenetrationDepth"] = "ConcretePenetrationDepth",
        ["WoodPenetrationDepth"] = "WoodPenetrationDepth",
        ["OtherPenetrationDepth"] = "OtherPenetrationDepth",
        ["MetalDamageModifier"] = "MetalDamageModifier",
        ["GlassDamageModifier"] = "GlassDamageModifier",
        ["ConcreteDamageModifier"] = "ConcreteDamageModifier",
        ["WoodDamageModifier"] = "WoodDamageModifier",
        ["OtherDamageModifier"] = "OtherDamageModifier",
        ["NearwallDistance"] = "NearwallDistance",
        ["clip_size"] = "clip_size",
        ["SecondaryFireRate"] = "SecondaryFireRate",
        ["IronSight"] = "IronSight",
    };

    internal static readonly Dictionary<string, Action<WeaponData, string>> FieldSetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SupportedFireModes"] = (w, v) => w.FireModes = v,
        ["default_clip"] = (w, v) => { if (int.TryParse(v, out int r)) w.DefaultClip = r; },
        ["ExtraBulletChamber"] = (w, v) => { if (int.TryParse(v, out int r)) w.ExtraBulletChamber = r; },
        ["bullets_per_shot"] = (w, v) => { if (int.TryParse(v, out int r)) w.BulletsPerShot = r; },
        ["FireRate"] = (w, v) => { if (int.TryParse(v, out int r)) w.FireRate = r; },
        ["BulletSpreadDegrees"] = (w, v) => { if (TryParseDouble(v, out double r)) w.BulletSpread = r; },
        ["BulletSpreadDegreesIronsighted"] = (w, v) => { if (TryParseDouble(v, out double r)) w.BulletSpreadDegreesIronsighted = r; },
        ["BulletSpreadDegreesBipod"] = (w, v) => { if (TryParseDouble(v, out double r)) w.BulletSpreadDegreesBipod = r; },
        ["BulletSpreadDegreesBipodIronsighted"] = (w, v) => { if (TryParseDouble(v, out double r)) w.BulletSpreadDegreesBipodIronsighted = r; },
        ["rangemodifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.RangeModifier = r; },
        ["IronsightSpeedScale"] = (w, v) => { if (TryParseDouble(v, out double r)) w.IronsightSpeedScale = r; },
        ["CrouchSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.CrouchSpreadMultiplier = r; },
        ["ProneSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ProneSpreadMultiplier = r; },
        ["StandMoveSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.StandMoveSpreadMultiplier = r; },
        ["SneakMoveSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.SneakMoveSpreadMultiplier = r; },
        ["CrouchMoveSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.CrouchMoveSpreadMultiplier = r; },
        ["JumpSpreadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.JumpSpreadMultiplier = r; },
        ["DamageHeadMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageHeadMultiplier = r; },
        ["DamageChestMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageChestMultiplier = r; },
        ["DamageStomachMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageStomachMultiplier = r; },
        ["DamageLegMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageLegMultiplier = r; },
        ["DamageArmMultiplier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageArmMultiplier = r; },
        ["DamageGeneric"] = (w, v) => { if (TryParseDouble(v, out double r)) w.DamageGeneric = r; },
        ["ShakeScale"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ShakeScale = r; },
        ["ShakeFreq"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ShakeFreq = r; },
        ["ShakeDuration"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ShakeDuration = r; },
        ["CrosshairMinDistance"] = (w, v) => { if (int.TryParse(v, out int r)) w.CrosshairMinDistance = r; },
        ["CrosshairDeltaDistance"] = (w, v) => { if (int.TryParse(v, out int r)) w.CrosshairDeltaDistance = r; },
        ["weight"] = (w, v) => { if (TryParseDouble(v, out double r)) w.Weight = r; },
        ["ZMBuyPrice"] = (w, v) => { if (int.TryParse(v, out int r)) w.ZMBuyPrice = r; },
        ["ZMWeight"] = (w, v) => { if (int.TryParse(v, out int r)) w.ZMWeight = r; },
        ["recoilpushbackvalue"] = (w, v) => { if (TryParseDouble(v, out double r)) w.RecoilPushbackValue = r; },
        ["ironsightwalkbobbingstrength"] = (w, v) => { if (TryParseDouble(v, out double r)) w.IronsightWalkBobbingStrength = r; },
        ["MetalPenetrationDepth"] = (w, v) => { if (TryParseDouble(v, out double r)) w.MetalPenetrationDepth = r; },
        ["GlassPenetrationDepth"] = (w, v) => { if (TryParseDouble(v, out double r)) w.GlassPenetrationDepth = r; },
        ["ConcretePenetrationDepth"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ConcretePenetrationDepth = r; },
        ["WoodPenetrationDepth"] = (w, v) => { if (TryParseDouble(v, out double r)) w.WoodPenetrationDepth = r; },
        ["OtherPenetrationDepth"] = (w, v) => { if (TryParseDouble(v, out double r)) w.OtherPenetrationDepth = r; },
        ["MetalDamageModifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.MetalDamageModifier = r; },
        ["GlassDamageModifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.GlassDamageModifier = r; },
        ["ConcreteDamageModifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.ConcreteDamageModifier = r; },
        ["WoodDamageModifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.WoodDamageModifier = r; },
        ["OtherDamageModifier"] = (w, v) => { if (TryParseDouble(v, out double r)) w.OtherDamageModifier = r; },
        ["NearwallDistance"] = (w, v) => { if (int.TryParse(v, out int r)) w.NearwallDistance = r; },
        ["primary_ammo"] = (w, v) => w.PrimaryAmmo = v,
        ["clip_size"] = (w, v) => w.ClipSize = v,
        ["SecondaryFireRate"] = (w, v) => { if (int.TryParse(v, out int r)) w.SecondaryFireRate = r; },
        ["IronSight"] = (w, v) => { if (int.TryParse(v, out int r)) w.IronSight = r; },
    };

    private static Dictionary<string, string> LoadPrintNameMap(string csvPath)
    {
        var map = new Dictionary<string, string>();
        if (!File.Exists(csvPath)) return map;

        try
        {
            var weapons = CsvService.LoadWeapons(csvPath);
            foreach (var w in weapons)
            {
                if (!string.IsNullOrEmpty(w.ScriptName) && !string.IsNullOrEmpty(w.PrintName))
                    map[w.ScriptName] = w.PrintName;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponScriptService.LoadPrintNameMap");
        }
        return map;
    }

    #endregion
    #region 公共工具

    //解析WeaponData块的顶层键值对 只收集不嵌套在子块内的键
    public static Dictionary<string, string> ParseWeaponDataPairs(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int wd = content.IndexOf("WeaponData", StringComparison.Ordinal);
            if (wd < 0) return values;
            int bs = content.IndexOf('{', wd);
            if (bs < 0) return values;
            int be = FindMatchingBrace(content, bs);
            if (be < 0 || bs + 1 >= be) return values;
            string block = content.Substring(bs + 1, be - bs - 1);

            foreach (Match m in Regex.Matches(block, @"""([^""]+)""\s+""([^""]*)""", RegexOptions.Multiline))
            {
                string before = block.Substring(0, m.Index);
                int ob = 0, cb = 0;
                for (int j = 0; j < before.Length; j++)
                { if (before[j] == '{') ob++; else if (before[j] == '}') cb++; }
                if (ob == cb) values[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponScriptService.ParseWeaponDataPairs");
        }
        return values;
    }

    //大括号匹配 忽略字符串内的{}
    public static int FindMatchingBrace(string text, int start)
    {
        int depth = 0; bool inStr = false;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\')) inStr = !inStr;
            if (!inStr) { if (text[i] == '{') depth++; else if (text[i] == '}') { depth--; if (depth == 0) return i; } }
        }
        return -1;
    }

    public static string FormatDouble(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

    public static string FormatClipSize(string raw, string extraChamber)
    {
        if (string.IsNullOrEmpty(raw) || raw == "-1" || raw == "-1/-1" || raw == "0/0") return "N/A";
        if (!raw.Contains('/')) return raw;
        var parts = raw.Split('/');
        string marker = extraChamber == "1" ? "[[+1]]" : "";
        return $"{parts[0].Trim()}{marker} / {parts[1].Trim()}";
    }

    public static double GetDoubleVal(Dictionary<string, string> vals, string key) =>
        vals.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    #endregion
    #region 导出导入

    public static string ExportCsvToScripts(string csvFilePath, string scriptsDir)
    {
        var log = new List<string>();
        LogService.Info($"ExportCsvToScripts: {csvFilePath} -> {scriptsDir}");
        var weapons = CsvService.LoadWeapons(csvFilePath);
        int total = weapons.Count;
        int success = 0;
        int skipped = 0;
        int skippedNoScript = 0;
        int skippedEmptyName = 0;

        log.Add($"CSV -> 脚本导出");
        log.Add($"CSV: {csvFilePath}");
        log.Add($"目标目录: {scriptsDir}");
        log.Add($"共 {total} 把武器");
        log.Add(new string('-', 50));

        for (int i = 0; i < weapons.Count; i++)
        {
            var weapon = weapons[i];
            string scriptName = weapon.ScriptName;

            if (string.IsNullOrEmpty(scriptName))
            {
                skipped++;
                skippedEmptyName++;
                continue;
            }

            string scriptPath = Path.Combine(scriptsDir, scriptName);

            if (!File.Exists(scriptPath))
            {
                skipped++;
                skippedNoScript++;
                LogService.Warn($"ExportCsvToScripts: script not found: {scriptName}");
                continue;
            }

            try
            {
                string content = ReadScriptFile(scriptPath);
                int updated = 0;

                foreach (var map in CsvToScriptMap)
                {
                    string? csvValue = GetFieldValue(weapon, map.Key, null);
                    if (csvValue == null) continue;
                    string newContent = ReplaceKeyValue(content, map.Value, csvValue);
                    if (newContent != content) { content = newContent; updated++; }
                }

                string? ru = GetFieldValue(weapon, "ViewSlideRecoil.Up", null);
                string? rr = GetFieldValue(weapon, "ViewSlideRecoil.Right", null);
                if (ru != null || rr != null)
                {
                    content = ReplaceRecoilBlock(content, "ViewSlideRecoil", ru, rr);
                    updated++;
                }

                string? au = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Up", null);
                string? ar = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Right", null);
                if (au != null || ar != null)
                {
                    content = ReplaceRecoilBlock(content, "ViewSlideRecoilIronsight", au, ar);
                    updated++;
                }

                File.WriteAllText(scriptPath, content, new UTF8Encoding(false));
                success++;
                log.Add($"[{i + 1}/{total}] {scriptName} ({updated} 字段)");
            }
            catch (Exception ex)
            {
                skipped++;
                log.Add($"[{i + 1}/{total}] 失败: {scriptName} - {ex.Message}");
                LogService.Error(ex, $"ExportCsvToScripts: {scriptName}");
            }
        }

        log.Add(new string('-', 50));
        log.Add($"完成: 成功 {success}, 跳过 {skipped} (空名{skippedEmptyName} 无脚本{skippedNoScript}), 总计 {total}");
        string result = string.Join("\n", log);
        LogService.Info($"ExportCsvToScripts done: {success} ok, {skipped} skip, {total} total");
        return result;
    }

    public static void ExportAltStatsToScripts(string csvFilePath, string scriptsDir, AltStatMode mode)
    {
        string blockName = AltStatBlockNames[mode];
        LogService.Info($"ExportAltStatsToScripts: {csvFilePath} -> {scriptsDir}, mode={mode}");
        var weapons = CsvService.LoadWeapons(csvFilePath);
        int updated = 0, skipped = 0;

        foreach (var weapon in weapons)
        {
            if (string.IsNullOrEmpty(weapon.ScriptName)) continue;
            string scriptPath = Path.Combine(scriptsDir, weapon.ScriptName);
            if (!File.Exists(scriptPath)) continue;
            try
            {
                string content = ReadScriptFile(scriptPath);

                bool hasAnyDiff = CsvToScriptMap.Keys.Any(k => GetFieldValue(weapon, k, mode) != null)
                    || GetFieldValue(weapon, "ViewSlideRecoil.Up", mode) != null
                    || GetFieldValue(weapon, "ViewSlideRecoilIronsight.Up", mode) != null;

                if (!content.Contains(blockName) && !content.Contains($"//{blockName}"))
                {
                    if (!hasAnyDiff) continue;
                    content = InsertAltStatBlock(content, blockName, mode);
                }

                content = ToggleAltStatBlockComment(content, mode, hasAnyDiff);
                if (!content.Contains(blockName)) continue;

                var blockMatch = Regex.Match(content, $@"{Regex.Escape(blockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
                if (!blockMatch.Success) continue;
                string fullBlock = blockMatch.Value.Replace("\r\n", "\n").Replace('\r', '\n');
                string originalBlock = fullBlock;

                foreach (var map in CsvToScriptMap)
                    fullBlock = ApplyKeyToBlock(fullBlock, map.Value, GetFieldValue(weapon, map.Key, mode) ?? "");

                if (fullBlock != originalBlock)
                    content = content.Replace(originalBlock, fullBlock);

                foreach (var recoil in new[] { "ViewSlideRecoil", "ViewSlideRecoilIronsight" })
                {
                    string? up = GetFieldValue(weapon, $"{recoil}.Up", mode);
                    string? right = GetFieldValue(weapon, $"{recoil}.Right", mode);
                    if (up != null || right != null)
                        content = WriteAltStatRecoilBlock(content, recoil, up, right, mode);
                }

                File.WriteAllText(scriptPath, content, new UTF8Encoding(false));
                updated++;
            }
            catch (Exception ex)
            {
                skipped++;
                LogService.Error(ex, $"ExportAltStatsToScripts: {weapon.ScriptName}");
            }
        }
        LogService.Info($"ExportAltStatsToScripts done: {updated} updated, {skipped} errors");
    }

    private static string InsertAltStatBlock(string content, string blockName, AltStatMode mode)
    {
        int insertIdx = content.LastIndexOf("\"ZMWeight\"", StringComparison.Ordinal);
        if (insertIdx < 0) insertIdx = content.LastIndexOf("// Day of Victory", StringComparison.Ordinal);
        if (insertIdx < 0) insertIdx = content.LastIndexOf("//---", StringComparison.Ordinal);
        if (insertIdx < 0) return content;

        int lineEnd = content.IndexOf('\n', insertIdx);
        if (lineEnd < 0) return content;

        int lineStart = content.LastIndexOf('\n', insertIdx);
        string indent = "\t";
        if (lineStart >= 0)
        {
            string refLine = content.Substring(lineStart + 1, insertIdx - lineStart - 1);
            indent = refLine.Length - refLine.TrimStart().Length > 0
                ? refLine.Substring(0, refLine.Length - refLine.TrimStart().Length)
                : "\t";
        }
        string modeLabel = mode == AltStatMode.Dov ? "DoV" : "Zombie Mode";
        return content.Insert(lineEnd + 1, $"\n{indent}// if anything is modified for {modeLabel}:\n{indent}{blockName}\n{indent}{{\n{indent}}}\n{indent}//---\n");
    }

    //正则地狱
    public static string ImportScriptsToCsv(string scriptsDir, string outputCsvPath)
    {
        var log = new List<string>();
        LogService.Info($"ImportScriptsToCsv: {scriptsDir} -> {outputCsvPath}");

        if (!Directory.Exists(scriptsDir))
        {
            LogService.Error($"ImportScriptsToCsv: directory not found: {scriptsDir}");
            return $"错误: 目录不存在 - {scriptsDir}";
        }

        var oldPrintNames = LoadPrintNameMap(outputCsvPath);

        string[] files = Directory.GetFiles(scriptsDir, "*.txt");
        var list = new List<WeaponData>();
        int total = files.Length;
        int success = 0, failed = 0, skipped = 0;

        log.Add($"脚本 -> CSV 导入");
        log.Add($"目录: {scriptsDir}");
        log.Add($"共 {total} 个文件");
        log.Add(new string('-', 50));

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);

            try
            {
                string content = ReadScriptFile(path);

                if (!IsStandardFirearm(name, content))
                {
                    skipped++;
                    log.Add($"[{i + 1}/{total}] 跳过(非枪械): {name}");
                    continue;
                }

                var weapon = new WeaponData { ScriptName = name };
                int read = 0;

                foreach (var s in FieldSetters)
                {
                    string? val = ExtractValue(content, s.Key);
                    if (val == null) continue;
                    s.Value(weapon, val);
                    read++;
                }

                if (oldPrintNames.TryGetValue(name, out var pn))
                    weapon.PrintName = pn;
                else
                {
                    var m = Regex.Match(content, @"""printname""\s+""([^""]*)""");
                    if (m.Success) weapon.PrintName = m.Groups[1].Value;
                }

                weapon.ViewSlideRecoilUp = ParseRecoilBlock(content, "ViewSlideRecoil", "Up");
                weapon.ViewSlideRecoilRight = ParseRecoilBlock(content, "ViewSlideRecoil", "Right");
                weapon.ViewSlideRecoilIronsightUp = ParseRecoilBlock(content, "ViewSlideRecoilIronsight", "Up");
                weapon.ViewSlideRecoilIronsightRight = ParseRecoilBlock(content, "ViewSlideRecoilIronsight", "Right");

                ImportAltStatBlock(weapon, content, AltStatMode.Dov);
                ImportAltStatBlock(weapon, content, AltStatMode.Zombie);

                list.Add(weapon);
                success++;
                log.Add($"[{i + 1}/{total}] {name} ({read} 字段)");
            }
            catch (Exception ex)
            {
                failed++;
                log.Add($"[{i + 1}/{total}] 失败: {name} - {ex.GetType().Name}: {ex.Message}");
                LogService.Error(ex, $"ImportScriptsToCsv: {name}");
            }
        }

        log.Add(new string('-', 50));
        log.Add($"解析完成: 成功 {success}, 失败 {failed}, 跳过 {skipped}");

        if (success > 0 && File.Exists(outputCsvPath))
        {
            try
            {
                var oldWeapons = CsvService.LoadWeapons(outputCsvPath);
                var oldOrder = oldWeapons.Select(w => w.ScriptName).ToList();
                if (oldOrder.Count > 0)
                {
                    var dict = list.ToDictionary(w => w.ScriptName);
                    var ordered = new List<WeaponData>();
                    foreach (var sn in oldOrder)
                    {
                        if (dict.TryGetValue(sn, out var w))
                        {
                            ordered.Add(w);
                            dict.Remove(sn);
                        }
                    }
                    ordered.AddRange(dict.Values);
                    list = ordered;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "ImportScriptsToCsv: failed to restore old weapon order");
            }
        }

        try
        {
            CsvService.SaveWeapons(outputCsvPath, list);
            log.Add($"保存完成: 共 {list.Count} 把武器写入 CSV");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ImportScriptsToCsv: CSV save failed");
            log.Add($"CSV保存失败: {ex.Message}");
        }

        string result = string.Join("\n", log);
        LogService.Info($"ImportScriptsToCsv done: {success} ok, {failed} fail, {skipped} skip, {list.Count} total");
        return result;
    }

    public static string? ReadAltStatBlockValue(string content, string key, AltStatMode mode)
    {
        try
        {
            string blockName = AltStatBlockNames[mode];
            var blockMatch = Regex.Match(content, $@"{Regex.Escape(blockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
            if (!blockMatch.Success) return null;
            return ExtractValue(blockMatch.Groups[1].Value, key);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ReadAltStatBlockValue: key={key}, mode={mode}");
            return null;
        }
    }

    public static string WriteAltStatBlockValue(string content, string key, string value, AltStatMode mode)
    {
        try
        {
        string blockName = AltStatBlockNames[mode];
        var blockMatch = Regex.Match(content, $@"{Regex.Escape(blockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
        if (!blockMatch.Success) return content;
        string fullBlock = blockMatch.Value.Replace("\r\n", "\n").Replace('\r', '\n');
        string newFullBlock = ApplyKeyToBlock(fullBlock, key, value);
        if (newFullBlock == fullBlock) return content;
        return content.Replace(fullBlock, newFullBlock);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WriteAltStatBlockValue: key={key}, mode={mode}");
            return content;
        }
    }

    public static string WriteAltStatRecoilBlock(string content, string recoilBlock, string? up, string? right, AltStatMode mode)
    {
        try
        {
        string blockName = AltStatBlockNames[mode];
        var altMatch = Regex.Match(content, $@"{Regex.Escape(blockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
        if (!altMatch.Success) return content;
        string altBlock = altMatch.Groups[1].Value;
        string newAltBlock = ReplaceRecoilBlock(altBlock, recoilBlock, up, right);
        if (newAltBlock == altBlock) return content;
        return content.Replace(altBlock, newAltBlock);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WriteAltStatRecoilBlock: recoilBlock={recoilBlock}, mode={mode}");
            return content;
        }
    }

    //如果所有备选值与顶层一致就注释掉整个备选块 如果有不同则激活
    public static string ToggleAltStatBlockComment(string content, AltStatMode mode, bool hasAnyDiff)
    {
        try
        {
        string blockName = AltStatBlockNames[mode];
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>();
        int i = 0;
        bool changed = false;

        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();
            string indent = line.Length - trimmed.Length > 0 ? line.Substring(0, line.Length - trimmed.Length) : "\t";

            bool isBlockLine = trimmed == blockName || trimmed == $"//{blockName}";
            if (isBlockLine)
            {
                int j = i + 1;
                while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                if (j < lines.Length)
                {
                    string nextTrimmed = lines[j].TrimStart();
                    string braceIndent = lines[j].Length - nextTrimmed.Length > 0
                        ? lines[j].Substring(0, lines[j].Length - nextTrimmed.Length) : indent;
                    bool openCommented = nextTrimmed == "//{" || nextTrimmed.StartsWith("//{");
                    bool openActive = nextTrimmed == "{";

                    if (openCommented || openActive)
                    {
                        int k = j + 1;
                        while (k < lines.Length)
                        {
                            string kt = lines[k].TrimStart();
                            if (kt == "}" || kt == "//}")
                                break;
                            k++;
                        }

                        if (hasAnyDiff)
                        {
                            result.Add($"{indent}{blockName}");
                            result.Add($"{braceIndent}{{");
                            for (int m = j + 1; m < k; m++)
                                result.Add(lines[m]);
                            result.Add($"{braceIndent}}}");
                            changed = true;
                            i = k + 1;
                            continue;
                        }
                        else
                        {
                            result.Add($"{indent}//{blockName}");
                            result.Add($"{braceIndent}//{{");
                            result.Add($"{braceIndent}//}}");
                            changed = true;
                            i = k + 1;
                            continue;
                        }
                    }
                }
            }
            result.Add(line);
            i++;
        }

        return changed ? string.Join("\n", result) : content;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ToggleAltStatBlockComment: mode={mode}");
            return content;
        }
    }

    private static string ApplyKeyToBlock(string fullBlock, string key, string value)//屎
    {
        int bracePos = fullBlock.IndexOf('{');
        int closePos = fullBlock.LastIndexOf('}');
        if (bracePos < 0 || closePos <= bracePos) return fullBlock;

        string beforeBrace = fullBlock.Substring(0, bracePos + 1);
        string blockContent = fullBlock.Substring(bracePos + 1, closePos - bracePos - 1);
        int afterLineStart = fullBlock.LastIndexOf('\n', closePos);
        string afterBrace = afterLineStart >= 0
            ? fullBlock.Substring(afterLineStart)
            : fullBlock.Substring(closePos);

        LogService.Debug($"ApplyKeyToBlock: key={key}, value={value}, beforeBrace=[{beforeBrace.Replace("\n","\\n").Replace("\t","\\t")}], blockContent=[{blockContent.Replace("\n","\\n").Replace("\t","\\t")}], afterBrace=[{afterBrace.Replace("\n","\\n").Replace("\t","\\t")}]");

        string deletePattern = $@"^[ \t]*""{Regex.Escape(key)}""\s+""[^""]*""\s*(?://.*)?(\r?\n)?";
        blockContent = Regex.Replace(blockContent, deletePattern, "", RegexOptions.Multiline);
        blockContent = blockContent.TrimEnd('\n', '\r', ' ', '\t');

        LogService.Debug($"ApplyKeyToBlock after delete+trim: blockContent=[{blockContent.Replace("\n","\\n").Replace("\t","\\t")}]");

        if (!string.IsNullOrEmpty(value))
        {
            int lineStart = fullBlock.LastIndexOf('\n', bracePos);
            string braceLine = lineStart >= 0
                ? fullBlock.Substring(lineStart + 1, bracePos - lineStart - 1)
                : fullBlock.Substring(0, bracePos);
            string braceIndent = braceLine.Length - braceLine.TrimStart().Length > 0
                ? braceLine.Substring(0, braceLine.Length - braceLine.TrimStart().Length)
                : "\t";
            string indent = braceIndent + "\t";
            blockContent = $"\n{indent}\"{key}\"\t\t\t\t\"{value}\"" + blockContent;
        }

        string result = beforeBrace + blockContent + afterBrace;
        result = Regex.Replace(result, @"(\n\s*){3,}", "\n\n");
        LogService.Debug($"ApplyKeyToBlock result: [{result.Replace("\n","\\n").Replace("\t","\\t")}]");
        return result;
    }

    #endregion
    #region 脚本解析

    private static bool IsStandardFirearm(string name, string content)
    {
        if (name.Contains("_zombie", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("weapon_cubemap", StringComparison.OrdinalIgnoreCase)) return false;
        var type = ExtractValue(content, "WeaponType");
        if (!string.IsNullOrEmpty(type) && NonFirearmTypes.Contains(type)) return false;
        if (ExtractValue(content, "ExplosionDamage") != null) return false;
        if (ExtractValue(content, "DamageGeneric") == null) return false;
        return true;
    }

    //先尝试带引号的值 再尝试不带引号的裸值 只匹配行首非注释的键 防止把注释掉的kv误识别
    private static string? ExtractValue(string content, string key)
    {
        //^[ \t]* 确保key在行首且前面没有// 防止匹配到注释块内被注释掉的键
        var m = Regex.Match(content, $@"^[ \t]*""{Regex.Escape(key)}""\s+""([^""]*)""", RegexOptions.Multiline);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(content, $@"^[ \t]*""{Regex.Escape(key)}""\s+(\S+)", RegexOptions.Multiline);//回退匹配"abc" 123这种不带引号的裸值
        if (m.Success)
        {
            string v = m.Groups[1].Value;
            if (v.StartsWith("\"") && v.EndsWith("\"")) v = v.Substring(1, v.Length - 2);
            return v;
        }
        return null;
    }

    private static bool TryParseDouble(string v, out double r) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r);

    //解析后座块 用Singleline跨越换行
    internal static double? ParseRecoilBlock(string content, string block, string key)
    {
        var m = Regex.Match(content, $@"{Regex.Escape(block)}\s*\{{[^}}]*""{Regex.Escape(key)}""\s+""([^""]*)""", RegexOptions.Singleline);//匹配block名后紧跟的大括号内容 取指定key的双引号
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
            return r;
        return null;
    }

    private static double? ParseRecoilBlockInAltStat(string altBlock, string blockName, string key)
    {
        int idx = altBlock.IndexOf(blockName, StringComparison.Ordinal);
        if (idx < 0) return null;
        int braceStart = altBlock.IndexOf('{', idx);
        if (braceStart < 0) return null;
        int depth = 1;
        int i = braceStart + 1;
        while (i < altBlock.Length && depth > 0)
        {
            if (altBlock[i] == '{') depth++;
            else if (altBlock[i] == '}') depth--;
            i++;
        }
        string blockContent = altBlock.Substring(braceStart + 1, i - braceStart - 2);
        var m = Regex.Match(blockContent, $@"""{Regex.Escape(key)}""\s+""([^""]*)""");
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
            return r;
        return null;
    }

    private static void ImportAltStatBlock(WeaponData w, string content, AltStatMode mode)
    {
        try
        {
            string blockName = AltStatBlockNames[mode];
            int dovIdx = content.IndexOf(blockName, StringComparison.Ordinal);
            if (dovIdx < 0) return;
            int braceStart = content.IndexOf('{', dovIdx);
            if (braceStart < 0) return;
            int depth = 1;
            int i = braceStart + 1;
            while (i < content.Length && depth > 0)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}') depth--;
                i++;
            }
            string block = content.Substring(braceStart + 1, i - braceStart - 2);

            if (mode == AltStatMode.Dov)
            {
                TrySetInt(block, "ExtraBulletChamber", out int? i1); w.DovExtraBulletChamber = i1;
                TrySetInt(block, "FireRate", out int? i2); w.DovFireRate = i2;
                TrySetDouble(block, "BulletSpreadDegrees", out double? d1); w.DovBulletSpread = d1;
                TrySetDouble(block, "BulletSpreadDegreesIronsighted", out double? d2); w.DovBulletSpreadDegreesIronsighted = d2;
                TrySetDouble(block, "BulletSpreadDegreesBipod", out double? d18); w.DovBulletSpreadDegreesBipod = d18;
                TrySetDouble(block, "BulletSpreadDegreesBipodIronsighted", out double? d19); w.DovBulletSpreadDegreesBipodIronsighted = d19;
                TrySetDouble(block, "rangemodifier", out double? d3); w.DovRangeModifier = d3;
                TrySetDouble(block, "IronsightSpeedScale", out double? d4); w.DovIronsightSpeedScale = d4;
                TrySetDouble(block, "CrouchSpreadMultiplier", out double? d5); w.DovCrouchSpreadMultiplier = d5;
                TrySetDouble(block, "ProneSpreadMultiplier", out double? d6); w.DovProneSpreadMultiplier = d6;
                TrySetDouble(block, "StandMoveSpreadMultiplier", out double? d7); w.DovStandMoveSpreadMultiplier = d7;
                TrySetDouble(block, "SneakMoveSpreadMultiplier", out double? d8); w.DovSneakMoveSpreadMultiplier = d8;
                TrySetDouble(block, "CrouchMoveSpreadMultiplier", out double? d9); w.DovCrouchMoveSpreadMultiplier = d9;
                TrySetDouble(block, "JumpSpreadMultiplier", out double? d10); w.DovJumpSpreadMultiplier = d10;
                TrySetDouble(block, "DamageHeadMultiplier", out double? d11); w.DovDamageHeadMultiplier = d11;
                TrySetDouble(block, "DamageChestMultiplier", out double? d12); w.DovDamageChestMultiplier = d12;
                TrySetDouble(block, "DamageStomachMultiplier", out double? d13); w.DovDamageStomachMultiplier = d13;
                TrySetDouble(block, "DamageLegMultiplier", out double? d14); w.DovDamageLegMultiplier = d14;
                TrySetDouble(block, "DamageArmMultiplier", out double? d15); w.DovDamageArmMultiplier = d15;
                TrySetDouble(block, "DamageGeneric", out double? d16); w.DovDamageGeneric = d16;
                TrySetDouble(block, "ShakeScale", out double? d20); w.DovShakeScale = d20;
                TrySetDouble(block, "ShakeFreq", out double? d21); w.DovShakeFreq = d21;
                TrySetDouble(block, "ShakeDuration", out double? d22); w.DovShakeDuration = d22;
                TrySetInt(block, "CrosshairMinDistance", out int? i3); w.DovCrosshairMinDistance = i3;
                TrySetInt(block, "CrosshairDeltaDistance", out int? i4); w.DovCrosshairDeltaDistance = i4;
                TrySetDouble(block, "weight", out double? d17); w.DovWeight = d17;
                TrySetInt(block, "ZMBuyPrice", out int? i5); w.DovZMBuyPrice = i5;
                TrySetInt(block, "ZMWeight", out int? i6); w.DovZMWeight = i6;
                TrySetDouble(block, "recoilpushbackvalue", out double? d23); w.DovRecoilPushbackValue = d23;
                TrySetDouble(block, "ironsightwalkbobbingstrength", out double? d24); w.DovIronsightWalkBobbingStrength = d24;
                TrySetDouble(block, "MetalPenetrationDepth", out double? d25); w.DovMetalPenetrationDepth = d25;
                TrySetDouble(block, "GlassPenetrationDepth", out double? d26); w.DovGlassPenetrationDepth = d26;
                TrySetDouble(block, "ConcretePenetrationDepth", out double? d27); w.DovConcretePenetrationDepth = d27;
                TrySetDouble(block, "WoodPenetrationDepth", out double? d28); w.DovWoodPenetrationDepth = d28;
                TrySetDouble(block, "OtherPenetrationDepth", out double? d29); w.DovOtherPenetrationDepth = d29;
                TrySetDouble(block, "MetalDamageModifier", out double? d30); w.DovMetalDamageModifier = d30;
                TrySetDouble(block, "GlassDamageModifier", out double? d31); w.DovGlassDamageModifier = d31;
                TrySetDouble(block, "ConcreteDamageModifier", out double? d32); w.DovConcreteDamageModifier = d32;
                TrySetDouble(block, "WoodDamageModifier", out double? d33); w.DovWoodDamageModifier = d33;
                TrySetDouble(block, "OtherDamageModifier", out double? d34); w.DovOtherDamageModifier = d34;
                TrySetInt(block, "NearwallDistance", out int? i7); w.DovNearwallDistance = i7;
                w.DovFireModes = ExtractValue(block, "SupportedFireModes") ?? "";
                w.DovClipSize = ExtractValue(block, "clip_size") ?? "";
                w.DovViewSlideRecoilUp = ParseRecoilBlockInAltStat(block, "ViewSlideRecoil", "Up");
                w.DovViewSlideRecoilRight = ParseRecoilBlockInAltStat(block, "ViewSlideRecoil", "Right");
                w.DovViewSlideRecoilIronsightUp = ParseRecoilBlockInAltStat(block, "ViewSlideRecoilIronsight", "Up");
                w.DovViewSlideRecoilIronsightRight = ParseRecoilBlockInAltStat(block, "ViewSlideRecoilIronsight", "Right");
                TrySetInt(block, "SecondaryFireRate", out int? i8); w.DovSecondaryFireRate = i8;
                TrySetInt(block, "IronSight", out int? i9); w.DovIronSight = i9;
                TrySetInt(block, "default_clip", out int? id0); w.DovDefaultClip = id0;
                TrySetInt(block, "bullets_per_shot", out int? id1); w.DovBulletsPerShot = id1;
            }
            else
            {
                TrySetInt(block, "ExtraBulletChamber", out int? i1); w.ZombieExtraBulletChamber = i1;
                TrySetInt(block, "FireRate", out int? i2); w.ZombieFireRate = i2;
                TrySetDouble(block, "BulletSpreadDegrees", out double? d1); w.ZombieBulletSpread = d1;
                TrySetDouble(block, "BulletSpreadDegreesIronsighted", out double? d2); w.ZombieBulletSpreadDegreesIronsighted = d2;
                TrySetDouble(block, "BulletSpreadDegreesBipod", out double? d18); w.ZombieBulletSpreadDegreesBipod = d18;
                TrySetDouble(block, "BulletSpreadDegreesBipodIronsighted", out double? d19); w.ZombieBulletSpreadDegreesBipodIronsighted = d19;
                TrySetDouble(block, "rangemodifier", out double? d3); w.ZombieRangeModifier = d3;
                TrySetDouble(block, "IronsightSpeedScale", out double? d4); w.ZombieIronsightSpeedScale = d4;
                TrySetDouble(block, "CrouchSpreadMultiplier", out double? d5); w.ZombieCrouchSpreadMultiplier = d5;
                TrySetDouble(block, "ProneSpreadMultiplier", out double? d6); w.ZombieProneSpreadMultiplier = d6;
                TrySetDouble(block, "StandMoveSpreadMultiplier", out double? d7); w.ZombieStandMoveSpreadMultiplier = d7;
                TrySetDouble(block, "SneakMoveSpreadMultiplier", out double? d8); w.ZombieSneakMoveSpreadMultiplier = d8;
                TrySetDouble(block, "CrouchMoveSpreadMultiplier", out double? d9); w.ZombieCrouchMoveSpreadMultiplier = d9;
                TrySetDouble(block, "JumpSpreadMultiplier", out double? d10); w.ZombieJumpSpreadMultiplier = d10;
                TrySetDouble(block, "DamageHeadMultiplier", out double? d11); w.ZombieDamageHeadMultiplier = d11;
                TrySetDouble(block, "DamageChestMultiplier", out double? d12); w.ZombieDamageChestMultiplier = d12;
                TrySetDouble(block, "DamageStomachMultiplier", out double? d13); w.ZombieDamageStomachMultiplier = d13;
                TrySetDouble(block, "DamageLegMultiplier", out double? d14); w.ZombieDamageLegMultiplier = d14;
                TrySetDouble(block, "DamageArmMultiplier", out double? d15); w.ZombieDamageArmMultiplier = d15;
                TrySetDouble(block, "DamageGeneric", out double? d16); w.ZombieDamageGeneric = d16;
                TrySetDouble(block, "ShakeScale", out double? d20); w.ZombieShakeScale = d20;
                TrySetDouble(block, "ShakeFreq", out double? d21); w.ZombieShakeFreq = d21;
                TrySetDouble(block, "ShakeDuration", out double? d22); w.ZombieShakeDuration = d22;
                TrySetInt(block, "CrosshairMinDistance", out int? i3); w.ZombieCrosshairMinDistance = i3;
                TrySetInt(block, "CrosshairDeltaDistance", out int? i4); w.ZombieCrosshairDeltaDistance = i4;
                TrySetDouble(block, "weight", out double? d17); w.ZombieWeight = d17;
                TrySetInt(block, "ZMBuyPrice", out int? i5); w.ZombieZMBuyPrice = i5;
                TrySetInt(block, "ZMWeight", out int? i6); w.ZombieZMWeight = i6;
                TrySetDouble(block, "recoilpushbackvalue", out double? d23); w.ZombieRecoilPushbackValue = d23;
                TrySetDouble(block, "ironsightwalkbobbingstrength", out double? d24); w.ZombieIronsightWalkBobbingStrength = d24;
                TrySetDouble(block, "MetalPenetrationDepth", out double? d25); w.ZombieMetalPenetrationDepth = d25;
                TrySetDouble(block, "GlassPenetrationDepth", out double? d26); w.ZombieGlassPenetrationDepth = d26;
                TrySetDouble(block, "ConcretePenetrationDepth", out double? d27); w.ZombieConcretePenetrationDepth = d27;
                TrySetDouble(block, "WoodPenetrationDepth", out double? d28); w.ZombieWoodPenetrationDepth = d28;
                TrySetDouble(block, "OtherPenetrationDepth", out double? d29); w.ZombieOtherPenetrationDepth = d29;
                TrySetDouble(block, "MetalDamageModifier", out double? d30); w.ZombieMetalDamageModifier = d30;
                TrySetDouble(block, "GlassDamageModifier", out double? d31); w.ZombieGlassDamageModifier = d31;
                TrySetDouble(block, "ConcreteDamageModifier", out double? d32); w.ZombieConcreteDamageModifier = d32;
                TrySetDouble(block, "WoodDamageModifier", out double? d33); w.ZombieWoodDamageModifier = d33;
                TrySetDouble(block, "OtherDamageModifier", out double? d34); w.ZombieOtherDamageModifier = d34;
                TrySetInt(block, "NearwallDistance", out int? i7); w.ZombieNearwallDistance = i7;
                w.ZombieFireModes = ExtractValue(block, "SupportedFireModes") ?? "";
                w.ZombieClipSize = ExtractValue(block, "clip_size") ?? "";
                w.ZombieViewSlideRecoilUp = ParseRecoilBlockInAltStat(block, "ViewSlideRecoil", "Up");
                w.ZombieViewSlideRecoilRight = ParseRecoilBlockInAltStat(block, "ViewSlideRecoil", "Right");
                w.ZombieViewSlideRecoilIronsightUp = ParseRecoilBlockInAltStat(block, "ViewSlideRecoilIronsight", "Up");
                w.ZombieViewSlideRecoilIronsightRight = ParseRecoilBlockInAltStat(block, "ViewSlideRecoilIronsight", "Right");
                TrySetInt(block, "SecondaryFireRate", out int? i8); w.ZombieSecondaryFireRate = i8;
                TrySetInt(block, "IronSight", out int? i9); w.ZombieIronSight = i9;
                TrySetInt(block, "default_clip", out int? iz0); w.ZombieDefaultClip = iz0;
                TrySetInt(block, "bullets_per_shot", out int? iz1); w.ZombieBulletsPerShot = iz1;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ImportAltStatBlock: mode={mode}");
        }
    }

    #endregion
    #region 字段读写替换

    private static string? GetFieldValue(WeaponData w, string h, AltStatMode? mode) => h switch
    {
        "SupportedFireModes" => AltS(w.FireModes, w.DovFireModes, w.ZombieFireModes, mode),
        "default_clip" => AltI(w.DefaultClip, w.DovDefaultClip, w.ZombieDefaultClip, mode),
        "ExtraBulletChamber" => AltI(w.ExtraBulletChamber, w.DovExtraBulletChamber, w.ZombieExtraBulletChamber, mode),
        "bullets_per_shot" => AltI(w.BulletsPerShot, w.DovBulletsPerShot, w.ZombieBulletsPerShot, mode),
        "FireRate" => AltI(w.FireRate, w.DovFireRate, w.ZombieFireRate, mode),
        "BulletSpreadDegrees" => AltF(w.BulletSpread, w.DovBulletSpread, w.ZombieBulletSpread, mode),
        "BulletSpreadDegreesIronsighted" => AltF(w.BulletSpreadDegreesIronsighted, w.DovBulletSpreadDegreesIronsighted, w.ZombieBulletSpreadDegreesIronsighted, mode),
        "BulletSpreadDegreesBipod" => AltF(w.BulletSpreadDegreesBipod, w.DovBulletSpreadDegreesBipod, w.ZombieBulletSpreadDegreesBipod, mode),
        "BulletSpreadDegreesBipodIronsighted" => AltF(w.BulletSpreadDegreesBipodIronsighted, w.DovBulletSpreadDegreesBipodIronsighted, w.ZombieBulletSpreadDegreesBipodIronsighted, mode),
        "rangemodifier" => AltF(w.RangeModifier, w.DovRangeModifier, w.ZombieRangeModifier, mode),
        "IronsightSpeedScale" => AltF(w.IronsightSpeedScale, w.DovIronsightSpeedScale, w.ZombieIronsightSpeedScale, mode),
        "CrouchSpreadMultiplier" => AltF(w.CrouchSpreadMultiplier, w.DovCrouchSpreadMultiplier, w.ZombieCrouchSpreadMultiplier, mode),
        "ProneSpreadMultiplier" => AltF(w.ProneSpreadMultiplier, w.DovProneSpreadMultiplier, w.ZombieProneSpreadMultiplier, mode),
        "StandMoveSpreadMultiplier" => AltF(w.StandMoveSpreadMultiplier, w.DovStandMoveSpreadMultiplier, w.ZombieStandMoveSpreadMultiplier, mode),
        "SneakMoveSpreadMultiplier" => AltF(w.SneakMoveSpreadMultiplier, w.DovSneakMoveSpreadMultiplier, w.ZombieSneakMoveSpreadMultiplier, mode),
        "CrouchMoveSpreadMultiplier" => AltF(w.CrouchMoveSpreadMultiplier, w.DovCrouchMoveSpreadMultiplier, w.ZombieCrouchMoveSpreadMultiplier, mode),
        "JumpSpreadMultiplier" => AltF(w.JumpSpreadMultiplier, w.DovJumpSpreadMultiplier, w.ZombieJumpSpreadMultiplier, mode),
        "DamageHeadMultiplier" => AltF(w.DamageHeadMultiplier, w.DovDamageHeadMultiplier, w.ZombieDamageHeadMultiplier, mode),
        "DamageChestMultiplier" => AltF(w.DamageChestMultiplier, w.DovDamageChestMultiplier, w.ZombieDamageChestMultiplier, mode),
        "DamageStomachMultiplier" => AltF(w.DamageStomachMultiplier, w.DovDamageStomachMultiplier, w.ZombieDamageStomachMultiplier, mode),
        "DamageLegMultiplier" => AltF(w.DamageLegMultiplier, w.DovDamageLegMultiplier, w.ZombieDamageLegMultiplier, mode),
        "DamageArmMultiplier" => AltF(w.DamageArmMultiplier, w.DovDamageArmMultiplier, w.ZombieDamageArmMultiplier, mode),
        "DamageGeneric" => AltF(w.DamageGeneric, w.DovDamageGeneric, w.ZombieDamageGeneric, mode),
        "ShakeScale" => AltF(w.ShakeScale, w.DovShakeScale, w.ZombieShakeScale, mode),
        "ShakeFreq" => AltF(w.ShakeFreq, w.DovShakeFreq, w.ZombieShakeFreq, mode),
        "ShakeDuration" => AltF(w.ShakeDuration, w.DovShakeDuration, w.ZombieShakeDuration, mode),
        "CrosshairMinDistance" => AltI(w.CrosshairMinDistance, w.DovCrosshairMinDistance, w.ZombieCrosshairMinDistance, mode),
        "CrosshairDeltaDistance" => AltI(w.CrosshairDeltaDistance, w.DovCrosshairDeltaDistance, w.ZombieCrosshairDeltaDistance, mode),
        "weight" => AltF(w.Weight, w.DovWeight, w.ZombieWeight, mode),
        "ZMBuyPrice" => AltI(w.ZMBuyPrice, w.DovZMBuyPrice, w.ZombieZMBuyPrice, mode),
        "ZMWeight" => AltI(w.ZMWeight, w.DovZMWeight, w.ZombieZMWeight, mode),
        "recoilpushbackvalue" => AltF(w.RecoilPushbackValue, w.DovRecoilPushbackValue, w.ZombieRecoilPushbackValue, mode),
        "ironsightwalkbobbingstrength" => AltF(w.IronsightWalkBobbingStrength, w.DovIronsightWalkBobbingStrength, w.ZombieIronsightWalkBobbingStrength, mode),
        "MetalPenetrationDepth" => AltF(w.MetalPenetrationDepth, w.DovMetalPenetrationDepth, w.ZombieMetalPenetrationDepth, mode),
        "GlassPenetrationDepth" => AltF(w.GlassPenetrationDepth, w.DovGlassPenetrationDepth, w.ZombieGlassPenetrationDepth, mode),
        "ConcretePenetrationDepth" => AltF(w.ConcretePenetrationDepth, w.DovConcretePenetrationDepth, w.ZombieConcretePenetrationDepth, mode),
        "WoodPenetrationDepth" => AltF(w.WoodPenetrationDepth, w.DovWoodPenetrationDepth, w.ZombieWoodPenetrationDepth, mode),
        "OtherPenetrationDepth" => AltF(w.OtherPenetrationDepth, w.DovOtherPenetrationDepth, w.ZombieOtherPenetrationDepth, mode),
        "MetalDamageModifier" => AltF(w.MetalDamageModifier, w.DovMetalDamageModifier, w.ZombieMetalDamageModifier, mode),
        "GlassDamageModifier" => AltF(w.GlassDamageModifier, w.DovGlassDamageModifier, w.ZombieGlassDamageModifier, mode),
        "ConcreteDamageModifier" => AltF(w.ConcreteDamageModifier, w.DovConcreteDamageModifier, w.ZombieConcreteDamageModifier, mode),
        "WoodDamageModifier" => AltF(w.WoodDamageModifier, w.DovWoodDamageModifier, w.ZombieWoodDamageModifier, mode),
        "OtherDamageModifier" => AltF(w.OtherDamageModifier, w.DovOtherDamageModifier, w.ZombieOtherDamageModifier, mode),
        "NearwallDistance" => AltI(w.NearwallDistance, w.DovNearwallDistance, w.ZombieNearwallDistance, mode),
        "ViewSlideRecoil.Up" => AltF(w.ViewSlideRecoilUp, w.DovViewSlideRecoilUp, w.ZombieViewSlideRecoilUp, mode),
        "ViewSlideRecoil.Right" => AltF(w.ViewSlideRecoilRight, w.DovViewSlideRecoilRight, w.ZombieViewSlideRecoilRight, mode),
        "ViewSlideRecoilIronsight.Up" => AltF(w.ViewSlideRecoilIronsightUp, w.DovViewSlideRecoilIronsightUp, w.ZombieViewSlideRecoilIronsightUp, mode),
        "ViewSlideRecoilIronsight.Right" => AltF(w.ViewSlideRecoilIronsightRight, w.DovViewSlideRecoilIronsightRight, w.ZombieViewSlideRecoilIronsightRight, mode),
        "primary_ammo" => mode != null ? null : w.PrimaryAmmo,
        "clip_size" => AltS(w.ClipSize, w.DovClipSize, w.ZombieClipSize, mode),
        "SecondaryFireRate" => AltI(w.SecondaryFireRate, w.DovSecondaryFireRate, w.ZombieSecondaryFireRate, mode),
        "IronSight" => AltI(w.IronSight, w.DovIronSight, w.ZombieIronSight, mode),
        _ => null
    };

    //string版本 从顶层/Dov/Zombie中按mode选值
    private static string? AltS(string? top, string? dov, string? zombie, AltStatMode? mode) => mode switch
    {
        null => top,
        AltStatMode.Dov => string.IsNullOrEmpty(dov) || string.Equals(dov, top, StringComparison.OrdinalIgnoreCase) ? null : dov,
        AltStatMode.Zombie => string.IsNullOrEmpty(zombie) || string.Equals(zombie, top, StringComparison.OrdinalIgnoreCase) ? null : zombie,
        _ => null
    };

    //double版本
    private static string? AltF(double? top, double? dov, double? zombie, AltStatMode? mode)
    {
        double? v = mode switch
        {
            null => top,
            AltStatMode.Dov => dov,
            AltStatMode.Zombie => zombie,
            _ => null
        };
        double topVal = top ?? 0.0;
        if (mode != null && v.HasValue && Math.Abs(v.Value - topVal) < 0.001) return null;
        return F(v);
    }

    //int版本
    private static string? AltI(int? top, int? dov, int? zombie, AltStatMode? mode)
    {
        int? v = mode switch
        {
            null => top,
            AltStatMode.Dov => dov,
            AltStatMode.Zombie => zombie,
            _ => null
        };
        int topVal = top ?? 0;
        if (mode != null && v.HasValue && v.Value == topVal) return null;
        return v?.ToString();
    }

    private static string? F(double? v) => v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : null;

    private static void TrySetInt(string block, string key, out int? val)
    {
        val = null;
        if (ExtractValue(block, key) is string v && int.TryParse(v, out int r))
            val = r;
    }

    private static void TrySetDouble(string block, string key, out double? val)
    {
        val = null;
        if (ExtractValue(block, key) is string v && TryParseDouble(v, out double r))
            val = r;
    }

    //替换脚本中的键值对 仅替换第一个未被注释的匹配 防止误伤嵌套块内的同名键
    private static string ReplaceKeyValue(string c, string k, string v)
    {
        try
        {
        //匹配"key" "value"格式的行 要求行首不是//(即非注释行) 捕获key及其前导空白 旧值和行尾注释
        string p = $@"(^[ \t]*""{Regex.Escape(k)}""\s+)""[^""]*""(\s*(?://.*)?)";
        var m = Regex.Match(c, p, RegexOptions.Multiline);
        if (m.Success)
        {
            //只替换第一个匹配 后面的同名键(如在嵌套块内)不受影响
            return c.Remove(m.Index, m.Length)
                    .Insert(m.Index, $@"{m.Groups[1].Value}""{v}""{m.Groups[2].Value}");
        }
        return c;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ReplaceKeyValue: key={k}");
            return c;
        }
    }

    private static string ReplaceRecoilBlock(string c, string block, string? up, string? right)
    {
        try
        {
        //匹配block 限制在WeaponData顶层 使用RegexOptions来匹配大括号
        //通过 ^(?!\s*//) 确保块名不在注释行内 加.*平衡大括号嵌套
        string p = $@"(^{Regex.Escape(block)}\s*\{{(?:[^{{}}]|(?<open>\{{)|(?<-open>\}}))*(?(open)(?!))\}})";
        var m = Regex.Match(c, p, RegexOptions.Multiline | RegexOptions.Singleline);
        if (!m.Success) return c;
        string b = m.Value;

        if (up != null)
        {
            string upPat = $@"(""Up""\s+)""[^""]*""";
            var upMatch = Regex.Match(b, upPat, RegexOptions.Singleline);
            if (upMatch.Success)
                b = b.Remove(upMatch.Index, upMatch.Length)
                     .Insert(upMatch.Index, $@"$1""{up}""".Replace("$1", upMatch.Groups[1].Value));
        }
        if (right != null)
        {
            string rightPat = $@"(""Right""\s+)""[^""]*""";
            var rightMatch = Regex.Match(b, rightPat, RegexOptions.Singleline);
            if (rightMatch.Success)
                b = b.Remove(rightMatch.Index, rightMatch.Length)
                     .Insert(rightMatch.Index, $@"$1""{right}""".Replace("$1", rightMatch.Groups[1].Value));
        }

        return c.Remove(m.Index, m.Length).Insert(m.Index, b);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ReplaceRecoilBlock: block={block}");
            return c;
        }
    }
    #endregion
}