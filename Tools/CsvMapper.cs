using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace WeaponDamageCalc.Tools;

public static class CsvMapper
{
    #region 列反射与缓存

    private static readonly Dictionary<Type, ColumnMap[]> Cache = new();

    public static ColumnMap[] GetColumns<T>()
    {
        var type = typeof(T);
        if (Cache.TryGetValue(type, out var cached))
            return cached;

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var list = new List<ColumnMap>();
        foreach (var p in props)
        {
            var name = p.Name;
            var attr = p.GetCustomAttribute<CsvColumnAttribute>();
            if (attr != null)
                name = attr.Name;
            list.Add(new ColumnMap(name, p));
        }
        var arr = list.ToArray();
        Cache[type] = arr;
        return arr;
    }

    #endregion
    #region 公开接口

    public static List<T> Read<T>(string path) where T : new()
    {
        var result = new List<T>();
        var columns = GetColumns<T>();
        if (columns.Length == 0) return result;

        string content;
        try
        {
            content = ReadAllTextWithBomDetection(path);
        }
        catch (Exception ex)
        {
            LogError($"Failed to read file: {path} — {ex.Message}");
            return result;
        }

        var lines = SplitLines(content);

        int headerIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var firstField = GetField(lines[i], 0);
            if (!string.IsNullOrWhiteSpace(firstField))
            {
                //如果首字段不含任何字母 文件可能缺少header行
                if (firstField.IndexOfAny("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray()) < 0)
                    LogWarn($"Header row's first field '{firstField}' contains no letters. File may be missing a header row.");

                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0) return result;

        var headers = SplitRow(lines[headerIdx]);
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var h = headers[i].Trim();
            if (string.IsNullOrEmpty(h)) continue;
            if (!headerMap.ContainsKey(h))
            {
                headerMap[h] = i;
            }
            else
            {
                LogWarn($"Duplicate column '{h}' at index {i}, using first occurrence at index {headerMap[h]}.");
            }
        }

        for (int i = headerIdx + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Trim().Replace(",", "").Length == 0) continue;//整行都是逗号

            var firstField = GetField(line, 0);
            if (string.IsNullOrWhiteSpace(firstField)) continue;

            var fields = SplitRow(line);
            var obj = new T();
            foreach (var col in columns)
            {
                if (!headerMap.TryGetValue(col.HeaderName, out int idx))
                    continue;
                var raw = idx < fields.Count ? fields[idx] : "";
                if (string.IsNullOrEmpty(raw))
                    raw = null;
                SetValue(obj, col.Property, raw);
            }
            result.Add(obj);
        }
        return result;
    }

    public static void Write<T>(string path, List<T> items)
    {
        var columns = GetColumns<T>();
        var sb = new StringBuilder();

        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(EscapeCsvField(columns[i].HeaderName)).Append('"');
        }
        sb.AppendLine();

        foreach (var item in items)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var rawVal = GetValue(item, columns[i].Property);
                sb.Append('"').Append(EscapeCsvField(rawVal)).Append('"');
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    #endregion
    #region 编码检测

    private static string ReadAllTextWithBomDetection(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try { return Encoding.UTF8.GetString(bytes); }
        catch
        {
            LogWarn($"UTF-8 decoding failed for {path}, falling back to system default encoding.");
            return Encoding.Default.GetString(bytes);
        }
    }

    #endregion
    #region CSV解析

    //引号内超过此阈值仍未闭合则强制退出 防止损坏的csv吞掉后续所有行
    private const int MaxQuotedFieldLength = 100_000;

    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int quoteStart = -1;

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes && quoteStart >= 0 && (i - quoteStart) > MaxQuotedFieldLength)
            {
                LogWarn($"Unclosed quote forced closure at position {i}.");
                inQuotes = false;
                quoteStart = -1;
            }

            if (c == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    quoteStart = inQuotes ? i : -1;
                }
            }
            else if (c == '\n' && !inQuotes)
            {
                lines.Add(sb.ToString().TrimEnd('\r'));
                sb.Clear();
            }
            else if (c == '\r' && !inQuotes)
            {
                if (i + 1 >= content.Length || content[i + 1] != '\n')
                {
                    lines.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (inQuotes)
            LogWarn($"Unclosed quote at end of file.");
        if (sb.Length > 0)
            lines.Add(sb.ToString());
        return lines;
    }

    private static List<string> SplitRow(string row)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int quoteStart = -1;

        for (int i = 0; i < row.Length; i++)
        {
            var c = row[i];

            if (inQuotes && quoteStart >= 0 && (i - quoteStart) > MaxQuotedFieldLength)
            {
                LogWarn($"Unclosed quote forced closure in row at column ~{fields.Count + 1}.");
                inQuotes = false;
                quoteStart = -1;
            }

            if (c == '"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    quoteStart = inQuotes ? i : -1;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        if (inQuotes)
            LogWarn($"Unclosed quote at end of row.");
        fields.Add(sb.ToString());

        //去掉外层引号 先Trim防行首空格
        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i].Trim();
            if (f.Length >= 2 && f.StartsWith("\"") && f.EndsWith("\""))
                fields[i] = f.Substring(1, f.Length - 2);
            else
                fields[i] = f;
        }

        return fields;
    }

    private static string GetField(string row, int index)
    {
        var fields = SplitRow(row);
        return index < fields.Count ? fields[index] : "";
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        return field.Replace("\"", "\"\"");
    }

    #endregion
    #region 值类型转换

    private static void SetValue<T>(T obj, PropertyInfo prop, string? raw)
    {
        try
        {
            if (raw == null)
            {
                if (IsNullable(prop.PropertyType))
                    prop.SetValue(obj, null);
                return;
            }

            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (targetType == typeof(string))
            {
                prop.SetValue(obj, raw);
            }
            else if (targetType == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                    prop.SetValue(obj, iv);
                else if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out int iv2))
                    prop.SetValue(obj, iv2);
                else
                {
                    LogWarn($"Failed to parse int '{prop.Name}': '{raw}'. Set to null.");
                    prop.SetValue(obj, null);
                }
            }
            else if (targetType == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                    prop.SetValue(obj, dv);
                else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out double dv2))
                    prop.SetValue(obj, dv2);
                else
                {
                    LogWarn($"Failed to parse double '{prop.Name}': '{raw}'. Set to null.");
                    prop.SetValue(obj, null);
                }
            }
        }
        catch (Exception ex)
        {
            LogWarn($"SetValue failed for '{prop.Name}' with '{raw}': {ex.Message}");
        }
    }

    private static string? GetValue<T>(T obj, PropertyInfo prop)
    {
        var val = prop.GetValue(obj);
        if (val == null) return "";

        if (val is double dv)
        {
            if (double.IsNaN(dv) || double.IsInfinity(dv))
            {
                LogWarn($"NaN/Infinity in double field '{prop.Name}'. Writing as empty.");
                return "";
            }
            if (dv == 0) return "0";
            return dv.ToString("0.####", CultureInfo.InvariantCulture);
        }
        if (val is int iv && iv == 0) return "0";
        return val.ToString();
    }

    private static bool IsNullable(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    #endregion
    #region 日志桥接

    private static void LogWarn(string msg)
    {
        try
        {
            var logType = Type.GetType("WeaponDamageCalc.LogService, WeaponDamageCalc");
            logType?.GetMethod("Warn", new[] { typeof(string) })
                   ?.Invoke(null, new object[] { "[CsvMapper] " + msg });
        }
        catch { }
        System.Diagnostics.Debug.WriteLine($"[CsvMapper WARN] {msg}");
    }

    private static void LogError(string msg)
    {
        try
        {
            var logType = Type.GetType("WeaponDamageCalc.LogService, WeaponDamageCalc");
            logType?.GetMethod("Error", new[] { typeof(string) })
                   ?.Invoke(null, new object[] { "[CsvMapper] " + msg });
        }
        catch { }
        System.Diagnostics.Debug.WriteLine($"[CsvMapper ERROR] {msg}");
    }

    #endregion
}

public class ColumnMap
{
    public string HeaderName { get; }
    public PropertyInfo Property { get; }
    public ColumnMap(string headerName, PropertyInfo property)
    {
        HeaderName = headerName;
        Property = property;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class CsvColumnAttribute : Attribute
{
    public string Name { get; }
    public CsvColumnAttribute(string name) => Name = name;
}