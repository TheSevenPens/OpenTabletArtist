using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Startup page (Advanced → Startup): the "launch with Windows" toggle (#360),
/// moved off Home so the dashboard stays focused on tablet + health status.
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    private bool _suppressRegistryWrite;

    public StartupViewModel()
    {
        RefreshFromRegistry();
    }

    /// <summary>Whether the run-at-startup toggle is available (Windows only) — hides the card elsewhere.</summary>
    public bool StartupSupported => StartupService.IsSupported;

    /// <summary>Launch OpenTabletArtist when Windows starts (per-user Run key). (#360)</summary>
    [ObservableProperty] private bool _startWithWindows;

    /// <summary>Shown when the registry write fails or the stored path is stale (#382).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRegistryMessage))]
    private string _registryMessage = "";

    public bool HasRegistryMessage => !string.IsNullOrEmpty(RegistryMessage);

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressRegistryWrite) return;
        if (StartupService.SetEnabled(value))
        {
            RegistryMessage = "";
            return;
        }

        _suppressRegistryWrite = true;
        StartWithWindows = StartupService.IsEnabled;
        _suppressRegistryWrite = false;
        RegistryMessage = "Couldn't update startup settings. The registry may be locked by policy.";
    }

    private void RefreshFromRegistry()
    {
        _suppressRegistryWrite = true;
        StartWithWindows = StartupService.IsEnabled;
        _suppressRegistryWrite = false;
        RegistryMessage = StartupService.GetState() switch
        {
            StartupRegistryState.StalePath =>
                "Startup is enabled but points at an old install path. Toggle off and on to repair.",
            _ => "",
        };
    }
}
