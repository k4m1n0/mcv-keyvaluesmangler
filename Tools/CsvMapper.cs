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
    private static readonly Dictionary<Type, ColumnMap[]> mpCache = new();

    public static ColumnMap[] GetColumns<T>()
    {
        var tType = typeof(T);
        if (mpCache.TryGetValue(tType, out var rgCached))
            return rgCached;

        var rgProps = tType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var rgList = new List<ColumnMap>();
        foreach (var pi in rgProps)
        {
            var sName = pi.Name;
            var attrCol = pi.GetCustomAttribute<CsvColumnAttribute>();
            if (attrCol != null)
                sName = attrCol.Name;
            rgList.Add(new ColumnMap(sName, pi));
        }
        var rgArr = rgList.ToArray();
        mpCache[tType] = rgArr;
        return rgArr;
    }

    #endregion
    #region 公开接口

    public static List<T> Read<T>(string sPath, bool bShowWarnings = true) where T : new()
    {
        var rgResult = new List<T>();
        var rgWarnings = new List<string>();
        var rgColumns = GetColumns<T>();
        if (rgColumns.Length == 0) return rgResult;

        string sContent;
        try
        {
            sContent = ReadAllTextWithBomDetection(sPath);
        }
        catch (Exception ex)
        {
            LogError($"Failed to read file: {sPath} - {ex.Message}");
            return rgResult;
        }

        void Warn(string sMsg) { LogWarn(sMsg); rgWarnings.Add(sMsg); }

        var rgLines = SplitLines(sContent, Warn);

        int iHeaderIdx = -1;
        for (int i = 0; i < rgLines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rgLines[i]))
                continue;

            var rgFirst = SplitRow(rgLines[i], Warn);
            var sFirstField = rgFirst.Count > 0 ? rgFirst[0] : "";
            if (!string.IsNullOrWhiteSpace(sFirstField))
            {
                //如果首字段不含任何字母 文件可能缺少header行
                if (sFirstField.IndexOfAny(s_rgLetters) < 0)
                    Warn($"Header row's first field '{sFirstField}' contains no letters. File may be missing a header row.");

                iHeaderIdx = i;
                break;
            }
        }
        if (iHeaderIdx < 0) return rgResult;

        var rgHeaders = SplitRow(rgLines[iHeaderIdx], Warn);
        var mpHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rgHeaders.Count; i++)
        {
            var sH = rgHeaders[i].Trim();
            if (string.IsNullOrEmpty(sH)) continue;
            if (!mpHeader.ContainsKey(sH))
            {
                mpHeader[sH] = i;
            }
            else
            {
                Warn($"Duplicate column '{sH}' at index {i}, using first occurrence at index {mpHeader[sH]}.");
            }
        }

        for (int i = iHeaderIdx + 1; i < rgLines.Count; i++)
        {
            var sLine = rgLines[i];
            if (string.IsNullOrWhiteSpace(sLine)) continue;
            if (sLine.Trim().Replace(",", "").Length == 0) continue;//整行都是逗号

            var rgFields = SplitRow(sLine, Warn);
            if (rgFields.Count == 0) continue;
            if (rgFields.Count == 1 && string.IsNullOrWhiteSpace(rgFields[0])) continue;
            if (rgFields.All(f => string.IsNullOrWhiteSpace(f))) continue;
            var obj = new T();
            foreach (var col in rgColumns)
            {
                if (!mpHeader.TryGetValue(col.HeaderName, out int iIdx))
                    continue;
                var sRaw = iIdx < rgFields.Count ? rgFields[iIdx] : "";
                if (string.IsNullOrEmpty(sRaw))
                    sRaw = null;
                SetValue(obj, col.Property, sRaw, Warn);
            }
            rgResult.Add(obj);
        }
        if (rgResult.Count > 0 && bShowWarnings)
            ShowWarnings(rgWarnings);
        return rgResult;
    }

    public static void Write<T>(string sPath, List<T> rgItems)
    {
        var rgColumns = GetColumns<T>();
        var sb = new StringBuilder();

        for (int i = 0; i < rgColumns.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(EscapeCsvField(rgColumns[i].HeaderName)).Append('"');
        }
        sb.AppendLine();

        foreach (var item in rgItems)
        {
            for (int i = 0; i < rgColumns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var sRawVal = GetValue(item, rgColumns[i].Property);
                sb.Append('"').Append(EscapeCsvField(sRawVal)).Append('"');
            }
            sb.AppendLine();
        }

        File.WriteAllText(sPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static bool bSuppressMessageBox = false;

    private static void ShowWarnings(List<string> rgWarnings)
    {
        if (rgWarnings.Count == 0) return;

        var rgDeduped = rgWarnings
            .GroupBy(s =>
            {
                int iColon = s.IndexOf(':');
                if (iColon < 0) return s;
                return s[..iColon]
                    .Replace(" int ", " number ")
                    .Replace(" double ", " number ");
            })
            .Select(g => g.Count() == 1 ? g.First() : $"{g.Count()} fields: {g.Key}")
            .ToList();

        var sb = new StringBuilder();
        int nShow = Math.Min(rgDeduped.Count, 8);
        for (int i = 0; i < nShow; i++)
            sb.AppendLine(rgDeduped[i]);

        if (rgDeduped.Count > 8)
        {
            sb.AppendLine();
            sb.AppendLine($"+ {rgDeduped.Count - 8} more issue types ({rgWarnings.Count} total).");
            sb.AppendLine("Check the log file for full details.");
        }

        if (!bSuppressMessageBox)
            MessageBox.Show(sb.ToString(), $"CSV Parse Warnings ({rgWarnings.Count})",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    #endregion
    #region 编码检测

    private static string ReadAllTextWithBomDetection(string sPath)
    {
        byte[] rgBytes = File.ReadAllBytes(sPath);
        if (rgBytes.Length >= 2 && rgBytes[0] == 0xFF && rgBytes[1] == 0xFE)
            return Encoding.Unicode.GetString(rgBytes);
        if (rgBytes.Length >= 2 && rgBytes[0] == 0xFE && rgBytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(rgBytes);
        if (rgBytes.Length >= 3 && rgBytes[0] == 0xEF && rgBytes[1] == 0xBB && rgBytes[2] == 0xBF)
            return Encoding.UTF8.GetString(rgBytes, 3, rgBytes.Length - 3);

        try { return Encoding.UTF8.GetString(rgBytes); }
        catch
        {
            LogWarn($"UTF-8 decoding failed for {sPath}, falling back to system default encoding.");
            return Encoding.Default.GetString(rgBytes);
        }
    }

    #endregion
    #region CSV解析

    //引号内超过此阈值仍未闭合则强制退出 防止损坏的csv吞掉后续所有行
    private const int iMaxQuotedFieldLength = 100;

    private static readonly char[] s_rgLetters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private static List<string> SplitLines(string sContent, Action<string>? warn = null)
    {
        var rgLines = new List<string>();
        var sb = new StringBuilder();
        bool bInQuotes = false;
        int iQuoteStart = -1;

        for (int i = 0; i < sContent.Length; i++)
        {
            var c = sContent[i];

            if (bInQuotes && iQuoteStart >= 0 && (i - iQuoteStart) > iMaxQuotedFieldLength)
            {
                WarnOrLog(warn, $"Unclosed quote forced closure at position {i}. Discarding broken field, keeping remaining lines.");
                bInQuotes = false;
                iQuoteStart = -1;
                var rgRemaining = sb.ToString().Split('\n');
                for (int j = 1; j < rgRemaining.Length; j++)
                {
                    var sLine = rgRemaining[j];
                    rgLines.Add(sLine.Length > 0 && sLine[^1] == '\r' ? sLine[..^1] : sLine);
                }
                sb.Clear();
                while (i < sContent.Length && sContent[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                sb.Append(c);
                if (bInQuotes && i + 1 < sContent.Length && sContent[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    bInQuotes = !bInQuotes;
                    iQuoteStart = bInQuotes ? i : -1;
                }
            }
            else if (c == '\n' && !bInQuotes)
            {
                var sLine = sb.ToString();
                rgLines.Add(sLine.Length > 0 && sLine[^1] == '\r' ? sLine[..^1] : sLine);
                sb.Clear();
            }
            else if (c == '\r' && !bInQuotes)
            {
                if (i + 1 >= sContent.Length || sContent[i + 1] != '\n')
                {
                    rgLines.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (bInQuotes)
        {
            WarnOrLog(warn, $"Unclosed quote at end of file. Discarding broken field, keeping remaining lines.");
            var rgRemaining = sb.ToString().Split('\n');
            for (int j = 1; j < rgRemaining.Length; j++)
            {
                var sLine = rgRemaining[j];
                rgLines.Add(sLine.Length > 0 && sLine[^1] == '\r' ? sLine[..^1] : sLine);
            }
        }
        else if (sb.Length > 0)
        {
            rgLines.Add(sb.ToString());
        }
        return rgLines;
    }

    private static List<string> SplitRow(string sRow, Action<string>? warn = null)
    {
        var rgFields = new List<string>();
        var sb = new StringBuilder();
        bool bInQuotes = false;
        int iQuoteStart = -1;

        for (int i = 0; i < sRow.Length; i++)
        {
            var c = sRow[i];

            if (bInQuotes && iQuoteStart >= 0 && (i - iQuoteStart) > iMaxQuotedFieldLength)
            {
                WarnOrLog(warn, $"Unclosed quote forced closure in row. Data may be incomplete.");
                bInQuotes = false;
                iQuoteStart = -1;
            }

            if (c == '"')
            {
                if (bInQuotes && i + 1 < sRow.Length && sRow[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    bInQuotes = !bInQuotes;
                    iQuoteStart = bInQuotes ? i : -1;
                }
            }
            else if (c == ',' && !bInQuotes)
            {
                rgFields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        if (bInQuotes)
            WarnOrLog(warn, $"Unclosed quote at end of row. Row may be incomplete.");

        rgFields.Add(sb.ToString());

        //去掉外层引号 先Trim防行首空格
        for (int i = 0; i < rgFields.Count; i++)
        {
            var sF = rgFields[i].Trim();
            if (sF.Length >= 2 && sF[0] == '"' && sF[^1] == '"')
                rgFields[i] = sF[1..^1];
            else
                rgFields[i] = sF;
        }

        return rgFields;
    }

    private static string EscapeCsvField(string? sField)
    {
        if (string.IsNullOrEmpty(sField)) return "";
        return sField.Replace("\"", "\"\"");
    }

    #endregion
    #region 值类型转换

    private static void SetValue<T>(T obj, PropertyInfo piProp, string? sRaw, Action<string>? warn = null)
    {
        try
        {
            if (sRaw == null)
            {
                if (IsNullable(piProp.PropertyType))
                    piProp.SetValue(obj, null);
                return;
            }

            var tTarget = Nullable.GetUnderlyingType(piProp.PropertyType) ?? piProp.PropertyType;

            if (tTarget == typeof(string))
            {
                piProp.SetValue(obj, sRaw);
            }
            else if (tTarget == typeof(int))
            {
                if (int.TryParse(sRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal))
                    piProp.SetValue(obj, iVal);
                else if (int.TryParse(sRaw, NumberStyles.Integer, CultureInfo.CurrentCulture, out int iVal2))
                    piProp.SetValue(obj, iVal2);
                else
                {
                    WarnOrLog(warn, $"Failed to parse int '{piProp.Name}': '{sRaw}'. Set to null.");
                    if (IsNullable(piProp.PropertyType))
                        piProp.SetValue(obj, null);
                }
            }
            else if (tTarget == typeof(double))
            {
                if (double.TryParse(sRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
                    piProp.SetValue(obj, dVal);
                else if (double.TryParse(sRaw, NumberStyles.Float, CultureInfo.CurrentCulture, out double dVal2))
                    piProp.SetValue(obj, dVal2);
                else
                {
                    WarnOrLog(warn, $"Failed to parse double '{piProp.Name}': '{sRaw}'. Set to null.");
                    if (IsNullable(piProp.PropertyType))
                        piProp.SetValue(obj, null);
                }
            }
        }
        catch (Exception ex)
        {
            WarnOrLog(warn, $"SetValue failed for '{piProp.Name}' with '{sRaw}': {ex.Message}");
        }
    }

    private static string? GetValue<T>(T obj, PropertyInfo piProp)
    {
        var oVal = piProp.GetValue(obj);
        if (oVal == null) return "";

        if (oVal is double dVal)
        {
            if (double.IsNaN(dVal) || double.IsInfinity(dVal))
            {
                LogWarn($"NaN/Infinity in double field '{piProp.Name}'. Writing as empty.");
                return "";
            }
            if (dVal == 0) return "0";
            return dVal.ToString("0.####", CultureInfo.InvariantCulture);
        }
        if (oVal is int iVal && iVal == 0) return "0";
        return oVal.ToString();
    }

    private static bool IsNullable(Type tType) =>
        !tType.IsValueType || Nullable.GetUnderlyingType(tType) != null;

    #endregion
    #region 日志桥接

    private static readonly Action<string>? s_actWarn = GetLogMethod("Warn");
    private static readonly Action<string>? s_actError = GetLogMethod("Error");

    private static Action<string>? GetLogMethod(string sMethod)
    {
        try
        {
            var tLogType = Type.GetType("WeaponDamageCalc.LogService, WeaponDamageCalc");
            if (tLogType == null) return null;
            var mi = tLogType.GetMethod(sMethod, new[] { typeof(string) });
            if (mi == null) return null;
            return (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), mi);
        }
        catch { return null; }
    }

    private static void LogWarn(string sMsg)
    {
        s_actWarn?.Invoke("[CsvMapper] " + sMsg);
        System.Diagnostics.Debug.WriteLine($"[CsvMapper WARN] {sMsg}");
    }

    //传入警告收集回调时走回调(LogWarn+入集合) 否则仅LogWarn 用于SplitLines/SplitRow的损坏行警告
    private static void WarnOrLog(Action<string>? warn, string sMsg)
    {
        if (warn != null) warn(sMsg);
        else LogWarn(sMsg);
    }

    private static void LogError(string sMsg)
    {
        s_actError?.Invoke("[CsvMapper] " + sMsg);
        System.Diagnostics.Debug.WriteLine($"[CsvMapper ERROR] {sMsg}");
    }
    #endregion
}

public class ColumnMap
{
    public string HeaderName { get; }
    public PropertyInfo Property { get; }
    public ColumnMap(string sHeaderName, PropertyInfo piProperty)
    {
        HeaderName = sHeaderName;
        Property = piProperty;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class CsvColumnAttribute : Attribute
{
    public string Name { get; }
    public CsvColumnAttribute(string sName) => Name = sName;
}