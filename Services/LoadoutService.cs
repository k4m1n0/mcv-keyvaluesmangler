using System;
using System.Collections.Generic;
using System.IO;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Services;

public static class LoadoutService
{
    private static readonly HashSet<string> Factions = new(StringComparer.OrdinalIgnoreCase) { "US", "VC" };
    private static readonly HashSet<string> Classes = new(StringComparer.OrdinalIgnoreCase)
        { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };
    private static readonly Dictionary<string, string> GroupToType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assault_rifles"] = "Assault Rifle", ["battle_rifles"] = "Battle Rifle",
        ["bolt_actions"] = "Bolt Action Rifle", ["carbines"] = "Carbine",
        ["grenade_launchers"] = "Grenade Launcher", ["grenades"] = "Grenade",
        ["lmgs"] = "Light Machine Gun", ["machine_pistols"] = "Machine Pistol",
        ["melee"] = "Melee", ["pistols"] = "Pistol", ["revolvers"] = "Revolver",
        ["rifle_grenades"] = "Rifle Grenade", ["rocket_launchers"] = "Rocket Launcher",
        ["shotguns"] = "Shotgun", ["sniper_rifles"] = "Sniper Rifle", ["smgs"] = "Submachine Gun",
    };

    public static readonly string[] ClassOrder = { "assault", "medic", "gunner", "sniper", "engineer", "radioman" };
    public static readonly Dictionary<string, string> ClassImageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assault"] = "Class_Assault.png", ["medic"] = "Class_medic.png",
        ["gunner"] = "Class_Gunner.png", ["sniper"] = "Class_sniper.png",
        ["engineer"] = "Class_Engineer.png", ["radioman"] = "Class_radioman.png",
    };

    public static Dictionary<string, LoadoutInfo> LoadAll(string resourceDir)
    {
        var result = new Dictionary<string, LoadoutInfo>(StringComparer.OrdinalIgnoreCase);
        var files = new[] { "vietnam_loadout.txt", "vietnam_loadout_zombie.txt", "vietnam_loadout_special.txt" };
        var sourceNames = new[] { "main", "zombie", "special" };

        for (int i = 0; i < files.Length; i++)
        {
            string path = Path.Combine(resourceDir, files[i]);
            if (!File.Exists(path)) continue;
            foreach (var kv in ParseFile(path, sourceNames[i]))
            {
                if (!result.ContainsKey(kv.Key)) result[kv.Key] = new LoadoutInfo();
                result[kv.Key].Absorb(kv.Value);
            }
        }
        return result;
    }

    private static Dictionary<string, LoadoutInfo> ParseFile(string path, string sourceName)
    {
        var result = new Dictionary<string, LoadoutInfo>(StringComparer.OrdinalIgnoreCase);
        string content = WeaponScriptService.ReadScriptFile(path)
            .Replace("\r\n", "\n").Replace('\r', '\n');
        var stack = new List<string>();
        string? pendingKey = null;

        string? CurrentFaction() => stack.FindLast(s => Factions.Contains(s));
        string? CurrentClass() => stack.FindLast(s => Classes.Contains(s));
        string? CurrentGroup() => stack.FindLast(s => GroupToType.ContainsKey(s));

        foreach (string rawLine in content.Split('\n'))
        {
            string line = LocalizationService.StripComment(rawLine).Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var inlineOpen = System.Text.RegularExpressions.Regex.Match(line, @"""([^""]+)""\s*\{");
            if (inlineOpen.Success) { stack.Add(inlineOpen.Groups[1].Value); continue; }
            if (line == "{") { stack.Add(pendingKey ?? "__anon__"); pendingKey = null; continue; }
            if (line == "}") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }

            var kv = System.Text.RegularExpressions.Regex.Match(line, @"""([^""]+)""\s+""([^""]*)""");
            if (!kv.Success) { var ko = System.Text.RegularExpressions.Regex.Match(line, @"""([^""]+)""$"); if (ko.Success) pendingKey = ko.Groups[1].Value; continue; }

            string key = kv.Groups[1].Value;
            if (!key.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)) continue;
            var info = result.TryGetValue(key, out var existing) ? existing : new LoadoutInfo();
            result[key] = info;

            var faction = CurrentFaction(); if (faction != null) info.Factions.Add(faction);
            var cls = CurrentClass(); if (cls != null) info.Classes.Add(cls);
            var group = CurrentGroup(); if (group != null) info.Groups.Add(group);
            info.Sources.Add(sourceName);
        }
        return result;
    }

    public static string GetWeaponType(Dictionary<string, string> vals, LoadoutInfo? info)
    {
        if (info != null)
            foreach (var g in info.Groups)
                if (GroupToType.TryGetValue(g, out var t)) return t;
        return vals.TryGetValue("WeaponType", out var raw) ? raw : "Unknown";
    }

    public static string GetResourceDir(string scriptsDir)
    {
        return Path.GetFullPath(Path.Combine(scriptsDir, "..", "resource"));
    }
}