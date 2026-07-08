using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Advanced → Developer: testing aids. Induce health warnings so the "Needs attention" cards can be
/// reviewed and screenshotted, reveal the normally-hidden Filters/JSON tabs on a tablet's page, and
/// deliberately introduce <em>real</em> misconfigurations for reproducing UX issues. The health/tab
/// state lives in the shared <see cref="DeveloperSettings"/> singleton (the view binds straight to
/// <see cref="Settings"/>); the "break config" commands mutate the live tablet settings through the
/// session, so they persist just like any other settings change.
/// </summary>
public sealed partial class DeveloperViewModel : ObservableObject
{
    // Non-null in the real app (wired from the session in MainViewModel); left null in design/test
    // construction, where the break-config commands simply report that no session is available.
    private readonly ISettingsCoordinator? _settings;
    private readonly IDeviceData? _device;

    public DeveloperViewModel(ISettingsCoordinator? settings = null, IDeviceData? device = null)
    {
        _settings = settings;
        _device = device;
    }

    public DeveloperSettings Settings => DeveloperSettings.Instance;

    /// <summary>Result of the last "create Start-menu shortcut" action (path on success, error otherwise).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutStatus))]
    private string _shortcutStatus = "";

    public bool HasShortcutStatus => !string.IsNullOrEmpty(ShortcutStatus);

    /// <summary>Result of the last "introduce a config error" action (what changed, or why it couldn't).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfigErrorStatus))]
    private string _configErrorStatus = "";

    public bool HasConfigErrorStatus => !string.IsNullOrEmpty(ConfigErrorStatus);

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

    /// <summary>Deliberately push the active tablet's display mapping partly off-screen — a <em>real</em>
    /// settings change (persisted), not a synthetic warning — so the "mapped area is partly off-screen"
    /// UX can be reproduced with an actual bad mapping. The output area is straddled across the desktop's
    /// right edge so roughly half of it lands in dead space beyond every monitor, which reliably trips
    /// <see cref="Domain.DisplayMappingApplier.ClassifyMapping"/>'s off-screen check on any layout. Undo
    /// by remapping the tablet to a display on its page (or the tray's Switch display).</summary>
    [RelayCommand]
    private async Task PushMappingOffScreen()
    {
        var settings = _settings?.CurrentSettings;
        if (settings == null) { ConfigErrorStatus = "No settings loaded yet — connect the daemon first."; return; }

        // The mapping is per-tablet, so act on the active/detected tablet (as the monitor-cycle hotkey does).
        var tabletName = _device?.ActiveTabletName ?? _device?.DetectedTablets.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tabletName)) { ConfigErrorStatus = "No active tablet to remap."; return; }

        var profile = settings.Profiles.FirstOrDefault(p => p.Tablet == tabletName);
        var abs = profile?.AbsoluteModeSettings;
        if (abs == null) { ConfigErrorStatus = $"{tabletName} isn't in an absolute mapping."; return; }

        var displays = DisplayEnumerator.Enumerate();
        if (displays.Count == 0) { ConfigErrorStatus = "No displays detected."; return; }

        // The stored Display area is in 0-based virtual-desktop coords with X/Y as its centre (see
        // DisplayMappingApplier.MappedCenter). Keep the current area size (fall back to the primary
        // monitor if degenerate) and centre it on the desktop's right edge: the right half then falls
        // outside every monitor, so ~50% is uncovered — comfortably past the classifier's 1% tolerance.
        float minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        float rightEdge = displays.Max(d => d.X - minX + d.Width);
        var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];

        abs.Display.Width = abs.Display.Width > 0 ? abs.Display.Width : primary.Width;
        abs.Display.Height = abs.Display.Height > 0 ? abs.Display.Height : primary.Height;
        abs.Display.X = rightEdge;                                  // centre on the right edge → right half off-screen
        abs.Display.Y = primary.Y - minY + primary.Height / 2f;     // vertically centred on the primary (on-screen)

        await _settings!.ApplyAndSaveSettingsAsync(settings);
        ConfigErrorStatus = $"Pushed {tabletName}'s active area partly off-screen (across the desktop's right edge). " +
                            "Home's Needs attention list should now flag it. Remap the tablet to a display to restore it.";
    }
}
