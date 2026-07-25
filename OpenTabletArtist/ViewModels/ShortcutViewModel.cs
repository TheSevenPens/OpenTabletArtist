using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The SETTINGS → SHORTCUT tab: a checkbox that mirrors whether a per-user Start-menu shortcut to
/// this app exists — checking it creates the shortcut, unchecking removes it. A dev build run from its build
/// folder isn't a registered app; the shortcut registers it under its name so tooling keyed to the
/// installed-app list (e.g. the UI-screenshot automation grant) can find it. Windows-only (see
/// <see cref="StartMenuShortcut"/>), so the SETTINGS rail hides this tab off-Windows.</summary>
public sealed partial class ShortcutViewModel : ObservableObject
{
    // Guards against re-entrancy when we revert the checkbox after a failed create/remove.
    private bool _suppressToggle;

    /// <summary>Whether the Start-menu shortcut currently exists. Bound two-way to the checkbox; toggling it
    /// creates or removes the shortcut (see <see cref="OnShortcutExistsChanged"/>).</summary>
    [ObservableProperty]
    private bool _shortcutExists;

    /// <summary>Result of the last create/remove action (path on create, confirmation on remove, or error).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutStatus))]
    private string _shortcutStatus = "";

    public bool HasShortcutStatus => !string.IsNullOrEmpty(ShortcutStatus);

    public ShortcutViewModel()
    {
        // Seed the checkbox from the real state without triggering a create/remove.
        _shortcutExists = StartMenuShortcut.Exists;
    }

    partial void OnShortcutExistsChanged(bool value)
    {
        if (_suppressToggle) return;

        bool ok;
        string? error;
        if (value)
        {
            ok = StartMenuShortcut.TryCreate(out var path, out error);
            ShortcutStatus = ok ? $"Created: {path}" : $"Couldn't create the shortcut: {error}";
        }
        else
        {
            ok = StartMenuShortcut.TryDelete(out error);
            ShortcutStatus = ok ? "Shortcut removed." : $"Couldn't remove the shortcut: {error}";
        }

        // On failure, snap the checkbox back to the shortcut's real state.
        if (!ok)
        {
            _suppressToggle = true;
            ShortcutExists = StartMenuShortcut.Exists;
            _suppressToggle = false;
        }
    }
}
