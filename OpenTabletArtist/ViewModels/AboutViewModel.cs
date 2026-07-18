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
    public string UserManualUrl => $"{RepoUrl}/blob/master/docs/USERMANUAL.md";

    /// <summary>The Drawing Tablet community Discord — where users should go for help (#568). Deliberately
    /// not the OpenTabletDriver forums: an issue is only forwarded to OTD once it's confirmed to be an OTD
    /// problem, so the first stop is the OTA help channel.</summary>
    public string HelpDiscordUrl => "https://discord.gg/Rr2MXeM7Ny";

    /// <summary>App version, read from the assembly so it never drifts (the release workflow stamps
    /// the tag version at build). Moved here from the sidebar footer so version info lives on About.</summary>
    public string AppVersion { get; } = AppVersionInfo.Format(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"Version v0.6.0"-style label for the About card.</summary>
    public string AppVersionLabel => $"Version {AppVersion}";

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
