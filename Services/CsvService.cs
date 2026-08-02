using System.Collections.Generic;
using System.IO;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc.Services;

public static class CsvService
{
    public static List<WeaponData> LoadWeapons(string filePath)
    {
        LogService.Info($"Loading CSV: {filePath}");
        try
        {
            var result = CsvMapper.Read<WeaponData>(filePath);
            LogService.Info($"CSV loaded: {result.Count} weapons");
            return result;
        }
        catch (System.Exception ex)
        {
            LogService.Error(ex, "CsvService.LoadWeapons");
            return new List<WeaponData>();
        }
    }

    public static void SaveWeapons(string filePath, List<WeaponData> weapons)
    {
        LogService.Info($"Saving CSV: {filePath} ({weapons.Count} weapons)");
        try
        {
            CsvMapper.Write(filePath, weapons);
            LogService.Info("CSV saved successfully");
        }
        catch (System.Exception ex)
        {
            LogService.Error(ex, "CsvService.SaveWeapons");
            throw;
        }
    }
}