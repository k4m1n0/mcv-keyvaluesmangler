using System.Collections.Generic;
using System.IO;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc.Services;

public static class CsvService
{
    public static List<WeaponData> LoadWeapons(string sFilePath)
    {
        LogService.Info($"Loading CSV: {sFilePath}");
        try
        {
            var rgResult = CsvMapper.Read<WeaponData>(sFilePath);
            LogService.Info($"CSV loaded: {rgResult.Count} weapons");
            return rgResult;
        }
        catch (System.Exception ex)
        {
            LogService.Error(ex, "CsvService.LoadWeapons");
            return new List<WeaponData>();
        }
    }

    public static void SaveWeapons(string sFilePath, List<WeaponData> rgWeapons)
    {
        LogService.Info($"Saving CSV: {sFilePath} ({rgWeapons.Count} weapons)");
        try
        {
            CsvMapper.Write(sFilePath, rgWeapons);
            LogService.Info("CSV saved successfully");
        }
        catch (System.Exception ex)
        {
            LogService.Error(ex, "CsvService.SaveWeapons");
            throw;
        }
    }
}