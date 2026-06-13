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
        ["primary_ammo"] = "primary_ammo",
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
        catch { }
        return map;
    }

    #endregion
    #region 导出导入

    public static string ExportCsvToScripts(string csvFilePath, string scriptsDir)
    {
        var log = new List<string>();
        var weapons = CsvService.LoadWeapons(csvFilePath);
        int total = weapons.Count;
        int success = 0;
        int skipped = 0;

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
                continue;
            }

            string scriptPath = Path.Combine(scriptsDir, scriptName);

            if (!File.Exists(scriptPath))
            {
                skipped++;
                continue;
            }

            string content = File.ReadAllText(scriptPath);
            int updated = 0;

            foreach (var map in CsvToScriptMap)
            {
                string? csvValue = GetFieldValue(weapon, map.Key, false);
                if (csvValue == null) continue;
                string newContent = ReplaceKeyValue(content, map.Value, csvValue);
                if (newContent != content) { content = newContent; updated++; }
            }

            string? ru = GetFieldValue(weapon, "ViewSlideRecoil.Up", false);
            string? rr = GetFieldValue(weapon, "ViewSlideRecoil.Right", false);
            if (ru != null || rr != null)
            {
                content = ReplaceRecoilBlock(content, "ViewSlideRecoil", ru, rr);
                updated++;
            }

            string? au = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Up", false);
            string? ar = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Right", false);
            if (au != null || ar != null)
            {
                content = ReplaceRecoilBlock(content, "ViewSlideRecoilIronsight", au, ar);
                updated++;
            }

            File.WriteAllText(scriptPath, content, new UTF8Encoding(false));
            success++;
            log.Add($"[{i + 1}/{total}] {scriptName} ({updated} 字段)");
        }

        log.Add(new string('-', 50));
        log.Add($"完成: 成功 {success}, 跳过 {skipped}, 总计 {total}");
        return string.Join("\n", log);
    }

    public static void ExportDovToScripts(string csvFilePath, string scriptsDir)
    {
        var weapons = CsvService.LoadWeapons(csvFilePath);
        foreach (var weapon in weapons)
        {
            if (string.IsNullOrEmpty(weapon.ScriptName)) continue;
            string scriptPath = Path.Combine(scriptsDir, weapon.ScriptName);
            if (!File.Exists(scriptPath)) continue;
            string content = File.ReadAllText(scriptPath);
            if (!content.Contains("dov_stats")) continue;
            foreach (var map in CsvToScriptMap)
            {
                string? csvValue = GetFieldValue(weapon, map.Key, true);
                if (csvValue == null) continue;
                content = WriteDovBlockValue(content, map.Value, csvValue);
            }
            string? ru = GetFieldValue(weapon, "ViewSlideRecoil.Up", true);
            string? rr = GetFieldValue(weapon, "ViewSlideRecoil.Right", true);
            if (ru != null || rr != null)
                content = WriteDovRecoilBlock(content, "ViewSlideRecoil", ru, rr);
            string? au = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Up", true);
            string? ar = GetFieldValue(weapon, "ViewSlideRecoilIronsight.Right", true);
            if (au != null || ar != null)
                content = WriteDovRecoilBlock(content, "ViewSlideRecoilIronsight", au, ar);
            File.WriteAllText(scriptPath, content, new UTF8Encoding(false));
        }
    }

    //正则地狱
    public static string ImportScriptsToCsv(string scriptsDir, string outputCsvPath)
    {
        var log = new List<string>();

        if (!Directory.Exists(scriptsDir))
            return $"错误: 目录不存在 - {scriptsDir}";

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
                string content = File.ReadAllText(path, Encoding.UTF8);

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

                ImportDovBlock(weapon, content);

                list.Add(weapon);
                success++;
                log.Add($"[{i + 1}/{total}] {name} ({read} 字段)");
            }
            catch (Exception ex)
            {
                failed++;
                log.Add($"[{i + 1}/{total}] 失败: {name} - {ex.GetType().Name}: {ex.Message}");
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
            catch { }
        }

        CsvService.SaveWeapons(outputCsvPath, list);
        log.Add($"保存完成: 共 {list.Count} 把武器写入 CSV");
        return string.Join("\n", log);
    }

    public static string? ReadDovBlockValue(string content, string key)
    {
        var blockMatch = Regex.Match(content, @"dov_stats\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!blockMatch.Success) return null;
        return ExtractValue(blockMatch.Groups[1].Value, key);
    }

    public static string WriteDovBlockValue(string content, string key, string value)
    {
        var blockMatch = Regex.Match(content, @"dov_stats\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!blockMatch.Success) return content;
        string block = blockMatch.Groups[1].Value;
        string newBlock = ReplaceKeyValue(block, key, value);
        if (newBlock == block) return content;
        return content.Replace(block, newBlock);
    }

    public static string WriteDovRecoilBlock(string content, string recoilBlock, string? up, string? right)
    {
        var dovMatch = Regex.Match(content, @"dov_stats\s*\{([^}]*(?:\{[^}]*\}[^}]*)*)\}", RegexOptions.Singleline);
        if (!dovMatch.Success) return content;
        string dovBlock = dovMatch.Groups[1].Value;
        string newDovBlock = ReplaceRecoilBlock(dovBlock, recoilBlock, up, right);
        if (newDovBlock == dovBlock) return content;
        return content.Replace(dovBlock, newDovBlock);
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

    //先尝试带引号的值 再尝试不带引号的裸值
    private static string? ExtractValue(string content, string key)
    {
        var m = Regex.Match(content, $@"""{Regex.Escape(key)}""\s+""([^""]*)""");//key后面至少一个空白 捕获双引号内的值
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(content, $@"""{Regex.Escape(key)}""\s+(\S+)");//回退匹配"abc" 123这种不带引号的裸值
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

    private static double? ParseRecoilBlockInDov(string dovBlock, string blockName, string key)
    {
        int idx = dovBlock.IndexOf(blockName, StringComparison.Ordinal);
        if (idx < 0) return null;
        int braceStart = dovBlock.IndexOf('{', idx);
        if (braceStart < 0) return null;
        int depth = 1;
        int i = braceStart + 1;
        while (i < dovBlock.Length && depth > 0)
        {
            if (dovBlock[i] == '{') depth++;
            else if (dovBlock[i] == '}') depth--;
            i++;
        }
        string blockContent = dovBlock.Substring(braceStart + 1, i - braceStart - 2);
        var m = Regex.Match(blockContent, $@"""{Regex.Escape(key)}""\s+""([^""]*)""");
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
            return r;
        return null;
    }

    private static void ImportDovBlock(WeaponData w, string content)
    {
        int dovIdx = content.IndexOf("dov_stats", StringComparison.Ordinal);
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

        TrySetInt(block, "ExtraBulletChamber", out int? i1); w.DovExtraBulletChamber = i1;
        TrySetInt(block, "FireRate", out int? i2); w.DovFireRate = i2;
        TrySetDouble(block, "BulletSpreadDegrees", out double? d1); w.DovBulletSpread = d1;
        TrySetDouble(block, "BulletSpreadDegreesIronsighted", out double? d2); w.DovBulletSpreadDegreesIronsighted = d2;
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
        TrySetInt(block, "CrosshairMinDistance", out int? i3); w.DovCrosshairMinDistance = i3;
        TrySetInt(block, "CrosshairDeltaDistance", out int? i4); w.DovCrosshairDeltaDistance = i4;
        TrySetDouble(block, "weight", out double? d17); w.DovWeight = d17;
        TrySetInt(block, "ZMBuyPrice", out int? i5); w.DovZMBuyPrice = i5;
        TrySetInt(block, "ZMWeight", out int? i6); w.DovZMWeight = i6;
        TrySetDouble(block, "BulletSpreadDegreesBipod", out double? d18); w.DovBulletSpreadDegreesBipod = d18;
        TrySetDouble(block, "BulletSpreadDegreesBipodIronsighted", out double? d19); w.DovBulletSpreadDegreesBipodIronsighted = d19;
        w.DovFireModes = ExtractValue(block, "SupportedFireModes") ?? "";        
        w.DovClipSize = ExtractValue(block, "clip_size") ?? "";
        w.DovViewSlideRecoilUp = ParseRecoilBlockInDov(block, "ViewSlideRecoil", "Up");
        w.DovViewSlideRecoilRight = ParseRecoilBlockInDov(block, "ViewSlideRecoil", "Right");
        w.DovViewSlideRecoilIronsightUp = ParseRecoilBlockInDov(block, "ViewSlideRecoilIronsight", "Up");
        w.DovViewSlideRecoilIronsightRight = ParseRecoilBlockInDov(block, "ViewSlideRecoilIronsight", "Right");
        TrySetInt(block, "SecondaryFireRate", out int? i7); w.DovSecondaryFireRate = i7;
        TrySetInt(block, "IronSight", out int? i8); w.DovIronSight = i8;
    }

    #endregion
    #region 字段读写替换

    private static string? GetFieldValue(WeaponData w, string h, bool isDov) => h switch
    {
        "SupportedFireModes" => isDov ? (string.IsNullOrEmpty(w.DovFireModes) ? null : w.DovFireModes) : w.FireModes,
        "default_clip" => w.DefaultClip?.ToString(),
        "ExtraBulletChamber" => isDov ? w.DovExtraBulletChamber?.ToString() : w.ExtraBulletChamber?.ToString(),
        "bullets_per_shot" => w.BulletsPerShot?.ToString(),
        "FireRate" => isDov ? w.DovFireRate?.ToString() : w.FireRate?.ToString(),
        "BulletSpreadDegrees" => F(isDov ? w.DovBulletSpread : w.BulletSpread),
        "BulletSpreadDegreesIronsighted" => F(isDov ? w.DovBulletSpreadDegreesIronsighted : w.BulletSpreadDegreesIronsighted),
        "BulletSpreadDegreesBipod" => isDov ? F(w.DovBulletSpreadDegreesBipod) : F(w.BulletSpreadDegreesBipod),
        "BulletSpreadDegreesBipodIronsighted" => isDov ? F(w.DovBulletSpreadDegreesBipodIronsighted) : F(w.BulletSpreadDegreesBipodIronsighted),
        "rangemodifier" => F(isDov ? w.DovRangeModifier : w.RangeModifier),
        "IronsightSpeedScale" => F(isDov ? w.DovIronsightSpeedScale : w.IronsightSpeedScale),
        "CrouchSpreadMultiplier" => F(isDov ? w.DovCrouchSpreadMultiplier : w.CrouchSpreadMultiplier),
        "ProneSpreadMultiplier" => F(isDov ? w.DovProneSpreadMultiplier : w.ProneSpreadMultiplier),
        "StandMoveSpreadMultiplier" => F(isDov ? w.DovStandMoveSpreadMultiplier : w.StandMoveSpreadMultiplier),
        "SneakMoveSpreadMultiplier" => F(isDov ? w.DovSneakMoveSpreadMultiplier : w.SneakMoveSpreadMultiplier),
        "CrouchMoveSpreadMultiplier" => F(isDov ? w.DovCrouchMoveSpreadMultiplier : w.CrouchMoveSpreadMultiplier),
        "JumpSpreadMultiplier" => F(isDov ? w.DovJumpSpreadMultiplier : w.JumpSpreadMultiplier),
        "DamageHeadMultiplier" => F(isDov ? w.DovDamageHeadMultiplier : w.DamageHeadMultiplier),
        "DamageChestMultiplier" => F(isDov ? w.DovDamageChestMultiplier : w.DamageChestMultiplier),
        "DamageStomachMultiplier" => F(isDov ? w.DovDamageStomachMultiplier : w.DamageStomachMultiplier),
        "DamageLegMultiplier" => F(isDov ? w.DovDamageLegMultiplier : w.DamageLegMultiplier),
        "DamageArmMultiplier" => F(isDov ? w.DovDamageArmMultiplier : w.DamageArmMultiplier),
        "DamageGeneric" => F(isDov ? w.DovDamageGeneric : w.DamageGeneric),
        "ShakeScale" => F(w.ShakeScale),
        "ShakeFreq" => F(w.ShakeFreq),
        "ShakeDuration" => F(w.ShakeDuration),
        "CrosshairMinDistance" => isDov ? w.DovCrosshairMinDistance?.ToString() : w.CrosshairMinDistance?.ToString(),
        "CrosshairDeltaDistance" => isDov ? w.DovCrosshairDeltaDistance?.ToString() : w.CrosshairDeltaDistance?.ToString(),
        "weight" => F(isDov ? w.DovWeight : w.Weight),
        "ZMBuyPrice" => isDov ? w.DovZMBuyPrice?.ToString() : w.ZMBuyPrice?.ToString(),
        "ZMWeight" => isDov ? w.DovZMWeight?.ToString() : w.ZMWeight?.ToString(),
        "recoilpushbackvalue" => F(w.RecoilPushbackValue),
        "ironsightwalkbobbingstrength" => F(w.IronsightWalkBobbingStrength),
        "MetalPenetrationDepth" => F(w.MetalPenetrationDepth),
        "GlassPenetrationDepth" => F(w.GlassPenetrationDepth),
        "ConcretePenetrationDepth" => F(w.ConcretePenetrationDepth),
        "WoodPenetrationDepth" => F(w.WoodPenetrationDepth),
        "OtherPenetrationDepth" => F(w.OtherPenetrationDepth),
        "MetalDamageModifier" => F(w.MetalDamageModifier),
        "GlassDamageModifier" => F(w.GlassDamageModifier),
        "ConcreteDamageModifier" => F(w.ConcreteDamageModifier),
        "WoodDamageModifier" => F(w.WoodDamageModifier),
        "OtherDamageModifier" => F(w.OtherDamageModifier),
        "NearwallDistance" => w.NearwallDistance?.ToString(),
        "ViewSlideRecoil.Up" => F(isDov ? w.DovViewSlideRecoilUp : w.ViewSlideRecoilUp),
        "ViewSlideRecoil.Right" => F(isDov ? w.DovViewSlideRecoilRight : w.ViewSlideRecoilRight),
        "ViewSlideRecoilIronsight.Up" => F(isDov ? w.DovViewSlideRecoilIronsightUp : w.ViewSlideRecoilIronsightUp),
        "ViewSlideRecoilIronsight.Right" => F(isDov ? w.DovViewSlideRecoilIronsightRight : w.ViewSlideRecoilIronsightRight),
        "primary_ammo" => w.PrimaryAmmo,
        "clip_size" => isDov ? w.DovClipSize : w.ClipSize,
        "SecondaryFireRate" => isDov ? w.DovSecondaryFireRate?.ToString() : w.SecondaryFireRate?.ToString(),
        "IronSight" => isDov ? w.DovIronSight?.ToString() : w.IronSight?.ToString(),
        _ => null
    };

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

    //替换脚本中的键值对 第一优先级匹配带注释行
    private static string ReplaceKeyValue(string c, string k, string v)
    {
        string p = $@"(""{Regex.Escape(k)}""\s+)"".*?""(\s*(?://.*)?)";//捕获key和空白 替换双引号内的旧值并保留行尾注释
        if (Regex.IsMatch(c, p))
            return Regex.Replace(c, p, $@"$1""{v}""$2");

        p = $@"(""{Regex.Escape(k)}""\s+)""[^""]*""";//回退匹配不带注释的行
        if (Regex.IsMatch(c, p))
            return Regex.Replace(c, p, $@"$1""{v}""");

        return c;
    }

    private static string ReplaceRecoilBlock(string c, string block, string? up, string? right)
    {
        string p = $@"({Regex.Escape(block)}\s*\{{[^}}]*)}}";//捕获block名到倒数第二个} 不含最后的}
        var m = Regex.Match(c, p, RegexOptions.Singleline);
        if (!m.Success) return c;
        string b = m.Groups[1].Value;

        if (up != null)
            b = Regex.Replace(b, $@"(""Up""\s+)""[^""]*""", $@"$1""{up}""");//匹配Up后面引号内的旧值替换为新值
        if (right != null)
            b = Regex.Replace(b, $@"(""Right""\s+)""[^""]*""", $@"$1""{right}""");//Right同上

        return c.Replace(m.Value, b + "}");
    }
    #endregion
}