using System;
using System.IO;

namespace WeaponDamageCalc;

public static class LogService
{
#if DEBUG
    private static bool _enabled = true;
#else
    private static bool _enabled = true;//反转开启release日志
#endif

    private static readonly object _lock = new();
    private static string? _path;
    private const long MaxFileSize = 5 * 1024 * 1024;

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    private static string LogPath =>
        _path ??= System.IO.Path.Combine(AppContext.BaseDirectory, "mangler.log");

    public static void Info(string msg)    => Write("INFO", msg);
    public static void Warn(string msg)    => Write("WARN", msg);
    public static void Error(string msg)   => Write("ERROR", msg);
    public static void Error(Exception ex, string ctx = "")
    {
        string m = string.IsNullOrEmpty(ctx) ? ex.ToString() : $"{ctx}: {ex}";
        Write("ERROR", m);
    }

    private static void Write(string level, string msg)
    {
        if (!_enabled) return;
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
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
}