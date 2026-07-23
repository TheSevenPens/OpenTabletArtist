using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Domain.Health;

namespace OpenTabletArtist.Services;

/// <summary>
/// App-wide developer/testing toggles (Advanced → Developer). A process-wide singleton so the page,
/// the health service, and any open tablet page all observe the same state. The tab-visibility toggles
/// persist (they're genuine preferences); the induced-warning flags are session-only, so a synthetic
/// warning can never survive a restart and be mistaken for a real one.
/// </summary>
public sealed partial class DeveloperSettings : ObservableObject
{
    public static DeveloperSettings Instance { get; } = new();

    private const string FiltersKey = "developer.showFiltersTab";
    private const string JsonKey = "developer.showJsonTab";
    private const string OnPageScreenshotKey = "developer.onPageScreenshot";
    private const string CutBelowKey = "developer.showCutBelowMinimum";
    private const string CardRoleTagsKey = "developer.showCardRoleTags";
    private const string ScreenshotFormatKey = "developer.screenshotFormat";

    private readonly bool _loading;

    private DeveloperSettings()
    {
        _loading = true;
        ShowFiltersTab = AppSettings.Get(FiltersKey) == "true";
        ShowJsonTab = AppSettings.Get(JsonKey) == "true";
        OnPageScreenshot = AppSettings.Get(OnPageScreenshotKey) == "true";
        ShowCutBelowMinimum = AppSettings.Get(CutBelowKey) == "true";
        ShowCardRoleTags = AppSettings.Get(CardRoleTagsKey) == "true";
        ScreenshotFormat = Enum.TryParse<ScreenshotFormat>(AppSettings.Get(ScreenshotFormatKey), out var fmt)
            ? fmt : ScreenshotFormat.PNG;
        _loading = false;
    }

    /// <summary>Show the Filters tab on a tablet's page. Hidden by default — users never need it.</summary>
    [ObservableProperty] private bool _showFiltersTab;
    /// <summary>Show the JSON tab on a tablet's page. Hidden by default — users never need it.</summary>
    [ObservableProperty] private bool _showJsonTab;
    /// <summary>Show a small capture button at the bottom of the nav bar that screenshots the current
    /// page (#437). Off by default — a developer aid.</summary>
    [ObservableProperty] private bool _onPageScreenshot;
    /// <summary>Overlay a small role tag (SECTION / ALERT / SELECT) at each card's bottom-right so the
    /// card-role split (#574) is visible while iterating on the cardless redesign (#573). Off by default.</summary>
    [ObservableProperty] private bool _showCardRoleTags;
    /// <summary>Show the "Cut below input minimum" dead-zone checkbox in Pressure Dynamics (#569). Hidden
    /// by default — it's an advanced/rarely-needed option; re-enabled from the Developer tab.</summary>
    [ObservableProperty] private bool _showCutBelowMinimum;
    /// <summary>Image format the screenshot aids write (#437). PNG by default; JPG or both are options for
    /// smaller files / attachments.</summary>
    [ObservableProperty] private ScreenshotFormat _screenshotFormat = ScreenshotFormat.PNG;

    /// <summary>The format choices for the screenshot dropdown.</summary>
    public ScreenshotFormat[] ScreenshotFormatOptions { get; } =
        { ScreenshotFormat.PNG, ScreenshotFormat.JPG, ScreenshotFormat.Both };

    // Induced health warnings, one per severity, for reviewing/screenshotting the "Needs attention" UI.
    // Session-only (not persisted): fixing one just clears its flag (see ClearInduced).
    [ObservableProperty] private bool _induceRecommendation;
    [ObservableProperty] private bool _induceMisconfigured;
    [ObservableProperty] private bool _induceBroken;

    // Force each ACTUAL health-check warning to appear (with its real title/body/Fix button) so the true
    // copy can be reviewed and screenshotted. These override the health inputs, so the real evaluator
    // produces the real issue — no separate preview text to drift. Session-only; the Fix button runs the
    // real remediation, so to clear the card for the next shot just turn the toggle back off here.
    [ObservableProperty] private bool _forceWinInkNotInstalled;
    [ObservableProperty] private bool _forceWinInkVersionMismatch;
    [ObservableProperty] private bool _forceVMultiNotInstalled;
    [ObservableProperty] private bool _forceDriverConflict;
    [ObservableProperty] private bool _forceRunningElevated;
    [ObservableProperty] private bool _forceTrayHostUnavailable;
    [ObservableProperty] private bool _forceForeignDaemon;
    [ObservableProperty] private bool _forceTabletNotWinInk;
    [ObservableProperty] private bool _forceTabletMappingOffScreen;
    [ObservableProperty] private bool _forceTabletMappingCustom;
    [ObservableProperty] private bool _forceTabletConfigOverride;
    [ObservableProperty] private bool _forceArtistPenBehavior;

    /// <summary>True for any developer flag that changes the health catalog (everything except the
    /// tab-visibility toggles), so the health service knows to re-evaluate.</summary>
    public static bool AffectsHealth(string? propertyName) =>
        propertyName is not (nameof(ShowFiltersTab) or nameof(ShowJsonTab) or nameof(OnPageScreenshot)
                             or nameof(ShowCutBelowMinimum) or nameof(ShowCardRoleTags)
                             or nameof(ScreenshotFormat));

    /// <summary>Any induce/force flag is on, so the health list currently contains a synthetic issue.
    /// Lets the health service skip the extra "what's real" pass in the normal (no-override) case.</summary>
    public bool HasActiveHealthOverride =>
        InduceRecommendation || InduceMisconfigured || InduceBroken
        || ForceWinInkNotInstalled || ForceWinInkVersionMismatch || ForceVMultiNotInstalled
        || ForceDriverConflict || ForceRunningElevated || ForceTrayHostUnavailable || ForceForeignDaemon
        || ForceTabletNotWinInk || ForceTabletMappingOffScreen || ForceTabletMappingCustom
        || ForceTabletConfigOverride || ForceArtistPenBehavior;

    partial void OnShowFiltersTabChanged(bool value)
    {
        if (!_loading) AppSettings.Set(FiltersKey, value ? "true" : "false");
    }

    partial void OnShowJsonTabChanged(bool value)
    {
        if (!_loading) AppSettings.Set(JsonKey, value ? "true" : "false");
    }

    partial void OnOnPageScreenshotChanged(bool value)
    {
        if (!_loading) AppSettings.Set(OnPageScreenshotKey, value ? "true" : "false");
    }

    partial void OnShowCutBelowMinimumChanged(bool value)
    {
        if (!_loading) AppSettings.Set(CutBelowKey, value ? "true" : "false");
    }

    partial void OnShowCardRoleTagsChanged(bool value)
    {
        if (!_loading) AppSettings.Set(CardRoleTagsKey, value ? "true" : "false");
    }

    partial void OnScreenshotFormatChanged(ScreenshotFormat value)
    {
        if (!_loading) AppSettings.Set(ScreenshotFormatKey, value.ToString());
    }

    /// <summary>Turn off the induced-warning flag of the given severity — the synthetic issue's Fix
    /// action, so "fixing" it simply removes the flag that caused it to show.</summary>
    public void ClearInduced(HealthSeverity severity)
    {
        switch (severity)
        {
            case HealthSeverity.Broken: InduceBroken = false; break;
            case HealthSeverity.Misconfigured: InduceMisconfigured = false; break;
            case HealthSeverity.Recommendation: InduceRecommendation = false; break;
        }
    }

    /// <summary>Clear whichever developer flag is producing <paramref name="issue"/> — the hidden
    /// right-click "dismiss" on Home. Safe to call for any issue; only developer-induced ids match.</summary>
    public void Dismiss(HealthIssue issue)
    {
        switch (issue.Id)
        {
            case "dev.induced.Broken": InduceBroken = false; break;
            case "dev.induced.Misconfigured": InduceMisconfigured = false; break;
            case "dev.induced.Recommendation": InduceRecommendation = false; break;
            case "winink.notInstalled": ForceWinInkNotInstalled = false; break;
            case "winink.versionMismatch": ForceWinInkVersionMismatch = false; break;
            case "vmulti.notInstalled": ForceVMultiNotInstalled = false; break;
            case "driver.conflict": ForceDriverConflict = false; break;
            case "app.elevated": ForceRunningElevated = false; break;
            case "tray.gnomeNoSni": ForceTrayHostUnavailable = false; break;
            case "daemon.foreign": ForceForeignDaemon = false; break;
            default:
                if (issue.Id.StartsWith("tablet.notWinInk:", System.StringComparison.Ordinal))
                    ForceTabletNotWinInk = false;
                else if (issue.Id.StartsWith("tablet.mappingOffScreen:", System.StringComparison.Ordinal))
                    ForceTabletMappingOffScreen = false;
                else if (issue.Id.StartsWith("tablet.mappingCustom:", System.StringComparison.Ordinal))
                    ForceTabletMappingCustom = false;
                else if (issue.Id.StartsWith("tablet.configOverride:", System.StringComparison.Ordinal))
                    ForceTabletConfigOverride = false;
                else if (issue.Id.StartsWith("tablet.penBehavior:", System.StringComparison.Ordinal))
                    ForceArtistPenBehavior = false;
                break;
        }
    }
}
