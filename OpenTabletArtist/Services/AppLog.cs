using System;
using System.IO;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>Severity of an <see cref="AppLog"/> entry.</summary>
public enum AppLogLevel { Debug, Info, Warning, Error }

/// <summary>
/// A lightweight, best-effort app-side diagnostics log (#21). Writes a rolling text file under the app's
/// data folder so the background paths that used to swallow exceptions silently leave a trace instead —
/// daemon reconnect failures, settings/plugin read problems, etc. Deliberately minimal: a static entry
/// point (matching <see cref="AppSettings"/> / <see cref="DeveloperSettings"/>), thread-safe appends, and
/// size-based rotation. Logging NEVER throws — a failed write must not break the code that was logging.
/// A future Diagnostics/Console page can subscribe to <see cref="LineWritten"/> to show entries live.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1_000_000; // ~1 MB, then roll the current file to app.log.1

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTabletArtist", "logs");

    /// <summary>Full path to the current log file (shown to users who ask "where are the logs?").</summary>
    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "app.log");

    /// <summary>Raised with each formatted line as it's logged, on the calling thread. Lets a future
    /// in-app log viewer mirror the file, and lets tests observe output without touching disk.</summary>
    public static event Action<string>? LineWritten;

    public static void Debug(string message) => Write(AppLogLevel.Debug, message, null);
    public static void Info(string message) => Write(AppLogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(AppLogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(AppLogLevel.Error, message, ex);

    /// <summary>Format one log line: "<c>2026-07-31 15:41:58.123 [WARNING] message — ExceptionType: detail</c>".
    /// Pure and deterministic (timestamp is passed in) so it's unit-testable.</summary>
    public static string Format(DateTime timestamp, AppLogLevel level, string message, Exception? ex)
    {
        var sb = new StringBuilder()
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(message);
        if (ex != null)
            sb.Append(" — ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        return sb.ToString();
    }

    private static void Write(AppLogLevel level, string message, Exception? ex)
    {
        var line = Format(DateTime.Now, level, message, ex);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { /* best-effort: a logger that throws would be worse than a missing line */ }

        try { LineWritten?.Invoke(line); } catch { /* a bad subscriber must not break logging */ }
    }

    // Roll the current file to app.log.1 (one generation) once it passes the size cap, so the log can't
    // grow without bound. Best-effort; called under the lock.
    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogFilePath);
        if (info.Exists && info.Length > MaxBytes)
            File.Move(LogFilePath, LogFilePath + ".1", overwrite: true);
    }
}
