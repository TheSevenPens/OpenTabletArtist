using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Advanced → Developer: testing aids. Induce health warnings so the "Needs attention" cards can be
/// reviewed and screenshotted, and reveal the normally-hidden Filters/JSON tabs on a tablet's page.
/// All state lives in the shared <see cref="DeveloperSettings"/> singleton, so Home and any open tablet
/// page react live; the view binds its controls straight to <see cref="Settings"/>.
/// </summary>
public sealed partial class DeveloperViewModel : ObservableObject
{
    public DeveloperSettings Settings => DeveloperSettings.Instance;

    /// <summary>Result of the last "create Start-menu shortcut" action (path on success, error otherwise).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutStatus))]
    private string _shortcutStatus = "";

    public bool HasShortcutStatus => !string.IsNullOrEmpty(ShortcutStatus);

    /// <summary>Create a per-user Start-menu shortcut to this exe. Registers the app under its display
    /// name so tooling keyed to the installed-app list (e.g. the UI-screenshot automation grant) can find
    /// a dev build run from its output folder. Makes it easy to set up on another machine.</summary>
    [RelayCommand]
    private void CreateStartMenuShortcut()
    {
        ShortcutStatus = StartMenuShortcut.TryCreate(out var path, out var error)
            ? $"Created: {path}"
            : $"Couldn't create the shortcut: {error}";
    }
}
