using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the About page. First step of the page-view-model split (#14 phase 2):
/// the About page owns no shared state, so it moves cleanly out of <see cref="MainViewModel"/>.
/// The view is re-pointed to this VM via a DataContext binding, so navigation is unchanged.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    /// <summary>This project's GitHub repository.</summary>
    public string RepoUrl => "https://github.com/TheSevenPens/OpenTabletArtist";

    /// <summary>GitHub releases page (downloads + release notes).</summary>
    public string ReleasesUrl => $"{RepoUrl}/releases";

    /// <summary>The user manual, rendered on GitHub.</summary>
    public string UserManualUrl => $"{RepoUrl}/blob/master/docs/USERMANUAL.md";

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
}
