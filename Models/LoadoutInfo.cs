using System;
using System.Collections.Generic;

namespace WeaponDamageCalc.Models;

public class LoadoutInfo
{
    public HashSet<string> Factions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    //main模式的职业/阵营/分组 用于wiki显示
    public HashSet<string> MainFactions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MainClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MainGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Absorb(LoadoutInfo other, bool bIsMain = false)
    {
        Factions.UnionWith(other.Factions);
        Classes.UnionWith(other.Classes);
        Groups.UnionWith(other.Groups);
        Sources.UnionWith(other.Sources);
        if (bIsMain)
        {
            MainFactions.UnionWith(other.Factions);
            MainClasses.UnionWith(other.Classes);
            MainGroups.UnionWith(other.Groups);
        }
    }
}