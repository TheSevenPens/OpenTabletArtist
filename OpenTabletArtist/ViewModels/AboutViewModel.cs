using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the About page. First step of the page-view-model split (#14 phase 2):
/// the About page owns no shared state, so it moves cleanly out of <see cref="MainViewModel"/>.
/// The view is re-pointed to this VM via a DataContext binding, so navigation is unchanged.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    private readonly IDialogService? _dialogs;
    private readonly Func<string?>? _detectedTabletName;

    /// <param name="dialogs">Used by the RESOURCES "Supported tablets" link to open the in-app catalog.
    /// Optional so the parameterless case (e.g. tests) keeps working.</param>
    /// <param name="detectedTabletName">Supplies the connected tablet to highlight in that dialog.</param>
    public AboutViewModel(IDialogService? dialogs = null, Func<string?>? detectedTabletName = null)
    {
        _dialogs = dialogs;
        _detectedTabletName = detectedTabletName;
    }

    /// <summary>This project's GitHub repository.</summary>
    public string RepoUrl => "https://github.com/TheSevenPens/OpenTabletArtist";

    /// <summary>GitHub releases page (downloads + release notes).</summary>
    public string ReleasesUrl => $"{RepoUrl}/releases";

    /// <summary>The user manual, rendered on GitHub.</summary>
    public string UserManualUrl => $"{RepoUrl}/blob/master/docs/user/USERMANUAL.md";

    /// <summary>The manual's "Getting help" page — where to ask, and what to include when you do. Replaces
    /// the Help card that used to sit on Home (#568): the guidance outgrew a paragraph, and a page can say
    /// what to include in a post without turning the column into an essay. The Discord link lives there
    /// now, along with the reason it is the first stop rather than the OpenTabletDriver forums.</summary>
    public string HelpUrl => $"{RepoUrl}/blob/master/docs/user/HELP.md";

    /// <summary>App version, read from the assembly so it never drifts (the release workflow stamps
    /// the tag version at build). Moved here from the sidebar footer so version info lives on About.</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"(v0.6.0 BETA)"-style suffix, shown beside the ABOUT heading rather than on a line of its
    /// own. The version is reference matter — worth having to hand when reporting a problem, not worth a
    /// row in the column — so it rides along with the heading it belongs to.</summary>
    public string AppVersionParenthetical => $"({AppVersion} BETA)";

    /// <summary>Opens a URL in the user's default browser.</summary>
    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Show OTD's built-in supported-tablets catalog in an in-app dialog (#155), highlighting the
    /// connected tablet. The RESOURCES-card entry point — replaces the former standalone Home card.</summary>
    [RelayCommand]
    private async Task OpenSupportedTablets()
    {
        if (_dialogs == null) return;
        await _dialogs.ShowSupportedTabletsAsync(_detectedTabletName?.Invoke());
    }
}
