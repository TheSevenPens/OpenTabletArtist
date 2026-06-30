using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the OTD page (under the sidebar's Advanced group) — details about the bundled
/// OpenTabletDriver engine. Currently the embedded OTD version + a launcher for OTD's own UX, moved
/// here off the Home dashboard.
/// </summary>
public partial class OtdViewModel : ObservableObject
{
    private readonly IConnectionState _connection;

    public OtdViewModel(IConnectionState connection) => _connection = connection;

    /// <summary>The version of the bundled OpenTabletDriver (read from its Desktop assembly).</summary>
    public string CurrentOtdVersion { get; } = typeof(Settings).Assembly.GetName().Version?.ToString() ?? "Unknown";

    /// <summary>Launches OTD's own WPF UX (forwarded from the session).</summary>
    public IRelayCommand LaunchOtdUxCommand => _connection.LaunchOtdUxCommand;
}
