using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OtdWindowsHelper.ViewModels;

/// <summary>
/// View model for the About page. First step of the page-view-model split (#14 phase 2):
/// the About page owns no shared state, so it moves cleanly out of <see cref="MainViewModel"/>.
/// The view is re-pointed to this VM via a DataContext binding, so navigation is unchanged.
/// </summary>
public partial class AboutViewModel : ObservableObject
{
    /// <summary>This project's GitHub repository.</summary>
    public string RepoUrl => "https://github.com/TheSevenPens/OTDWindowsHelper";

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
