using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace WeaponDamageCalc;

public static class LogService
{
    public enum Level { Debug, Info, Warn, Error, Fatal }

#if DEBUG
    private static bool bEnabled = true;
#else
    private static bool bEnabled = false;//开关release日志
#endif

    private static Level lvlMin = Level.Debug;
    private static readonly object oLock = new();
    private static string? sPath;
    private const long cbMaxFile = 5 * 1024 * 1024;
    private static readonly Dictionary<string, DateTime> mpDebounce = new();
    private static bool bFileOutputEnabled = true;

    public static bool Enabled
    {
        get => bEnabled;
        set => bEnabled = value;
    }

    public static Level MinLevel
    {
        get => lvlMin;
        set => lvlMin = value;
    }

    public static bool FileOutputEnabled
    {
        get => bFileOutputEnabled;
        set => bFileOutputEnabled = value;
    }

    private static string sLogPath =>
        sPath ??= Path.Combine(AppContext.BaseDirectory, "mangler.log");

    public static void Debug(string sMsg) => Write("DEBUG", Level.Debug, sMsg, null);
    public static void Info(string sMsg)  => Write("INFO", Level.Info, sMsg, null);
    public static void Warn(string sMsg)  => Write("WARN", Level.Warn, sMsg, new StackTrace(1, true));
    public static void Error(string sMsg) => Write("ERROR", Level.Error, sMsg, new StackTrace(1, true));
    public static void Error(Exception ex, string sCtx = "")
    {
        string sMsg = string.IsNullOrEmpty(sCtx) ? ex.ToString() : $"{sCtx}: {ex}";
        Write("ERROR", Level.Error, sMsg, new StackTrace(1, true));
    }
    public static void Fatal(string sMsg) => Write("FATAL", Level.Fatal, sMsg, new StackTrace(1, true));
    public static void Fatal(Exception ex, string sCtx = "")
    {
        string sMsg = string.IsNullOrEmpty(sCtx) ? ex.ToString() : $"{sCtx}: {ex}";
        Write("FATAL", Level.Fatal, sMsg, new StackTrace(1, true));
    }

    public static void DebugDebounce(string sKey, string sMsg, int iCooldownMs = 1000)
    {
        lock (mpDebounce)
        {
            if (mpDebounce.TryGetValue(sKey, out var dtLast) &&
                (DateTime.Now - dtLast).TotalMilliseconds < iCooldownMs)
                return;
            mpDebounce[sKey] = DateTime.Now;
        }
        Write("DEBUG", Level.Debug, sMsg, null);
    }

    private static void Write(string sLevelName, Level lvl, string sMsg, StackTrace? stTrace)
    {
        if (!bEnabled) return;

        //Debug.Write始终输出 不受MinLevel限制
        string sLine = FormatLine(sLevelName, sMsg, stTrace);
        System.Diagnostics.Debug.Write(sLine);

        //文件写入受MinLevel控制
        if (!bFileOutputEnabled || lvl < lvlMin) return;

        try
        {
            lock (oLock)
            {
                try
                {
                    var fi = new FileInfo(sLogPath);
                    if (fi.Exists && fi.Length > cbMaxFile)
                        File.WriteAllText(sLogPath, sLine);
                    else
                        File.AppendAllText(sLogPath, sLine);
                }
                catch
                {
                    File.AppendAllText(sLogPath, sLine);
                }
            }
        }
        catch { }
    }

    private static string FormatLine(string sLevelName, string sMsg, StackTrace? stTrace)
    {
        string sLine = $"[{DateTime.Now:HH:mm:ss.fff}] [{sLevelName}] {sMsg}";
        if (stTrace != null)
        {
            var stFrame = stTrace.GetFrame(0);
            if (stFrame != null)
            {
                var miMethod = stFrame.GetMethod();
                string? sFile = stFrame.GetFileName();
                int iLineNum = stFrame.GetFileLineNumber();
                if (miMethod != null)
                {
                    string sLocation = sFile != null
                        ? $"  @ {miMethod.DeclaringType?.Name}.{miMethod.Name} ({Path.GetFileName(sFile)}:{iLineNum})"
                        : $"  @ {miMethod.DeclaringType?.Name}.{miMethod.Name}";
                    sLine += Environment.NewLine + sLocation;
                }
            }
        }
        return sLine + Environment.NewLine;
    }
}