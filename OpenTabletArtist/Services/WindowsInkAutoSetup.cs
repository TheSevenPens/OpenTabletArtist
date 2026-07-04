using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>
/// Makes Windows Ink "just work" so the user never has to learn about it: silently installs the
/// Windows Ink plugin when it's missing, and switches any detected tablet that isn't already on a
/// Windows Ink mode onto Windows Ink Absolute. The mode switch is gated on VMulti being functional
/// (enabling Windows Ink without it breaks the pointer) and the plugin being present (the mode's plugin
/// must exist). Runs only on a connected, app-owned daemon; everything is best-effort so a failure just
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
    private bool _installAttempted; // don't re-attempt a failed install on every 3s poll

    public WindowsInkAutoSetup(AppSession session)
    {
        _session = session;
        _setup = new SetupActions(session, session);
        _session.DataLoaded += OnDataLoaded;
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
        if (_installAttempted) return;                                       // tried once this session
        _installAttempted = true;

        // Online first (newest compatible from the plugin repo), then the copy bundled with the app.
        var latest = await _winInk.GetLatestCompatibleAsync();
        if (latest != null && await _session.Daemon.DownloadPluginAsync(latest))
        {
            await _session.Daemon.LoadPluginsAsync();
            return;
        }
        if (_bundled.EnsureInstalled(_session.PluginDirectory) != PluginInstallOutcome.None)
            await _session.Daemon.LoadPluginsAsync();
    }

    private async Task EnsureWindowsInkEnabledAsync()
    {
        // Both are prerequisites for Windows Ink to actually deliver pressure/tilt: the plugin provides
        // the output mode, and VMulti is the virtual device it injects through. Without VMulti, switching
        // to Windows Ink would stop the pointer — so leave the tablet alone until VMulti is functional.
        if (_winInk.ReadInstalled(_session.PluginDirectory) == null) return;
        if (!_vmulti.DetectHid().Functional) return;
        if (_setup.DetectedTabletsNotOnWindowsInk().Count == 0) return;
        await _setup.SetDetectedTabletsToWindowsInkAsync();
    }

    public void Dispose() => _session.DataLoaded -= OnDataLoaded;
}
