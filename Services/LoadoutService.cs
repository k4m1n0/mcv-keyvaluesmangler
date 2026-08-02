using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Services;

public static class LoadoutService
{
    private static readonly HashSet<string> hsFactions = new(StringComparer.OrdinalIgnoreCase) { "US", "VC" };
    private static readonly HashSet<string> hsClasses = new(StringComparer.OrdinalIgnoreCase)
        { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };
    private static readonly Dictionary<string, string> mpGroupToType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assault_rifles"] = "Assault Rifle", ["battle_rifles"] = "Battle Rifle",
        ["bolt_actions"] = "Bolt Action Rifle", ["carbines"] = "Carbine",
        ["grenade_launchers"] = "Grenade Launcher", ["grenades"] = "Grenade",
        ["lmgs"] = "Light Machine Gun", ["machine_pistols"] = "Machine Pistol",
        ["melee"] = "Melee", ["pistols"] = "Pistol", ["revolvers"] = "Revolver",
        ["rifle_grenades"] = "Rifle Grenade", ["rocket_launchers"] = "Rocket Launcher",
        ["shotguns"] = "Shotgun", ["sniper_rifles"] = "Sniper Rifle", ["smgs"] = "Submachine Gun",
    };

    public static readonly string[] rgClassOrder = { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };
    public static readonly Dictionary<string, string> mpClassImage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assault"] = "Class_Assault.png", ["medic"] = "Class_medic.png",
        ["gunner"] = "Class_Gunner.png", ["sniper"] = "Class_sniper.png",
        ["engineer"] = "Class_Engineer.png", ["radioman"] = "Class_radioman.png",
    };

    public static Dictionary<string, LoadoutInfo> LoadAll(string sResourceDir)
    {
        LogService.Info($"Loading loadout from: {sResourceDir}");
        var mpResult = new Dictionary<string, LoadoutInfo>(StringComparer.OrdinalIgnoreCase);
        var rgFiles = new[] { "vietnam_loadout.txt", "vietnam_loadout_zombie.txt", "vietnam_loadout_special.txt" };
        var rgSourceNames = new[] { "main", "zombie", "special" };

        for (int i = 0; i < rgFiles.Length; i++)
        {
            string sPath = Path.Combine(sResourceDir, rgFiles[i]);
            if (!File.Exists(sPath))
            {
                LogService.Warn($"Loadout file not found: {sPath}");
                continue;
            }
            var mpParsed = ParseFile(sPath, rgSourceNames[i]);
            LogService.Info($"  {rgFiles[i]}: {mpParsed.Count} weapons");
            foreach (var kvp in mpParsed)
            {
                if (!mpResult.ContainsKey(kvp.Key)) mpResult[kvp.Key] = new LoadoutInfo();
                mpResult[kvp.Key].Absorb(kvp.Value);
            }
        }
        LogService.Info($"Loadout total: {mpResult.Count} weapons");
        return mpResult;
    }

    private static Dictionary<string, LoadoutInfo> ParseFile(string sPath, string sSourceName)
    {
        var mpResult = new Dictionary<string, LoadoutInfo>(StringComparer.OrdinalIgnoreCase);
        string sContent = WeaponScriptService.ReadScriptFile(sPath)
            .Replace("\r\n", "\n").Replace('\r', '\n');
        var rgStack = new List<string>();
        string? sPendingKey = null;

        string? CurrentFaction() => rgStack.FindLast(s => hsFactions.Contains(s));
        string? CurrentClass() => rgStack.FindLast(s => hsClasses.Contains(s));
        string? CurrentGroup() => rgStack.FindLast(s => mpGroupToType.ContainsKey(s));

        foreach (string sRawLine in sContent.Split('\n'))
        {
            string sLine = LocalizationService.StripComment(sRawLine).Trim();
            if (string.IsNullOrEmpty(sLine)) continue;

            var mInlineOpen = Regex.Match(sLine, @"""([^""]+)""\s*\{");
            if (mInlineOpen.Success) { rgStack.Add(mInlineOpen.Groups[1].Value); continue; }
            if (sLine == "{") { rgStack.Add(sPendingKey ?? "__anon__"); sPendingKey = null; continue; }
            if (sLine == "}") { if (rgStack.Count > 0) rgStack.RemoveAt(rgStack.Count - 1); continue; }

            var mKv = Regex.Match(sLine, @"""([^""]+)""\s+""([^""]*)""");
            if (!mKv.Success) { var mKo = Regex.Match(sLine, @"""([^""]+)""$"); if (mKo.Success) sPendingKey = mKo.Groups[1].Value; continue; }

            string sKey = mKv.Groups[1].Value;
            if (!sKey.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)) continue;
            var liInfo = mpResult.TryGetValue(sKey, out var liExisting) ? liExisting : new LoadoutInfo();
            mpResult[sKey] = liInfo;

            var sFaction = CurrentFaction(); if (sFaction != null) liInfo.Factions.Add(sFaction);
            var sCls = CurrentClass(); if (sCls != null) liInfo.Classes.Add(sCls);
            var sGroup = CurrentGroup(); if (sGroup != null) liInfo.Groups.Add(sGroup);
            liInfo.Sources.Add(sSourceName);
        }
        return mpResult;
    }

    public static string GetWeaponType(Dictionary<string, string> mpVals, LoadoutInfo? liInfo)
    {
        if (liInfo != null)
            foreach (var sG in liInfo.Groups)
                if (mpGroupToType.TryGetValue(sG, out var sType)) return sType;
        return mpVals.TryGetValue("WeaponType", out var sRaw) ? sRaw : "Unknown";
    }

    public static string GetResourceDir(string sScriptsDir)
    {
        return Path.GetFullPath(Path.Combine(sScriptsDir, "..", "resource"));
    }
}