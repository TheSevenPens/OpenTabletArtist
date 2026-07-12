using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The SETTINGS → SHORTCUT tab: create a per-user Start-menu shortcut to this app. A dev build run
/// from its build folder isn't a registered app; this registers it under its name so tooling keyed to the
/// installed-app list (e.g. the UI-screenshot automation grant) can find it. Windows-only (see
/// <see cref="StartMenuShortcut"/>), so the SETTINGS rail hides this tab off-Windows.</summary>
public sealed partial class ShortcutViewModel : ObservableObject
{
    /// <summary>Result of the last "create Start-menu shortcut" action (path on success, error otherwise).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutStatus))]
    private string _shortcutStatus = "";

    public bool HasShortcutStatus => !string.IsNullOrEmpty(ShortcutStatus);

    [RelayCommand]
    private void CreateStartMenuShortcut()
    {
        ShortcutStatus = StartMenuShortcut.TryCreate(out var path, out var error)
            ? $"Created: {path}"
            : $"Couldn't create the shortcut: {error}";
    }
}
