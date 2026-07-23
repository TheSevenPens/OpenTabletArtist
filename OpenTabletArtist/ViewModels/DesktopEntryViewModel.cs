using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The SETTINGS → SYSTEM card on Linux: create a per-user application-menu entry (<c>.desktop</c>)
/// for this app — the Linux counterpart to <see cref="ShortcutViewModel"/>'s Windows Start-menu shortcut. A
/// dev build run from its build folder isn't a registered app; this registers it under its name so tooling
/// keyed to the installed-app list (e.g. the UI-screenshot automation grant) can find it. Linux-only (see
/// <see cref="DesktopEntry"/>), so the SETTINGS rail only surfaces it there.</summary>
public sealed partial class DesktopEntryViewModel : ObservableObject
{
    /// <summary>Result of the last "create application-menu entry" action (path on success, error otherwise).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntryStatus))]
    private string _entryStatus = "";

    public bool HasEntryStatus => !string.IsNullOrEmpty(EntryStatus);

    [RelayCommand]
    private void CreateDesktopEntry()
    {
        EntryStatus = DesktopEntry.TryCreate(out var path, out var error)
            ? $"Created: {path}"
            : $"Couldn't create the entry: {error}";
    }
}
