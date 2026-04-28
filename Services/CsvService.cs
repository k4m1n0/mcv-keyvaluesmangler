using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Services;

public static class CsvService
{
    public static List<WeaponData> LoadWeapons(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            ShouldSkipRecord = args =>
            {
                var firstField = args.Row.GetField(0);
                return string.IsNullOrWhiteSpace(firstField);
            },
            PrepareHeaderForMatch = args => args.Header.Trim().Trim('"')
        };

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        csv.Context.TypeConverterOptionsCache.GetOptions<double?>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<int?>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<double>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<int>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<string>().NullValues.Add(string.Empty);

        return csv.GetRecords<WeaponData>().ToList();
    }

    public static void SaveWeapons(string filePath, List<WeaponData> weapons)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            ShouldQuote = args => true
        };

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        using var csv = new CsvWriter(writer, config);

        csv.WriteHeader<WeaponData>();
        csv.NextRecord();

        foreach (var weapon in weapons)
        {
            csv.WriteRecord(weapon);
            csv.NextRecord();
        }
    }
}