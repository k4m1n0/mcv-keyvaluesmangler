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

    internal static string ReadScriptFile(string sPath)
    {
        try
        {
            byte[] rgBytes = File.ReadAllBytes(sPath);
            if (rgBytes.Length >= 2 && rgBytes[0] == 0xFF && rgBytes[1] == 0xFE)
                return Encoding.Unicode.GetString(rgBytes);
            if (rgBytes.Length >= 2 && rgBytes[0] == 0xFE && rgBytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(rgBytes);
            if (rgBytes.Length >= 3 && rgBytes[0] == 0xEF && rgBytes[1] == 0xBB && rgBytes[2] == 0xBF)
                return Encoding.UTF8.GetString(rgBytes);
            return Encoding.UTF8.GetString(rgBytes);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ReadScriptFile: {sPath}");
            return string.Empty;
        }
    }

    public enum AltStatMode { Dov, Zombie }

    private static readonly Dictionary<AltStatMode, string> mpAltStatBlockNames = new()
    {
        [AltStatMode.Dov] = "dov_stats",
        [AltStatMode.Zombie] = "zombie_stats",
    };

    private static readonly HashSet<string> hsNonFirearmTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "GrenadeLauncher", "RocketLauncher", "Melee", "Equipment",
        "SmokeGrenade", "Grenade", "RifleGrenade", "C4",
        "Crossbow", "Flaregun", "Flamethrower", "Incendiary", "Fists", "Mine"
    };

    internal static readonly Dictionary<string, string> mpCsvToScript = new()
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

    internal static readonly Dictionary<string, Action<WeaponData, string>> mpFieldSetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SupportedFireModes"] = (w, sV) => w.FireModes = sV,
        ["default_clip"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.DefaultClip = iR; },
        ["ExtraBulletChamber"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.ExtraBulletChamber = iR; },
        ["bullets_per_shot"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.BulletsPerShot = iR; },
        ["FireRate"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.FireRate = iR; },
        ["BulletSpreadDegrees"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.BulletSpread = dR; },
        ["BulletSpreadDegreesIronsighted"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.BulletSpreadDegreesIronsighted = dR; },
        ["BulletSpreadDegreesBipod"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.BulletSpreadDegreesBipod = dR; },
        ["BulletSpreadDegreesBipodIronsighted"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.BulletSpreadDegreesBipodIronsighted = dR; },
        ["rangemodifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.RangeModifier = dR; },
        ["IronsightSpeedScale"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.IronsightSpeedScale = dR; },
        ["CrouchSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.CrouchSpreadMultiplier = dR; },
        ["ProneSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ProneSpreadMultiplier = dR; },
        ["StandMoveSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.StandMoveSpreadMultiplier = dR; },
        ["SneakMoveSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.SneakMoveSpreadMultiplier = dR; },
        ["CrouchMoveSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.CrouchMoveSpreadMultiplier = dR; },
        ["JumpSpreadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.JumpSpreadMultiplier = dR; },
        ["DamageHeadMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageHeadMultiplier = dR; },
        ["DamageChestMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageChestMultiplier = dR; },
        ["DamageStomachMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageStomachMultiplier = dR; },
        ["DamageLegMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageLegMultiplier = dR; },
        ["DamageArmMultiplier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageArmMultiplier = dR; },
        ["DamageGeneric"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.DamageGeneric = dR; },
        ["ShakeScale"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ShakeScale = dR; },
        ["ShakeFreq"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ShakeFreq = dR; },
        ["ShakeDuration"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ShakeDuration = dR; },
        ["CrosshairMinDistance"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.CrosshairMinDistance = iR; },
        ["CrosshairDeltaDistance"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.CrosshairDeltaDistance = iR; },
        ["weight"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.Weight = dR; },
        ["ZMBuyPrice"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.ZMBuyPrice = iR; },
        ["ZMWeight"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.ZMWeight = iR; },
        ["recoilpushbackvalue"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.RecoilPushbackValue = dR; },
        ["ironsightwalkbobbingstrength"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.IronsightWalkBobbingStrength = dR; },
        ["MetalPenetrationDepth"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.MetalPenetrationDepth = dR; },
        ["GlassPenetrationDepth"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.GlassPenetrationDepth = dR; },
        ["ConcretePenetrationDepth"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ConcretePenetrationDepth = dR; },
        ["WoodPenetrationDepth"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.WoodPenetrationDepth = dR; },
        ["OtherPenetrationDepth"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.OtherPenetrationDepth = dR; },
        ["MetalDamageModifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.MetalDamageModifier = dR; },
        ["GlassDamageModifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.GlassDamageModifier = dR; },
        ["ConcreteDamageModifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.ConcreteDamageModifier = dR; },
        ["WoodDamageModifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.WoodDamageModifier = dR; },
        ["OtherDamageModifier"] = (w, sV) => { if (TryParseDouble(sV, out double dR)) w.OtherDamageModifier = dR; },
        ["NearwallDistance"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.NearwallDistance = iR; },
        ["primary_ammo"] = (w, sV) => w.PrimaryAmmo = sV,
        ["clip_size"] = (w, sV) => w.ClipSize = sV,
        ["SecondaryFireRate"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.SecondaryFireRate = iR; },
        ["IronSight"] = (w, sV) => { if (int.TryParse(sV, out int iR)) w.IronSight = iR; },
    };

    private static Dictionary<string, string> LoadPrintNameMap(string sCsvPath)
    {
        var mpMap = new Dictionary<string, string>();
        if (!File.Exists(sCsvPath)) return mpMap;

        try
        {
            var rgWeapons = CsvService.LoadWeapons(sCsvPath);
            foreach (var w in rgWeapons)
            {
                if (!string.IsNullOrEmpty(w.ScriptName) && !string.IsNullOrEmpty(w.PrintName))
                    mpMap[w.ScriptName] = w.PrintName;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponScriptService.LoadPrintNameMap");
        }
        return mpMap;
    }

    #endregion
    #region 公共工具

    //解析WeaponData块的顶层键值对 只收集不嵌套在子块内的键
    public static Dictionary<string, string> ParseWeaponDataPairs(string sContent)
    {
        var mpValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int iWd = sContent.IndexOf("WeaponData", StringComparison.Ordinal);
            if (iWd < 0) return mpValues;
            int iBs = sContent.IndexOf('{', iWd);
            if (iBs < 0) return mpValues;
            int iBe = FindMatchingBrace(sContent, iBs);
            if (iBe < 0 || iBs + 1 >= iBe) return mpValues;
            string sBlock = sContent.Substring(iBs + 1, iBe - iBs - 1);

            foreach (Match m in Regex.Matches(sBlock, @"""([^""]+)""\s+""([^""]*)""", RegexOptions.Multiline))
            {
                string sBefore = sBlock.Substring(0, m.Index);
                int iOb = 0, iCb = 0;
                for (int j = 0; j < sBefore.Length; j++)
                { if (sBefore[j] == '{') iOb++; else if (sBefore[j] == '}') iCb++; }
                if (iOb == iCb) mpValues[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponScriptService.ParseWeaponDataPairs");
        }
        return mpValues;
    }

    //大括号匹配 忽略字符串内的{}
    public static int FindMatchingBrace(string sText, int iStart)
    {
        int iDepth = 0; bool bInStr = false;
        for (int i = iStart; i < sText.Length; i++)
        {
            if (sText[i] == '"' && (i == 0 || sText[i - 1] != '\\')) bInStr = !bInStr;
            if (!bInStr) { if (sText[i] == '{') iDepth++; else if (sText[i] == '}') { iDepth--; if (iDepth == 0) return i; } }
        }
        return -1;
    }

    public static string FormatDouble(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

    public static string FormatClipSize(string sRaw, string sExtraChamber)
    {
        if (string.IsNullOrEmpty(sRaw) || sRaw == "-1" || sRaw == "-1/-1" || sRaw == "0/0") return "N/A";
        if (!sRaw.Contains('/')) return sRaw;
        var rgParts = sRaw.Split('/');
        string sMarker = sExtraChamber == "1" ? "[[+1]]" : "";
        return $"{rgParts[0].Trim()}{sMarker} / {rgParts[1].Trim()}";
    }

    public static double GetDoubleVal(Dictionary<string, string> mpVals, string sKey) =>
        mpVals.TryGetValue(sKey, out var sS) && double.TryParse(sS, NumberStyles.Float, CultureInfo.InvariantCulture, out double dD) ? dD : 0;

    #endregion
    #region 导出导入

    public static string ExportCsvToScripts(string sCsvFilePath, string sScriptsDir)
    {
        var rgLog = new List<string>();
        LogService.Info($"ExportCsvToScripts: {sCsvFilePath} -> {sScriptsDir}");
        var rgWeapons = CsvService.LoadWeapons(sCsvFilePath);
        int iTotal = rgWeapons.Count;
        int iSuccess = 0;
        int iSkipped = 0;
        int iSkippedNoScript = 0;
        int iSkippedEmptyName = 0;

        rgLog.Add($"CSV -> 脚本导出");
        rgLog.Add($"CSV: {sCsvFilePath}");
        rgLog.Add($"目标目录: {sScriptsDir}");
        rgLog.Add($"共 {iTotal} 把武器");
        rgLog.Add(new string('-', 50));

        for (int i = 0; i < rgWeapons.Count; i++)
        {
            var wWeapon = rgWeapons[i];
            string sScriptName = wWeapon.ScriptName;

            if (string.IsNullOrEmpty(sScriptName))
            {
                iSkipped++;
                iSkippedEmptyName++;
                continue;
            }

            string sScriptPath = Path.Combine(sScriptsDir, sScriptName);

            if (!File.Exists(sScriptPath))
            {
                iSkipped++;
                iSkippedNoScript++;
                LogService.Warn($"ExportCsvToScripts: script not found: {sScriptName}");
                continue;
            }

            try
            {
                string sContent = ReadScriptFile(sScriptPath);
                int iUpdated = 0;

                foreach (var kvpMap in mpCsvToScript)
                {
                    string? sCsvValue = GetFieldValue(wWeapon, kvpMap.Key, null);
                    if (sCsvValue == null) continue;
                    string sNewContent = ReplaceKeyValue(sContent, kvpMap.Value, sCsvValue);
                    if (sNewContent != sContent) { sContent = sNewContent; iUpdated++; }
                }

                string? sRu = GetFieldValue(wWeapon, "ViewSlideRecoil.Up", null);
                string? sRr = GetFieldValue(wWeapon, "ViewSlideRecoil.Right", null);
                if (sRu != null || sRr != null)
                {
                    sContent = ReplaceRecoilBlock(sContent, "ViewSlideRecoil", sRu, sRr);
                    iUpdated++;
                }

                string? sAu = GetFieldValue(wWeapon, "ViewSlideRecoilIronsight.Up", null);
                string? sAr = GetFieldValue(wWeapon, "ViewSlideRecoilIronsight.Right", null);
                if (sAu != null || sAr != null)
                {
                    sContent = ReplaceRecoilBlock(sContent, "ViewSlideRecoilIronsight", sAu, sAr);
                    iUpdated++;
                }

                sContent = sContent.TrimEnd('\r', '\n');
                File.WriteAllText(sScriptPath, sContent, new UTF8Encoding(false));
                iSuccess++;
                rgLog.Add($"[{i + 1}/{iTotal}] {sScriptName} ({iUpdated} 字段)");
            }
            catch (Exception ex)
            {
                iSkipped++;
                rgLog.Add($"[{i + 1}/{iTotal}] 失败: {sScriptName} - {ex.Message}");
                LogService.Error(ex, $"ExportCsvToScripts: {sScriptName}");
            }
        }

        rgLog.Add(new string('-', 50));
        rgLog.Add($"完成: 成功 {iSuccess}, 跳过 {iSkipped} (空名{iSkippedEmptyName} 无脚本{iSkippedNoScript}), 总计 {iTotal}");
        string sResult = string.Join("\n", rgLog);
        LogService.Info($"ExportCsvToScripts done: {iSuccess} ok, {iSkipped} skip, {iTotal} total");
        return sResult;
    }

    public static void ExportAltStatsToScripts(string sCsvFilePath, string sScriptsDir, AltStatMode mode)//屎
    {
        string sBlockName = mpAltStatBlockNames[mode];
        LogService.Info($"ExportAltStatsToScripts: {sCsvFilePath} -> {sScriptsDir}, mode={mode}");
        var rgWeapons = CsvService.LoadWeapons(sCsvFilePath);
        int iUpdated = 0, iSkipped = 0;

        foreach (var wWeapon in rgWeapons)
        {
            if (string.IsNullOrEmpty(wWeapon.ScriptName)) continue;
            string sScriptPath = Path.Combine(sScriptsDir, wWeapon.ScriptName);
            if (!File.Exists(sScriptPath)) continue;
            try
            {
                string sContent = ReadScriptFile(sScriptPath);
                sContent = sContent.Replace("\r\n", "\n").Replace('\r', '\n');
                string sOriginalContent = sContent;

                bool bHasAnyDiff = false;
                foreach (var kvpMap in mpCsvToScript)
                {
                    if (GetFieldValue(wWeapon, kvpMap.Key, mode) != null)
                    {
                        bHasAnyDiff = true;
                        if (wWeapon.ScriptName.Equals("weapon_ak47.txt", StringComparison.OrdinalIgnoreCase))
                            LogService.Debug($"hasAnyDiff: {mode} {kvpMap.Key}={GetFieldValue(wWeapon, kvpMap.Key, mode)}");
                        break;
                    }
                }
                if (!bHasAnyDiff)
                {
                    string? sRu = GetFieldValue(wWeapon, "ViewSlideRecoil.Up", mode);
                    string? sRi = GetFieldValue(wWeapon, "ViewSlideRecoilIronsight.Up", mode);
                    if (sRu != null || sRi != null)
                    {
                        bHasAnyDiff = true;
                        if (wWeapon.ScriptName.Equals("weapon_ak47.txt", StringComparison.OrdinalIgnoreCase))
                            LogService.Debug($"hasAnyDiff: {mode} recoil Up={sRu}, IronsightUp={sRi}");
                    }
                }

                if (!sContent.Contains(sBlockName) && !sContent.Contains($"//{sBlockName}"))
                {
                    if (!bHasAnyDiff) continue;
                    sContent = InsertAltStatBlock(sContent, sBlockName, mode);
                }

                sContent = ToggleAltStatBlockComment(sContent, mode, bHasAnyDiff, wWeapon.ScriptName);

                if (sContent.Contains(sBlockName))
                {
                    var mBlock = Regex.Match(sContent, $@"{Regex.Escape(sBlockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
                    if (mBlock.Success)
                    {
                        string sFullBlock = mBlock.Value.Replace("\r\n", "\n").Replace('\r', '\n');
                        string sOriginalBlock = sFullBlock;

                    foreach (var kvpMap in mpCsvToScript)
                        sFullBlock = ApplyKeyToBlock(sFullBlock, kvpMap.Value, GetFieldValue(wWeapon, kvpMap.Key, mode) ?? "", null);

                        if (sFullBlock != sOriginalBlock)
                            sContent = sContent.Replace(sOriginalBlock, sFullBlock);

                        foreach (var sRecoil in new[] { "ViewSlideRecoil", "ViewSlideRecoilIronsight" })
                        {
                            string? sUp = GetFieldValue(wWeapon, $"{sRecoil}.Up", mode);
                            string? sRight = GetFieldValue(wWeapon, $"{sRecoil}.Right", mode);
                            if (sUp != null || sRight != null)
                                sContent = WriteAltStatRecoilBlock(sContent, sRecoil, sUp, sRight, mode);
                        }
                    }
                }

                if (sContent != sOriginalContent)
                {
                    sContent = sContent.TrimEnd('\r', '\n');
                    File.WriteAllText(sScriptPath, sContent, new UTF8Encoding(false));
                    iUpdated++;
                }
            }
            catch (Exception ex)
            {
                iSkipped++;
                LogService.Error(ex, $"ExportAltStatsToScripts: {wWeapon.ScriptName}");
            }
        }
        LogService.Info($"ExportAltStatsToScripts done: {iUpdated} updated, {iSkipped} errors");
    }

    private static string InsertAltStatBlock(string sContent, string sBlockName, AltStatMode mode)
    {
        int iInsertIdx = sContent.LastIndexOf("\"ZMWeight\"", StringComparison.Ordinal);
        if (iInsertIdx < 0) iInsertIdx = sContent.LastIndexOf("// Day of Victory", StringComparison.Ordinal);
        if (iInsertIdx < 0) iInsertIdx = sContent.LastIndexOf("//---", StringComparison.Ordinal);
        if (iInsertIdx < 0) return sContent;

        int iLineEnd = sContent.IndexOf('\n', iInsertIdx);
        if (iLineEnd < 0) return sContent;

        int iLineStart = sContent.LastIndexOf('\n', iInsertIdx);
        string sIndent = "\t";
        if (iLineStart >= 0)
        {
            string sRefLine = sContent.Substring(iLineStart + 1, iInsertIdx - iLineStart - 1);
            sIndent = sRefLine.Length - sRefLine.TrimStart().Length > 0
                ? sRefLine.Substring(0, sRefLine.Length - sRefLine.TrimStart().Length)
                : "\t";
        }
        string sModeLabel = mode == AltStatMode.Dov ? "DoV" : "Zombie Mode";
        return sContent.Insert(iLineEnd + 1, $"\n{sIndent}// if anything is modified for {sModeLabel}:\n{sIndent}{sBlockName}\n{sIndent}{{\n{sIndent}}}\n{sIndent}//---\n");
    }

    //正则地狱
    public static string ImportScriptsToCsv(string sScriptsDir, string sOutputCsvPath)
    {
        var rgLog = new List<string>();
        LogService.Info($"ImportScriptsToCsv: {sScriptsDir} -> {sOutputCsvPath}");

        if (!Directory.Exists(sScriptsDir))
        {
            LogService.Error($"ImportScriptsToCsv: directory not found: {sScriptsDir}");
            return $"错误: 目录不存在 - {sScriptsDir}";
        }

        var mpOldPrintNames = LoadPrintNameMap(sOutputCsvPath);

        string[] rgFiles = Directory.GetFiles(sScriptsDir, "*.txt");
        var rgList = new List<WeaponData>();
        int iTotal = rgFiles.Length;
        int iSuccess = 0, iFailed = 0, iSkipped = 0;

        rgLog.Add($"脚本 -> CSV 导入");
        rgLog.Add($"目录: {sScriptsDir}");
        rgLog.Add($"共 {iTotal} 个文件");
        rgLog.Add(new string('-', 50));

        for (int i = 0; i < rgFiles.Length; i++)
        {
            string sPath = rgFiles[i];
            string sName = Path.GetFileName(sPath);

            try
            {
                string sContent = ReadScriptFile(sPath);

                if (!IsStandardFirearm(sName, sContent))
                {
                    iSkipped++;
                    rgLog.Add($"[{i + 1}/{iTotal}] 跳过(非枪械): {sName}");
                    continue;
                }

                var wWeapon = new WeaponData { ScriptName = sName };
                int iRead = 0;

                foreach (var kvpSetter in mpFieldSetters)
                {
                    string? sVal = ExtractValue(sContent, kvpSetter.Key);
                    if (sVal == null) continue;
                    kvpSetter.Value(wWeapon, sVal);
                    iRead++;
                }

                if (mpOldPrintNames.TryGetValue(sName, out var sPn))
                    wWeapon.PrintName = sPn;
                else
                {
                    var mPrintName = Regex.Match(sContent, @"""printname""\s+""([^""]*)""");
                    if (mPrintName.Success) wWeapon.PrintName = mPrintName.Groups[1].Value;
                }

                wWeapon.ViewSlideRecoilUp = ParseRecoilBlock(sContent, "ViewSlideRecoil", "Up");
                wWeapon.ViewSlideRecoilRight = ParseRecoilBlock(sContent, "ViewSlideRecoil", "Right");
                wWeapon.ViewSlideRecoilIronsightUp = ParseRecoilBlock(sContent, "ViewSlideRecoilIronsight", "Up");
                wWeapon.ViewSlideRecoilIronsightRight = ParseRecoilBlock(sContent, "ViewSlideRecoilIronsight", "Right");

                ImportAltStatBlock(wWeapon, sContent, AltStatMode.Dov);
                ImportAltStatBlock(wWeapon, sContent, AltStatMode.Zombie);

                rgList.Add(wWeapon);
                iSuccess++;
                rgLog.Add($"[{i + 1}/{iTotal}] {sName} ({iRead} 字段)");
            }
            catch (Exception ex)
            {
                iFailed++;
                rgLog.Add($"[{i + 1}/{iTotal}] 失败: {sName} - {ex.GetType().Name}: {ex.Message}");
                LogService.Error(ex, $"ImportScriptsToCsv: {sName}");
            }
        }

        rgLog.Add(new string('-', 50));
        rgLog.Add($"解析完成: 成功 {iSuccess}, 失败 {iFailed}, 跳过 {iSkipped}");

        if (iSuccess > 0 && File.Exists(sOutputCsvPath))
        {
            try
            {
                var rgOldWeapons = CsvService.LoadWeapons(sOutputCsvPath);
                var rgOldOrder = rgOldWeapons.Select(w => w.ScriptName).ToList();
                if (rgOldOrder.Count > 0)
                {
                    var mpDict = rgList.ToDictionary(w => w.ScriptName);
                    var rgOrdered = new List<WeaponData>();
                    foreach (var sSn in rgOldOrder)
                    {
                        if (mpDict.TryGetValue(sSn, out var wWeapon))
                        {
                            rgOrdered.Add(wWeapon);
                            mpDict.Remove(sSn);
                        }
                    }
                    rgOrdered.AddRange(mpDict.Values);
                    rgList = rgOrdered;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "ImportScriptsToCsv: failed to restore old weapon order");
            }
        }

        try
        {
            CsvService.SaveWeapons(sOutputCsvPath, rgList);
            rgLog.Add($"保存完成: 共 {rgList.Count} 把武器写入 CSV");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ImportScriptsToCsv: CSV save failed");
            rgLog.Add($"CSV保存失败: {ex.Message}");
        }

        string sResult = string.Join("\n", rgLog);
        LogService.Info($"ImportScriptsToCsv done: {iSuccess} ok, {iFailed} fail, {iSkipped} skip, {rgList.Count} total");
        return sResult;
    }

    public static string? ReadAltStatBlockValue(string sContent, string sKey, AltStatMode mode)
    {
        try
        {
            string sBlockName = mpAltStatBlockNames[mode];
            var mBlock = Regex.Match(sContent, $@"{Regex.Escape(sBlockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
            if (!mBlock.Success) return null;
            return ExtractValue(mBlock.Groups[1].Value, sKey);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ReadAltStatBlockValue: key={sKey}, mode={mode}");
            return null;
        }
    }

    public static string WriteAltStatBlockValue(string sContent, string sKey, string sValue, AltStatMode mode)
    {
        try
        {
        string sBlockName = mpAltStatBlockNames[mode];
        var mBlock = Regex.Match(sContent, $@"{Regex.Escape(sBlockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
        if (!mBlock.Success) return sContent;
        string sFullBlock = mBlock.Value.Replace("\r\n", "\n").Replace('\r', '\n');
        string sNewFullBlock = ApplyKeyToBlock(sFullBlock, sKey, sValue, null);
        if (sNewFullBlock == sFullBlock) return sContent;
        return sContent.Replace(sFullBlock, sNewFullBlock);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WriteAltStatBlockValue: key={sKey}, mode={mode}");
            return sContent;
        }
    }

    public static string WriteAltStatRecoilBlock(string sContent, string sRecoilBlock, string? sUp, string? sRight, AltStatMode mode)
    {
        try
        {
        string sBlockName = mpAltStatBlockNames[mode];
        var mAlt = Regex.Match(sContent, $@"{Regex.Escape(sBlockName)}\s*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
        if (!mAlt.Success) return sContent;
        string sAltBlock = mAlt.Groups[1].Value;
        string sNewAltBlock = ReplaceRecoilBlock(sAltBlock, sRecoilBlock, sUp, sRight);
        if (sNewAltBlock == sAltBlock) return sContent;
        return sContent.Replace(sAltBlock, sNewAltBlock);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WriteAltStatRecoilBlock: recoilBlock={sRecoilBlock}, mode={mode}");
            return sContent;
        }
    }

    //如果所有备选值与顶层一致就注释掉整个备选块 如果有不同则激活
    public static string ToggleAltStatBlockComment(string sContent, AltStatMode mode, bool bHasAnyDiff, string? sWeaponName = null)
    {
        try
        {
        string sBlockName = mpAltStatBlockNames[mode];
        var rgLines = sContent.Replace("\r\n", "\n").Split('\n');
        var rgResult = new List<string>();
        int i = 0;
        bool bChanged = false;

        while (i < rgLines.Length)
        {
            string sLine = rgLines[i];
            string sTrimmed = sLine.TrimStart();
            string sIndent = sLine.Length - sTrimmed.Length > 0 ? sLine.Substring(0, sLine.Length - sTrimmed.Length) : "\t";

            bool bIsBlockLine = sTrimmed == sBlockName || sTrimmed == $"//{sBlockName}";
            if (bIsBlockLine)
            {
                int j = i + 1;
                while (j < rgLines.Length && string.IsNullOrWhiteSpace(rgLines[j])) j++;
                if (j < rgLines.Length)
                {
                    string sNextTrimmed = rgLines[j].TrimStart();
                    string sBraceIndent = rgLines[j].Length - sNextTrimmed.Length > 0
                        ? rgLines[j].Substring(0, rgLines[j].Length - sNextTrimmed.Length) : sIndent;
                    bool bOpenCommented = sNextTrimmed == "//{" || sNextTrimmed.StartsWith("//{");
                    bool bOpenActive = sNextTrimmed == "{";

                    if (bOpenCommented || bOpenActive)
                    {
                        int k = j + 1;
                        while (k < rgLines.Length)
                        {
                            string sKt = rgLines[k].TrimStart();
                            if (sKt == "}" || sKt == "//}")
                                break;
                            k++;
                        }

                        if (bHasAnyDiff && bOpenCommented)
                        {
                            rgResult.Add($"{sIndent}{sBlockName}");
                            rgResult.Add($"{sBraceIndent}{{");
                            for (int m = j + 1; m < k; m++)
                                rgResult.Add(rgLines[m]);
                            rgResult.Add($"{sBraceIndent}}}");
                            LogService.Debug($"ToggleAltStatBlockComment: [{sWeaponName}] {mode} activate");
                            bChanged = true;
                            i = k + 1;
                            continue;
                        }
                        else if (!bHasAnyDiff && bOpenActive)
                        {
                            rgResult.Add($"{sIndent}//{sBlockName}");
                            rgResult.Add($"{sBraceIndent}//{{");
                            rgResult.Add($"{sBraceIndent}//}}");
                            LogService.Debug($"ToggleAltStatBlockComment: [{sWeaponName}] {mode} comment out");
                            bChanged = true;
                            i = k + 1;
                            continue;
                        }
                    }
                }
            }
            rgResult.Add(sLine);
            i++;
        }

        return bChanged ? string.Join("\n", rgResult) : sContent;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ToggleAltStatBlockComment: mode={mode}");
            return sContent;
        }
    }

    private static string ApplyKeyToBlock(string sFullBlock, string sKey, string sValue, string? sWeaponName = null)
    {
        int iBracePos = sFullBlock.IndexOf('{');
        int iClosePos = sFullBlock.LastIndexOf('}');
        if (iBracePos < 0 || iClosePos <= iBracePos) return sFullBlock;

        string sBeforeBrace = sFullBlock.Substring(0, iBracePos + 1);
        string sBlockContent = sFullBlock.Substring(iBracePos + 1, iClosePos - iBracePos - 1);
        int iAfterLineStart = sFullBlock.LastIndexOf('\n', iClosePos);
        string sAfterBrace = iAfterLineStart >= 0
            ? sFullBlock.Substring(iAfterLineStart)
            : sFullBlock.Substring(iClosePos);

        var rgLines = new List<string>();
        string sKeyPattern = $"\"{Regex.Escape(sKey)}\"";
        foreach (string sLine in sBlockContent.Split('\n'))
        {
            string sTrimmed = sLine.TrimStart();
            if (sTrimmed.StartsWith(sKeyPattern))
                continue;
            if (!string.IsNullOrWhiteSpace(sLine))
                rgLines.Add(sLine);
        }

        if (!string.IsNullOrEmpty(sValue))
        {
            int iLineStart = sFullBlock.LastIndexOf('\n', iBracePos);
            string sBraceLine = iLineStart >= 0
                ? sFullBlock.Substring(iLineStart + 1, iBracePos - iLineStart - 1)
                : sFullBlock.Substring(0, iBracePos);
            string sBraceIndent = sBraceLine.Length - sBraceLine.TrimStart().Length > 0
                ? sBraceLine.Substring(0, sBraceLine.Length - sBraceLine.TrimStart().Length)
                : "\t";
            string sIndent = sBraceIndent + "\t";
            rgLines.Insert(0, $"{sIndent}\"{sKey}\"\t\t\t\t\"{sValue}\"");
        }

        string sResult = sBeforeBrace + "\n" + string.Join("\n", rgLines) + sAfterBrace;
        sResult = Regex.Replace(sResult, @"(\n\s*){3,}", "\n\n");
        return sResult;
    }

    #endregion
    #region 脚本解析

    private static bool IsStandardFirearm(string sName, string sContent)
    {
        if (sName.Contains("_zombie", StringComparison.OrdinalIgnoreCase)) return false;
        if (sName.StartsWith("weapon_cubemap", StringComparison.OrdinalIgnoreCase)) return false;
        var sType = ExtractValue(sContent, "WeaponType");
        if (!string.IsNullOrEmpty(sType) && hsNonFirearmTypes.Contains(sType)) return false;
        if (ExtractValue(sContent, "ExplosionDamage") != null) return false;
        if (ExtractValue(sContent, "DamageGeneric") == null) return false;
        return true;
    }

    //先尝试带引号的值 再尝试不带引号的裸值 只匹配行首非注释的键 防止把注释掉的kv误识别
    private static string? ExtractValue(string sContent, string sKey)
    {
        //^[ \t]* 确保key在行首且前面没有// 防止匹配到注释块内被注释掉的键
        var m = Regex.Match(sContent, $@"^[ \t]*""{Regex.Escape(sKey)}""\s+""([^""]*)""", RegexOptions.Multiline);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(sContent, $@"^[ \t]*""{Regex.Escape(sKey)}""\s+(\S+)", RegexOptions.Multiline);//回退匹配"abc" 123这种不带引号的裸值
        if (m.Success)
        {
            string sV = m.Groups[1].Value;
            if (sV.StartsWith("\"") && sV.EndsWith("\"")) sV = sV.Substring(1, sV.Length - 2);
            return sV;
        }
        return null;
    }

    private static bool TryParseDouble(string sV, out double dR) =>
        double.TryParse(sV, NumberStyles.Float, CultureInfo.InvariantCulture, out dR);

    //解析后座块 用Singleline跨越换行
    internal static double? ParseRecoilBlock(string sContent, string sBlock, string sKey)
    {
        var m = Regex.Match(sContent, $@"{Regex.Escape(sBlock)}\s*\{{[^}}]*""{Regex.Escape(sKey)}""\s+""([^""]*)""", RegexOptions.Singleline);//匹配block名后紧跟的大括号内容 取指定key的双引号
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dR))
            return dR;
        return null;
    }

    private static double? ParseRecoilBlockInAltStat(string sAltBlock, string sBlockName, string sKey)
    {
        int iIdx = sAltBlock.IndexOf(sBlockName, StringComparison.Ordinal);
        if (iIdx < 0) return null;
        int iBraceStart = sAltBlock.IndexOf('{', iIdx);
        if (iBraceStart < 0) return null;
        int iDepth = 1;
        int i = iBraceStart + 1;
        while (i < sAltBlock.Length && iDepth > 0)
        {
            if (sAltBlock[i] == '{') iDepth++;
            else if (sAltBlock[i] == '}') iDepth--;
            i++;
        }
        string sBlockContent = sAltBlock.Substring(iBraceStart + 1, i - iBraceStart - 2);
        var m = Regex.Match(sBlockContent, $@"""{Regex.Escape(sKey)}""\s+""([^""]*)""");
        if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dR))
            return dR;
        return null;
    }

    private static void ImportAltStatBlock(WeaponData w, string sContent, AltStatMode mode)
    {
        try
        {
            string sBlockName = mpAltStatBlockNames[mode];
            int iBlockIdx = sContent.IndexOf(sBlockName, StringComparison.Ordinal);
            if (iBlockIdx < 0) return;
            int iBraceStart = sContent.IndexOf('{', iBlockIdx);
            if (iBraceStart < 0) return;
            int iDepth = 1;
            int i = iBraceStart + 1;
            while (i < sContent.Length && iDepth > 0)
            {
                if (sContent[i] == '{') iDepth++;
                else if (sContent[i] == '}') iDepth--;
                i++;
            }
            string sBlock = sContent.Substring(iBraceStart + 1, i - iBraceStart - 2);

            if (mode == AltStatMode.Dov)
            {
                TrySetInt(sBlock, "ExtraBulletChamber", out int? n1); w.DovExtraBulletChamber = n1;
                TrySetInt(sBlock, "FireRate", out int? n2); w.DovFireRate = n2;
                TrySetDouble(sBlock, "BulletSpreadDegrees", out double? f1); w.DovBulletSpread = f1;
                TrySetDouble(sBlock, "BulletSpreadDegreesIronsighted", out double? f2); w.DovBulletSpreadDegreesIronsighted = f2;
                TrySetDouble(sBlock, "BulletSpreadDegreesBipod", out double? f18); w.DovBulletSpreadDegreesBipod = f18;
                TrySetDouble(sBlock, "BulletSpreadDegreesBipodIronsighted", out double? f19); w.DovBulletSpreadDegreesBipodIronsighted = f19;
                TrySetDouble(sBlock, "rangemodifier", out double? f3); w.DovRangeModifier = f3;
                TrySetDouble(sBlock, "IronsightSpeedScale", out double? f4); w.DovIronsightSpeedScale = f4;
                TrySetDouble(sBlock, "CrouchSpreadMultiplier", out double? f5); w.DovCrouchSpreadMultiplier = f5;
                TrySetDouble(sBlock, "ProneSpreadMultiplier", out double? f6); w.DovProneSpreadMultiplier = f6;
                TrySetDouble(sBlock, "StandMoveSpreadMultiplier", out double? f7); w.DovStandMoveSpreadMultiplier = f7;
                TrySetDouble(sBlock, "SneakMoveSpreadMultiplier", out double? f8); w.DovSneakMoveSpreadMultiplier = f8;
                TrySetDouble(sBlock, "CrouchMoveSpreadMultiplier", out double? f9); w.DovCrouchMoveSpreadMultiplier = f9;
                TrySetDouble(sBlock, "JumpSpreadMultiplier", out double? f10); w.DovJumpSpreadMultiplier = f10;
                TrySetDouble(sBlock, "DamageHeadMultiplier", out double? f11); w.DovDamageHeadMultiplier = f11;
                TrySetDouble(sBlock, "DamageChestMultiplier", out double? f12); w.DovDamageChestMultiplier = f12;
                TrySetDouble(sBlock, "DamageStomachMultiplier", out double? f13); w.DovDamageStomachMultiplier = f13;
                TrySetDouble(sBlock, "DamageLegMultiplier", out double? f14); w.DovDamageLegMultiplier = f14;
                TrySetDouble(sBlock, "DamageArmMultiplier", out double? f15); w.DovDamageArmMultiplier = f15;
                TrySetDouble(sBlock, "DamageGeneric", out double? f16); w.DovDamageGeneric = f16;
                TrySetDouble(sBlock, "ShakeScale", out double? f20); w.DovShakeScale = f20;
                TrySetDouble(sBlock, "ShakeFreq", out double? f21); w.DovShakeFreq = f21;
                TrySetDouble(sBlock, "ShakeDuration", out double? f22); w.DovShakeDuration = f22;
                TrySetInt(sBlock, "CrosshairMinDistance", out int? n3); w.DovCrosshairMinDistance = n3;
                TrySetInt(sBlock, "CrosshairDeltaDistance", out int? n4); w.DovCrosshairDeltaDistance = n4;
                TrySetDouble(sBlock, "weight", out double? f17); w.DovWeight = f17;
                TrySetInt(sBlock, "ZMBuyPrice", out int? n5); w.DovZMBuyPrice = n5;
                TrySetInt(sBlock, "ZMWeight", out int? n6); w.DovZMWeight = n6;
                TrySetDouble(sBlock, "recoilpushbackvalue", out double? f23); w.DovRecoilPushbackValue = f23;
                TrySetDouble(sBlock, "ironsightwalkbobbingstrength", out double? f24); w.DovIronsightWalkBobbingStrength = f24;
                TrySetDouble(sBlock, "MetalPenetrationDepth", out double? f25); w.DovMetalPenetrationDepth = f25;
                TrySetDouble(sBlock, "GlassPenetrationDepth", out double? f26); w.DovGlassPenetrationDepth = f26;
                TrySetDouble(sBlock, "ConcretePenetrationDepth", out double? f27); w.DovConcretePenetrationDepth = f27;
                TrySetDouble(sBlock, "WoodPenetrationDepth", out double? f28); w.DovWoodPenetrationDepth = f28;
                TrySetDouble(sBlock, "OtherPenetrationDepth", out double? f29); w.DovOtherPenetrationDepth = f29;
                TrySetDouble(sBlock, "MetalDamageModifier", out double? f30); w.DovMetalDamageModifier = f30;
                TrySetDouble(sBlock, "GlassDamageModifier", out double? f31); w.DovGlassDamageModifier = f31;
                TrySetDouble(sBlock, "ConcreteDamageModifier", out double? f32); w.DovConcreteDamageModifier = f32;
                TrySetDouble(sBlock, "WoodDamageModifier", out double? f33); w.DovWoodDamageModifier = f33;
                TrySetDouble(sBlock, "OtherDamageModifier", out double? f34); w.DovOtherDamageModifier = f34;
                TrySetInt(sBlock, "NearwallDistance", out int? n7); w.DovNearwallDistance = n7;
                w.DovFireModes = ExtractValue(sBlock, "SupportedFireModes") ?? "";
                w.DovClipSize = ExtractValue(sBlock, "clip_size") ?? "";
                w.DovViewSlideRecoilUp = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoil", "Up");
                w.DovViewSlideRecoilRight = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoil", "Right");
                w.DovViewSlideRecoilIronsightUp = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoilIronsight", "Up");
                w.DovViewSlideRecoilIronsightRight = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoilIronsight", "Right");
                TrySetInt(sBlock, "SecondaryFireRate", out int? n8); w.DovSecondaryFireRate = n8;
                TrySetInt(sBlock, "IronSight", out int? n9); w.DovIronSight = n9;
                TrySetInt(sBlock, "default_clip", out int? nd0); w.DovDefaultClip = nd0;
                TrySetInt(sBlock, "bullets_per_shot", out int? nd1); w.DovBulletsPerShot = nd1;
            }
            else
            {
                TrySetInt(sBlock, "ExtraBulletChamber", out int? n1); w.ZombieExtraBulletChamber = n1;
                TrySetInt(sBlock, "FireRate", out int? n2); w.ZombieFireRate = n2;
                TrySetDouble(sBlock, "BulletSpreadDegrees", out double? f1); w.ZombieBulletSpread = f1;
                TrySetDouble(sBlock, "BulletSpreadDegreesIronsighted", out double? f2); w.ZombieBulletSpreadDegreesIronsighted = f2;
                TrySetDouble(sBlock, "BulletSpreadDegreesBipod", out double? f18); w.ZombieBulletSpreadDegreesBipod = f18;
                TrySetDouble(sBlock, "BulletSpreadDegreesBipodIronsighted", out double? f19); w.ZombieBulletSpreadDegreesBipodIronsighted = f19;
                TrySetDouble(sBlock, "rangemodifier", out double? f3); w.ZombieRangeModifier = f3;
                TrySetDouble(sBlock, "IronsightSpeedScale", out double? f4); w.ZombieIronsightSpeedScale = f4;
                TrySetDouble(sBlock, "CrouchSpreadMultiplier", out double? f5); w.ZombieCrouchSpreadMultiplier = f5;
                TrySetDouble(sBlock, "ProneSpreadMultiplier", out double? f6); w.ZombieProneSpreadMultiplier = f6;
                TrySetDouble(sBlock, "StandMoveSpreadMultiplier", out double? f7); w.ZombieStandMoveSpreadMultiplier = f7;
                TrySetDouble(sBlock, "SneakMoveSpreadMultiplier", out double? f8); w.ZombieSneakMoveSpreadMultiplier = f8;
                TrySetDouble(sBlock, "CrouchMoveSpreadMultiplier", out double? f9); w.ZombieCrouchMoveSpreadMultiplier = f9;
                TrySetDouble(sBlock, "JumpSpreadMultiplier", out double? f10); w.ZombieJumpSpreadMultiplier = f10;
                TrySetDouble(sBlock, "DamageHeadMultiplier", out double? f11); w.ZombieDamageHeadMultiplier = f11;
                TrySetDouble(sBlock, "DamageChestMultiplier", out double? f12); w.ZombieDamageChestMultiplier = f12;
                TrySetDouble(sBlock, "DamageStomachMultiplier", out double? f13); w.ZombieDamageStomachMultiplier = f13;
                TrySetDouble(sBlock, "DamageLegMultiplier", out double? f14); w.ZombieDamageLegMultiplier = f14;
                TrySetDouble(sBlock, "DamageArmMultiplier", out double? f15); w.ZombieDamageArmMultiplier = f15;
                TrySetDouble(sBlock, "DamageGeneric", out double? f16); w.ZombieDamageGeneric = f16;
                TrySetDouble(sBlock, "ShakeScale", out double? f20); w.ZombieShakeScale = f20;
                TrySetDouble(sBlock, "ShakeFreq", out double? f21); w.ZombieShakeFreq = f21;
                TrySetDouble(sBlock, "ShakeDuration", out double? f22); w.ZombieShakeDuration = f22;
                TrySetInt(sBlock, "CrosshairMinDistance", out int? n3); w.ZombieCrosshairMinDistance = n3;
                TrySetInt(sBlock, "CrosshairDeltaDistance", out int? n4); w.ZombieCrosshairDeltaDistance = n4;
                TrySetDouble(sBlock, "weight", out double? f17); w.ZombieWeight = f17;
                TrySetInt(sBlock, "ZMBuyPrice", out int? _);
                TrySetInt(sBlock, "ZMWeight", out int? _);
                TrySetDouble(sBlock, "recoilpushbackvalue", out double? f23); w.ZombieRecoilPushbackValue = f23;
                TrySetDouble(sBlock, "ironsightwalkbobbingstrength", out double? f24); w.ZombieIronsightWalkBobbingStrength = f24;
                TrySetDouble(sBlock, "MetalPenetrationDepth", out double? f25); w.ZombieMetalPenetrationDepth = f25;
                TrySetDouble(sBlock, "GlassPenetrationDepth", out double? f26); w.ZombieGlassPenetrationDepth = f26;
                TrySetDouble(sBlock, "ConcretePenetrationDepth", out double? f27); w.ZombieConcretePenetrationDepth = f27;
                TrySetDouble(sBlock, "WoodPenetrationDepth", out double? f28); w.ZombieWoodPenetrationDepth = f28;
                TrySetDouble(sBlock, "OtherPenetrationDepth", out double? f29); w.ZombieOtherPenetrationDepth = f29;
                TrySetDouble(sBlock, "MetalDamageModifier", out double? f30); w.ZombieMetalDamageModifier = f30;
                TrySetDouble(sBlock, "GlassDamageModifier", out double? f31); w.ZombieGlassDamageModifier = f31;
                TrySetDouble(sBlock, "ConcreteDamageModifier", out double? f32); w.ZombieConcreteDamageModifier = f32;
                TrySetDouble(sBlock, "WoodDamageModifier", out double? f33); w.ZombieWoodDamageModifier = f33;
                TrySetDouble(sBlock, "OtherDamageModifier", out double? f34); w.ZombieOtherDamageModifier = f34;
                TrySetInt(sBlock, "NearwallDistance", out int? n7); w.ZombieNearwallDistance = n7;
                w.ZombieFireModes = ExtractValue(sBlock, "SupportedFireModes") ?? "";
                w.ZombieClipSize = ExtractValue(sBlock, "clip_size") ?? "";
                w.ZombieViewSlideRecoilUp = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoil", "Up");
                w.ZombieViewSlideRecoilRight = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoil", "Right");
                w.ZombieViewSlideRecoilIronsightUp = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoilIronsight", "Up");
                w.ZombieViewSlideRecoilIronsightRight = ParseRecoilBlockInAltStat(sBlock, "ViewSlideRecoilIronsight", "Right");
                TrySetInt(sBlock, "SecondaryFireRate", out int? n8); w.ZombieSecondaryFireRate = n8;
                TrySetInt(sBlock, "IronSight", out int? n9); w.ZombieIronSight = n9;
                TrySetInt(sBlock, "default_clip", out int? nz0); w.ZombieDefaultClip = nz0;
                TrySetInt(sBlock, "bullets_per_shot", out int? nz1); w.ZombieBulletsPerShot = nz1;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponScriptService.ImportAltStatBlock: mode={mode}");
        }
    }

    #endregion
    #region 字段读写替换

    internal static string? GetFieldValue(WeaponData w, string h, AltStatMode? mode) => h switch
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
        "ZMBuyPrice" => AltI(w.ZMBuyPrice, w.DovZMBuyPrice, null, mode),
        "ZMWeight" => AltI(w.ZMWeight, w.DovZMWeight, null, mode),
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
    private static string? AltS(string? sTop, string? sDov, string? sZombie, AltStatMode? mode) => mode switch
    {
        null => sTop,
        AltStatMode.Dov => string.IsNullOrEmpty(sDov) || string.Equals(sDov, sTop, StringComparison.OrdinalIgnoreCase) ? null : sDov,
        AltStatMode.Zombie => string.IsNullOrEmpty(sZombie) || string.Equals(sZombie, sTop, StringComparison.OrdinalIgnoreCase) ? null : sZombie,
        _ => null
    };

    //double版本
    private static string? AltF(double? fTop, double? fDov, double? fZombie, AltStatMode? mode)
    {
        double? fV = mode switch
        {
            null => fTop,
            AltStatMode.Dov => fDov,
            AltStatMode.Zombie => fZombie,
            _ => null
        };
        double dTopVal = fTop ?? 0.0;
        if (mode != null && fV.HasValue && Math.Abs(fV.Value - dTopVal) < 0.001) return null;
        return F(fV);
    }

    //int版本
    private static string? AltI(int? nTop, int? nDov, int? nZombie, AltStatMode? mode)
    {
        int? nV = mode switch
        {
            null => nTop,
            AltStatMode.Dov => nDov,
            AltStatMode.Zombie => nZombie,
            _ => null
        };
        int iTopVal = nTop ?? 0;
        if (mode != null && nV.HasValue && nV.Value == iTopVal) return null;
        return nV?.ToString();
    }

    private static string? F(double? fV) => fV.HasValue ? fV.Value.ToString("0.####", CultureInfo.InvariantCulture) : null;

    private static void TrySetInt(string sBlock, string sKey, out int? nVal)
    {
        nVal = null;
        if (ExtractValue(sBlock, sKey) is string sV && int.TryParse(sV, out int iR))
            nVal = iR;
    }

    private static void TrySetDouble(string sBlock, string sKey, out double? fVal)
    {
        fVal = null;
        if (ExtractValue(sBlock, sKey) is string sV && TryParseDouble(sV, out double dR))
            fVal = dR;
    }

    //替换脚本中的键值对 仅替换第一个未被注释的匹配 防止误伤嵌套块内的同名键
    private static string ReplaceKeyValue(string sC, string sK, string sV)
    {
        try
        {
        //匹配"key" "value"格式的行 要求行首不是//(即非注释行) 捕获key及其前导空白 旧值和行尾注释
        string sP = $@"(^[ \t]*""{Regex.Escape(sK)}""\s+)""[^""]*""(\s*(?://.*)?)";
        var m = Regex.Match(sC, sP, RegexOptions.Multiline);
        if (m.Success)
        {
            //只替换第一个匹配 后面的同名键(如在嵌套块内)不受影响
            return sC.Remove(m.Index, m.Length)
                    .Insert(m.Index, $@"{m.Groups[1].Value}""{sV}""{m.Groups[2].Value}");
        }
        return sC;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ReplaceKeyValue: key={sK}");
            return sC;
        }
    }

    private static string ReplaceRecoilBlock(string sC, string sBlock, string? sUp, string? sRight)
    {
        try
        {
        //匹配block 限制在WeaponData顶层 使用RegexOptions来匹配大括号
        //通过 ^(?!\s*//) 确保块名不在注释行内 加.*平衡大括号嵌套
        string sP = $@"(^{Regex.Escape(sBlock)}\s*\{{(?:[^{{}}]|(?<open>\{{)|(?<-open>\}}))*(?(open)(?!))\}})";
        var m = Regex.Match(sC, sP, RegexOptions.Multiline | RegexOptions.Singleline);
        if (!m.Success) return sC;
        string sB = m.Value;

        if (sUp != null)
        {
            string sUpPat = $@"(""Up""\s+)""[^""]*""";
            var mUp = Regex.Match(sB, sUpPat, RegexOptions.Singleline);
            if (mUp.Success)
                sB = sB.Remove(mUp.Index, mUp.Length)
                     .Insert(mUp.Index, $@"$1""{sUp}""".Replace("$1", mUp.Groups[1].Value));
        }
        if (sRight != null)
        {
            string sRightPat = $@"(""Right""\s+)""[^""]*""";
            var mRight = Regex.Match(sB, sRightPat, RegexOptions.Singleline);
            if (mRight.Success)
                sB = sB.Remove(mRight.Index, mRight.Length)
                     .Insert(mRight.Index, $@"$1""{sRight}""".Replace("$1", mRight.Groups[1].Value));
        }

        return sC.Remove(m.Index, m.Length).Insert(m.Index, sB);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ReplaceRecoilBlock: block={sBlock}");
            return sC;
        }
    }
    #endregion
}