using System;
using System.Collections.Generic;

namespace WeaponDamageCalc.Models;

public class LoadoutInfo
{
    public HashSet<string> Factions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Absorb(LoadoutInfo other)
    {
        Factions.UnionWith(other.Factions);
        Classes.UnionWith(other.Classes);
        Groups.UnionWith(other.Groups);
        Sources.UnionWith(other.Sources);
    }
}