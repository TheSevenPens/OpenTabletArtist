using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop.Profiles;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.Helpers;
using OtdWindowsHelper.Services;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// Application shell: owns navigation and the composed page view models, plus the shared
/// <see cref="AppSession"/> (daemon connection + settings + data). After the Option C split
/// (#41) this holds no feature state of its own — each page has its own VM, and the Dashboard
/// (the session's status home) is just another composed VM.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsFileStore _settingsStore = new SettingsFileStore();
    private readonly AppSession _session;

    public AboutViewModel About { get; } = new();
    public UtilitiesViewModel Utilities { get; } = new();
    public CustomTabletConfigsViewModel Configs { get; } = new();
    public PresetsViewModel Presets { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public TabletSettingsViewModel TabletSettings { get; }
    public DashboardViewModel Dashboard { get; }

    [ObservableProperty] private string _currentPage = "Dashboard";

    public MainViewModel()
    {
        _session = new AppSession(new DaemonClient(), new DaemonLifecycleService(), _settingsStore);

        // Page VMs depend on the session (role interfaces) or shared shell logic.
        Presets = new PresetsViewModel(_settingsStore, _session);
        Diagnostics = new DiagnosticsViewModel(_session.Daemon);
        TabletSettings = new TabletSettingsViewModel(_session, OpenTabletSettingsForProfile);
        Dashboard = new DashboardViewModel(_session, OpenTabletSettingsForProfile);

        // Keep the Diagnostics connection gate in sync (Diagnostics still takes a pushed
        // IsConnected; re-plumbing it onto IConnectionState is a later cleanup).
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IConnectionState.IsConnected))
                Diagnostics.IsConnected = _session.IsConnected;
        };

        // The session owns the data load; the shell pushes the loaded data into the pages that
        // don't yet self-subscribe (a small remaining IDeviceData re-plumb, deferred).
        _session.DataLoaded += OnSessionDataLoaded;

        _ = InitAsync();
    }

    private void OnSessionDataLoaded()
    {
        TabletSettings.Profiles = _session.Profiles;
        Presets.PresetDirectory = _session.PresetDirectory;
        _ = LoadPresetsSafelyAsync();
    }

    // Fire-and-forget preset refresh; keep the old swallow semantics so an enumeration/ordering
    // failure can't surface as an unobserved exception. (Codex #43.)
    private async Task LoadPresetsSafelyAsync()
    {
        try { await Presets.LoadAsync(); }
        catch { /* preset refresh failure must not surface */ }
    }

    private async Task InitAsync()
    {
        // Auto-start the daemon if needed and begin connecting (session owns connect + polling).
        await _session.StartAndConnectAsync();
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    partial void OnCurrentPageChanged(string? oldValue, string newValue)
    {
        if (oldValue == "Diagnostics" && newValue != "Diagnostics")
            _ = Diagnostics.StopDebuggingAsync();
    }

    // Opens the per-tablet settings dialog. Shared by the Tablet Settings page and the
    // Dashboard's "Open" (both receive this as a delegate). Stays in the shell because it is
    // UI orchestration (constructs a Window); it moves onto a dialog service with #37.
    private async Task OpenTabletSettingsForProfile(Profile profile)
    {
        var tabletName = profile.Tablet;
        var digitizer = _session.GetTabletDigitizer(tabletName);
        var dialog = new Views.TabletSettingsDialog(
            profile,
            _session.CurrentSettings,
            async updatedSettings => await _session.ApplyAndSaveSettingsAsync(updatedSettings),
            async () =>
            {
                // Authoritative refresh through the session so its CurrentSettings cache stays
                // coherent (and the rest of the UI updates too). (Codex #43.)
                await _session.ReloadAsync();
                return _session.CurrentSettings?.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
            },
            digitizer);

        var mainWindow = Dialogs.GetMainWindow();
        if (mainWindow != null)
            await dialog.ShowDialog(mainWindow);
    }

    public void Dispose()
    {
        Diagnostics.Dispose(); // stops debugging + disables the daemon debug stream if active
        Dashboard.Dispose();   // cancels VMulti install/uninstall token
        _session.Dispose();    // cancels the connect/poll loops, disposes the daemon client + load gate
        Utilities.Dispose();
    }
}

public record ConfigurationItem(string Name, string FileName, string Path, string SizeText);

/// <summary>
/// View-model record for a settings snapshot file shown in the Saved Settings list.
/// Plain-property record so Avalonia bindings can resolve Name/LastModified directly
/// (JObject indexer bindings stopped rendering for TextBlock.Text in Avalonia 12).
/// </summary>
public record PresetInfo(string Name, string Path, string Content, string LastModified);
