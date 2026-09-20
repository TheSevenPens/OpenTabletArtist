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

    /// <summary>
    /// Where the user's log lives: under this application's data directory.
    /// </summary>
    /// <remarks>
    /// Through <see cref="AppPaths"/>, so an override redirects the log with everything else (#879).
    /// <see cref="RedirectTo"/> remains for tests that want a directory of their own without touching the
    /// environment; this is the default that a packaged run resolves.
    /// </remarks>
    private static readonly string UserLogDirectory = Path.Combine(AppPaths.LocalAppData, "logs");

    /// <summary>
    /// Where log lines actually go. The user's directory unless something redirected it.
    /// </summary>
    /// <remarks>
    /// Not <c>readonly</c>, and read on every write rather than captured once, so a redirect takes effect
    /// whenever it happens rather than only if it beat the first use of this class (#834). Caching it was
    /// the reason a redirect could silently do nothing: the field initialises on first touch, which for a
    /// test host is easy to lose a race with.
    /// </remarks>
    private static string _logDirectory = UserLogDirectory;

    /// <summary>Full path to the current log file (shown to users who ask "where are the logs?").</summary>
    public static string LogFilePath => Path.Combine(_logDirectory, "app.log");

    /// <summary>
    /// Sends the log somewhere else. For test hosts, which must not write to the user's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tests appended to the real diagnostics file — the one a user is asked for when they report a
    /// problem (#834). Not merely untidy: the log rolls at 1 MB and keeps one previous generation, so a
    /// few suite runs between reproducing a bug and collecting the log can push the actual evidence out
    /// of both. It also wrote <c>[ERROR]</c> lines about failures that never happened to anyone.
    /// </para>
    /// <para>
    /// Internal, so only the test assemblies can call it, and there is no way for the application to
    /// redirect its own log by accident.
    /// </para>
    /// </remarks>
    /// <param name="directory">Where to write instead. Created on first use, like the real one.</param>
    internal static void RedirectTo(string directory) => _logDirectory = directory;

    /// <summary>True while the log is going to the user's own directory rather than somewhere else.</summary>
    /// <remarks>
    /// Exists so a test can assert the redirect actually took. Without that assertion the failure is
    /// invisible: a test host whose redirect never ran writes to the user's log and reports nothing,
    /// which is precisely the state #834 describes.
    /// </remarks>
    internal static bool WritesToTheUsersLog => _logDirectory == UserLogDirectory;

    /// <summary>Raised with each formatted line as it's logged, on the calling thread. Lets a future
    /// in-app log viewer mirror the file, and lets tests observe output without touching disk.</summary>
    public static event Action<string>? LineWritten;

    public static void Debug(string message, Exception? ex = null) => Write(AppLogLevel.Debug, message, ex);
    public static void Info(string message) => Write(AppLogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(AppLogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(AppLogLevel.Error, message, ex);

    /// <summary>Format one log line: "<c>2026-07-31 15:41:58.123 [WARNING] message — ExceptionType: detail</c>".
    /// The exception's <em>outer</em> type is kept for context, but the message comes from
    /// <see cref="Exception.GetBaseException"/> — I/O and RPC failures are usually wrapped, and the useful
    /// detail is on the inner exception. Pure and deterministic (timestamp is passed in) so it's unit-testable.</summary>
    public static string Format(DateTime timestamp, AppLogLevel level, string message, Exception? ex)
    {
        var sb = new StringBuilder()
            .Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(message);
        if (ex != null)
            sb.Append(" — ").Append(ex.GetType().Name).Append(": ").Append(ex.GetBaseException().Message);
        return sb.ToString();
    }

    private static void Write(AppLogLevel level, string message, Exception? ex)
    {
        var line = Format(DateTime.Now, level, message, ex);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(_logDirectory);
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
