using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Dashboard -- the application's home page. Your tablets on the left; on the right, in
/// that order, About, a one-card health summary, the presets you can switch between, and the daemon.
/// </summary>
/// <remarks>
/// Health's detail is not here (#1050): it moved to the Health page, and Home says only how strongly to act, in
/// one card that is the same size in every state and never lists a finding. Home used to give up its whole
/// right-hand column to them.
/// </remarks>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly Action _openHealth;
    private readonly Action _openPresets;

    public DashboardViewModel(IDialogService dialogs, TabletsOverviewViewModel tablets, PresetsViewModel presets,
        HealthBadgeViewModel health, DaemonStatusViewModel daemon, Action openHealth, Action openPresets)
    {
        TabletsOverview = tablets;
        Presets = presets;
        Health = health;
        Daemon = daemon;
        _openHealth = openHealth;
        _openPresets = openPresets;
        // The RESOURCES "Supported tablets" link opens the same in-app catalog the old Home card did,
        // highlighting whichever tablet is currently detected (#155).
        About = new AboutViewModel(dialogs,
            () => TabletsOverview.Tablets.FirstOrDefault(t => t.IsDetected)?.Name);
    }

    /// <summary>The tablets overview (list + supported-tablets link), merged into Home.</summary>
    public TabletsOverviewViewModel TabletsOverview { get; }

    /// <summary>The saved presets, with the one loaded last marked (#1041). Home lists them so changing
    /// preset is one click from the first page, the same list and the same load the Presets page and the
    /// tray use.</summary>
    public PresetsViewModel Presets { get; }

    /// <summary>What Home's health card says: the worst tier and how many findings, in a sentence. The findings
    /// themselves are on the Health page.</summary>
    public HealthBadgeViewModel Health { get; }

    /// <summary>The daemon in one line, for the daemon card. The full status and controls are on the Daemon page.</summary>
    public DaemonStatusViewModel Daemon { get; }

    /// <summary>Go to the Health page: the health card's button, and the link in the section's heading.</summary>
    [RelayCommand]
    private void OpenHealth() => _openHealth();

    /// <summary>Go to the Presets page: the link in the presets heading. Presets has no card of its own to
    /// carry a button, and the row it shows loads a preset rather than opening anything.</summary>
    [RelayCommand]
    private void OpenPresets() => _openPresets();

    /// <summary>The About content (what-is / help / resources / version), first in Home's right column --
    /// the standalone About page was folded into Home. Home owns its own instance (constructed with the
    /// dialog service so its RESOURCES "Supported tablets" link can open the in-app catalog).</summary>
    public AboutViewModel About { get; }

    /// <summary>Load a preset by name: the Presets page's own command, so the question about unsaved edits and
    /// the "last loaded" mark behave exactly as they do from there, the tray and the hotkeys. Named here
    /// because the view binds it through this page's data context (and a test checks that such names exist).</summary>
    [RelayCommand]
    private Task LoadPreset(string name) => Presets.LoadPresetCommand.ExecuteAsync(name);

    public void Dispose() { }
}
