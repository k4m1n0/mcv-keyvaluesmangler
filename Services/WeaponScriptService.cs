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
    };

    private static readonly Dictionary<string, Action<WeaponData, string>> FieldSetters = new(StringComparer.OrdinalIgnoreCase)
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
                string? csvValue = GetCsvFieldValue(weapon, map.Key);
                if (csvValue == null) continue;
                string newContent = ReplaceKeyValue(content, map.Value, csvValue);
                if (newContent != content) { content = newContent; updated++; }
            }

            string? ru = GetCsvFieldValue(weapon, "ViewSlideRecoil.Up");
            string? rr = GetCsvFieldValue(weapon, "ViewSlideRecoil.Right");
            if (ru != null || rr != null)
            {
                content = ReplaceRecoilBlock(content, "ViewSlideRecoil", ru, rr);
                updated++;
            }

            string? au = GetCsvFieldValue(weapon, "ViewSlideRecoilIronsight.Up");
            string? ar = GetCsvFieldValue(weapon, "ViewSlideRecoilIronsight.Right");
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
    private static double? ParseRecoilBlock(string content, string block, string key)
    {
        var m = Regex.Match(content, $@"{Regex.Escape(block)}\s*\{{[^}}]*""{Regex.Escape(key)}""\s+""([^""]*)""", RegexOptions.Singleline);//匹配block名后紧跟的大括号内容 取指定key的双引号
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
            return r;
        return null;
    }

    private static string? GetCsvFieldValue(WeaponData w, string h) => h switch
    {
        "SupportedFireModes" => w.FireModes,
        "default_clip" => w.DefaultClip?.ToString(),
        "ExtraBulletChamber" => w.ExtraBulletChamber?.ToString(),
        "bullets_per_shot" => w.BulletsPerShot?.ToString(),
        "FireRate" => w.FireRate?.ToString(),
        "BulletSpreadDegrees" => F(w.BulletSpread),
        "BulletSpreadDegreesIronsighted" => F(w.BulletSpreadDegreesIronsighted),
        "BulletSpreadDegreesBipod" => F(w.BulletSpreadDegreesBipod),
        "BulletSpreadDegreesBipodIronsighted" => F(w.BulletSpreadDegreesBipodIronsighted),
        "rangemodifier" => F(w.RangeModifier),
        "IronsightSpeedScale" => F(w.IronsightSpeedScale),
        "CrouchSpreadMultiplier" => F(w.CrouchSpreadMultiplier),
        "ProneSpreadMultiplier" => F(w.ProneSpreadMultiplier),
        "StandMoveSpreadMultiplier" => F(w.StandMoveSpreadMultiplier),
        "SneakMoveSpreadMultiplier" => F(w.SneakMoveSpreadMultiplier),
        "CrouchMoveSpreadMultiplier" => F(w.CrouchMoveSpreadMultiplier),
        "JumpSpreadMultiplier" => F(w.JumpSpreadMultiplier),
        "DamageHeadMultiplier" => F(w.DamageHeadMultiplier),
        "DamageChestMultiplier" => F(w.DamageChestMultiplier),
        "DamageStomachMultiplier" => F(w.DamageStomachMultiplier),
        "DamageLegMultiplier" => F(w.DamageLegMultiplier),
        "DamageArmMultiplier" => F(w.DamageArmMultiplier),
        "DamageGeneric" => F(w.DamageGeneric),
        "ShakeScale" => F(w.ShakeScale),
        "ShakeFreq" => F(w.ShakeFreq),
        "ShakeDuration" => F(w.ShakeDuration),
        "CrosshairMinDistance" => w.CrosshairMinDistance?.ToString(),
        "CrosshairDeltaDistance" => w.CrosshairDeltaDistance?.ToString(),
        "weight" => F(w.Weight),
        "ZMBuyPrice" => w.ZMBuyPrice?.ToString(),
        "ZMWeight" => w.ZMWeight?.ToString(),
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
        "ViewSlideRecoil.Up" => F(w.ViewSlideRecoilUp),
        "ViewSlideRecoil.Right" => F(w.ViewSlideRecoilRight),
        "ViewSlideRecoilIronsight.Up" => F(w.ViewSlideRecoilIronsightUp),
        "ViewSlideRecoilIronsight.Right" => F(w.ViewSlideRecoilIronsightRight),
        "primary_ammo" => w.PrimaryAmmo,
        "clip_size" => w.ClipSize,
        _ => null
    };

    private static string? F(double? v) => v.HasValue ? v.Value.ToString("0.#####", CultureInfo.InvariantCulture) : null;

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
}