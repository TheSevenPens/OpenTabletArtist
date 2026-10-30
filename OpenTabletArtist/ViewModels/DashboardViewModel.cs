using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Dashboard -- the application's home page. The tablets overview, the presets you can
/// switch between, and the folded-in About content.
/// </summary>
/// <remarks>
/// Health is not here any more (#1050). Its detail moved to the Health page, and Home carries nothing of it
/// beyond what the top-bar chip already says on every page. Home used to give up its whole right-hand column
/// to the findings.
/// </remarks>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    public DashboardViewModel(IDialogService dialogs, TabletsOverviewViewModel tablets, PresetsViewModel presets)
    {
        TabletsOverview = tablets;
        Presets = presets;
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

    /// <summary>The About content (what-is / help / resources / version), shown in Home's left column --
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
