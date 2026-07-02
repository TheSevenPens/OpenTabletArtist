using System;
using System.Diagnostics;
using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// TIMING SPIKE for per-app profile switching (#167) — not the shipped feature. When enabled, it toggles
/// between two chosen snapshots on every foreground-app change and records how long the live switch takes,
/// so we can measure switch latency and feel it mid-stroke before committing to the full PerAppSwitcher.
/// Reuses the proven <see cref="ProfileSwitchService"/> apply path from #320. Results are logged to
/// %LOCALAPPDATA%\OpenTabletArtist\perapp-spike.log and surfaced live via <see cref="Measured"/>.
/// </summary>
public sealed class PerAppSpikeService : IDisposable
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenTabletArtist", "perapp-spike.log");

    private readonly IForegroundAppWatcher _watcher;
    private readonly ProfileSwitchService _switch;
    private readonly string _ownExeName;

    private string _snapshotA = "";
    private string _snapshotB = "";
    private bool _enabled;
    private int _toggle;
    private string? _lastExe;

    /// <summary>Fired (UI thread) after each measured switch with a status line for the page.</summary>
    public event Action<string>? Measured;

    public PerAppSpikeService(IForegroundAppWatcher watcher, ProfileSwitchService profileSwitch)
    {
        _watcher = watcher;
        _switch = profileSwitch;
        _ownExeName = Process.GetCurrentProcess().ProcessName + ".exe";
        _watcher.Changed += OnForegroundChanged;
    }

    /// <summary>The two snapshots the spike alternates between.</summary>
    public void Configure(string snapshotA, string snapshotB)
    {
        _snapshotA = snapshotA;
        _snapshotB = snapshotB;
    }

    public void SetEnabled(bool on)
    {
        if (on == _enabled) return;
        _enabled = on;
        if (on)
        {
            _lastExe = null;
            _watcher.Start();
            Log($"--- spike enabled (A='{_snapshotA}', B='{_snapshotB}') ---");
        }
        else
        {
            _watcher.Stop();
            _ = _switch.RestoreDefaultAsync();
            Log("--- spike disabled, default restored ---");
        }
    }

    private async void OnForegroundChanged(Domain.AppIdentity app)
    {
        if (!_enabled) return;
        // Ignore our own window (editing/testing shouldn't thrash) and repeats of the same app.
        if (string.Equals(app.ExeName, _ownExeName, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(app.ExeName, _lastExe, StringComparison.OrdinalIgnoreCase)) return;
        _lastExe = app.ExeName;

        if (string.IsNullOrEmpty(_snapshotA) || string.IsNullOrEmpty(_snapshotB)) return;

        var target = (_toggle++ % 2 == 0) ? _snapshotA : _snapshotB;
        var sw = Stopwatch.StartNew();
        bool ok = await _switch.SwitchToAsync(target);
        sw.Stop();

        var msg = $"{app.ExeName} → {target} : {sw.ElapsedMilliseconds} ms{(ok ? "" : "  (snapshot load failed)")}";
        Log(msg);
        Measured?.Invoke(msg);
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* diagnostics must never throw */ }
    }

    public void Dispose()
    {
        _watcher.Changed -= OnForegroundChanged;
        _watcher.Dispose();
    }
}
