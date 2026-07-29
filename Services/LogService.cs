using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace WeaponDamageCalc;

public static class LogService
{
    public enum Level { Debug, Info, Warn, Error, Fatal }

#if DEBUG
    private static bool _enabled = true;
#else
    private static bool _enabled = false;//开关release日志
#endif

    private static Level _minLevel = Level.Debug;
    private static readonly object _lock = new();
    private static string? _path;
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly Dictionary<string, DateTime> _debounce = new();
    private static bool _fileOutputEnabled = true;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public static Level MinLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    public static bool FileOutputEnabled
    {
        get => _fileOutputEnabled;
        set => _fileOutputEnabled = value;
    }

    private static string LogPath =>
        _path ??= System.IO.Path.Combine(AppContext.BaseDirectory, "mangler.log");

    public static void Debug(string msg) => Write("DEBUG", Level.Debug, msg, null);
    public static void Info(string msg)  => Write("INFO", Level.Info, msg, null);
    public static void Warn(string msg)  => Write("WARN", Level.Warn, msg, new StackTrace(1, true));
    public static void Error(string msg) => Write("ERROR", Level.Error, msg, new StackTrace(1, true));
    public static void Error(Exception ex, string ctx = "")
    {
        string m = string.IsNullOrEmpty(ctx) ? ex.ToString() : $"{ctx}: {ex}";
        Write("ERROR", Level.Error, m, new StackTrace(1, true));
    }
    public static void Fatal(string msg) => Write("FATAL", Level.Fatal, msg, new StackTrace(1, true));
    public static void Fatal(Exception ex, string ctx = "")
    {
        string m = string.IsNullOrEmpty(ctx) ? ex.ToString() : $"{ctx}: {ex}";
        Write("FATAL", Level.Fatal, m, new StackTrace(1, true));
    }

    public static void DebugDebounce(string key, string msg, int cooldownMs = 1000)
    {
        lock (_debounce)
        {
            if (_debounce.TryGetValue(key, out var last) &&
                (DateTime.Now - last).TotalMilliseconds < cooldownMs)
                return;
            _debounce[key] = DateTime.Now;
        }
        Write("DEBUG", Level.Debug, msg, null);
    }

    private static void Write(string levelName, Level lvl, string msg, StackTrace? stackTrace)
    {
        if (!_enabled) return;

        //Debug.Write始终输出 不受MinLevel限制
        string line = FormatLine(levelName, msg, stackTrace);
        System.Diagnostics.Debug.Write(line);

        //文件写入受MinLevel控制
        if (!_fileOutputEnabled || lvl < _minLevel) return;

        try
        {
            lock (_lock)
            {
                try
                {
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > MaxFileSize)
                        File.WriteAllText(LogPath, line);
                    else
                        File.AppendAllText(LogPath, line);
                }
                catch
                {
                    File.AppendAllText(LogPath, line);
                }
            }
        }
        catch { }
    }

    private static string FormatLine(string levelName, string msg, StackTrace? stackTrace)
    {
        string baseLine = $"[{DateTime.Now:HH:mm:ss.fff}] [{levelName}] {msg}";
        if (stackTrace != null)
        {
            var frame = stackTrace.GetFrame(0);
            if (frame != null)
            {
                var method = frame.GetMethod();
                string? file = frame.GetFileName();
                int lineNum = frame.GetFileLineNumber();
                if (method != null)
                {
                    string location = file != null
                        ? $"  @ {method.DeclaringType?.Name}.{method.Name} ({Path.GetFileName(file)}:{lineNum})"
                        : $"  @ {method.DeclaringType?.Name}.{method.Name}";
                    baseLine += Environment.NewLine + location;
                }
            }
        }
        return baseLine + Environment.NewLine;
    }
}