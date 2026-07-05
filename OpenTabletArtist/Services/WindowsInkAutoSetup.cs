using System;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>
/// Makes Windows Ink "just work" so the user never has to learn about it: silently installs the
/// Windows Ink plugin when it's missing, and switches any detected tablet that isn't already on a
/// Windows Ink mode onto Windows Ink Absolute. The mode switch is gated on VMulti being functional
/// (enabling Windows Ink without it breaks the pointer) and the plugin being present (the mode's plugin
/// must exist). Respects per-tablet opt-outs when the user deliberately chose another output mode (#380).
/// Runs only on a connected, app-owned daemon; everything is best-effort so a failure just
/// leaves the existing manual controls (Advanced → Windows Ink Plugin, the tablet's Fix button) in play.
/// </summary>
public sealed class WindowsInkAutoSetup : System.IDisposable
{
    private readonly AppSession _session;
    private readonly SetupActions _setup;
    private readonly WindowsInkPluginService _winInk = new();
    private readonly WindowsInkBundledInstaller _bundled = new();
    private readonly VMultiDetector _vmulti = new();

    private bool _busy;
    private int _installAttempts; // bounded retries per session (#397)

    public WindowsInkAutoSetup(AppSession session)
    {
        _session = session;
        _setup = new SetupActions(session, session);
        _session.DataLoaded += OnDataLoaded;
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Fresh connect → allow another install attempt (#397).
        if (e.PropertyName == nameof(AppSession.IsConnected) && _session.IsConnected)
            _installAttempts = 0;
    }

    private async void OnDataLoaded()
    {
        // Skip while a previous pass is still applying (each apply reloads → re-fires DataLoaded), and
        // never touch a daemon this app didn't start. Only bother once a tablet is actually present.
        if (_busy || !_session.IsConnected || _session.IsForeignDaemon || !_session.HasTablet) return;
        _busy = true;
        try
        {
            await EnsurePluginInstalledAsync();
            await EnsureWindowsInkEnabledAsync();
        }
        catch { /* best-effort — manual controls remain as a fallback */ }
        finally { _busy = false; }
    }

    private async Task EnsurePluginInstalledAsync()
    {
        if (_winInk.ReadInstalled(_session.PluginDirectory) != null) return; // already installed
        if (_installAttempts >= 3) return;                                   // bounded retries (#397)
        _installAttempts++;

        // Online first (newest compatible from the plugin repo), then the copy bundled with the app.
        var latest = await _winInk.GetLatestCompatibleAsync();
        if (latest != null && await _session.Daemon.DownloadPluginAsync(latest))
        {
            await _session.Daemon.LoadPluginsAsync();
            return;
        }
        var outcome = _bundled.EnsureInstalled(_session.PluginDirectory);
        await PluginInstallApplier.ApplyAsync(_session, outcome);
    }

    private async Task EnsureWindowsInkEnabledAsync()
    {
        // Both are prerequisites for Windows Ink to actually deliver pressure/tilt: the plugin provides
        // the output mode, and VMulti is the virtual device it injects through. Without VMulti, switching
        // to Windows Ink would stop the pointer — so leave the tablet alone until VMulti is functional.
        if (_winInk.ReadInstalled(_session.PluginDirectory) == null) return;
        if (!_vmulti.DetectHid().Functional) return;

        var pending = _setup.DetectedTabletsNotOnWindowsInk()
            .Where(t => !WinInkAutoOptOut.IsOptedOut(t))
            .ToList();
        if (pending.Count == 0) return;

        if (_session.CurrentSettings is not { } settings) return;
        int changed = 0;
        foreach (var name in pending)
        {
            var prof = settings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase));
            if (prof == null) continue;
            prof.OutputMode ??= new OpenTabletDriver.Desktop.Reflection.PluginSettingStore(
                "VoiDPlugins.OutputMode.WinInkAbsoluteMode", true);
            prof.OutputMode.Path = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
            changed++;
        }
        if (changed > 0) await _session.ApplyAndSaveSettingsAsync(settings);
    }

    public void Dispose()
    {
        _session.DataLoaded -= OnDataLoaded;
        _session.PropertyChanged -= OnSessionPropertyChanged;
    }
}
