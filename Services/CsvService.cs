using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
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
            ShouldSkipRecord = args =>//跳过排版用空行 首字段为空则整行忽略
            {
                var firstField = args.Row.GetField(0);
                return string.IsNullOrWhiteSpace(firstField);
            },
            PrepareHeaderForMatch = args => args.Header.Trim().Trim('"')
        };

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config);

        //空字符串视为null 否则空字段解析会抛异常
        csv.Context.TypeConverterOptionsCache.GetOptions<double?>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<int?>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<double>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<int>().NullValues.Add(string.Empty);
        csv.Context.TypeConverterOptionsCache.GetOptions<string>().NullValues.Add(string.Empty);

        return csv.GetRecords<WeaponData>().ToList();
    }

    public static void SaveWeapons(string filePath, List<WeaponData> weapons)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)//所有字段都加引号 防止特殊字符破坏CSV格式
        {
            HasHeaderRecord = true,
            ShouldQuote = args => true
        };

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        using var csv = new CsvWriter(writer, config);

        var doubleOptions = new TypeConverterOptions { Formats = new[] { "0.####" } };
        csv.Context.TypeConverterOptionsCache.AddOptions<double>(doubleOptions);
        csv.Context.TypeConverterOptionsCache.AddOptions<double?>(doubleOptions);

        csv.WriteHeader<WeaponData>();
        csv.NextRecord();

        foreach (var weapon in weapons)
        {
            csv.WriteRecord(weapon);
            csv.NextRecord();
        }
    }
}