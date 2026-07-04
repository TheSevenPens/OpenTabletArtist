using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Startup page (Advanced → Startup): the "launch with Windows" toggle (#360),
/// moved off Home so the dashboard stays focused on tablet + health status.
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    public StartupViewModel()
    {
        // Assign the backing field directly so reading the current state here doesn't fire the change
        // handler (which would rewrite the registry) at construction.
        _startWithWindows = StartupService.IsEnabled;
    }

    /// <summary>Whether the run-at-startup toggle is available (Windows only) — hides the card elsewhere.</summary>
    public bool StartupSupported => StartupService.IsSupported;

    /// <summary>Launch OpenTabletArtist when Windows starts (per-user Run key). (#360)</summary>
    [ObservableProperty] private bool _startWithWindows;

    partial void OnStartWithWindowsChanged(bool value) => StartupService.SetEnabled(value);
}
