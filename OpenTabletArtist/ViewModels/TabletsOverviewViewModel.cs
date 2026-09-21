using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Landing page for the Tablets group (shown when its header is clicked / no tablet is selected).
/// Lists every known tablet — detected and remembered — with its detection status and last-seen time,
/// each navigable to its settings page. The row list and <see cref="HasTablets"/> are kept in sync by
/// the shell as tablets come and go (#307). (The supported-tablets catalog link now lives in Home's
/// About → RESOURCES card.)
/// </summary>
public partial class TabletsOverviewViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasTablets;

    /// <summary>One row per known tablet, rebuilt by the shell on each data load.</summary>
    [ObservableProperty] private List<TabletOverviewItemViewModel> _tablets = [];
}

/// <summary>One tablet on the overview: detection status + last-seen, navigable to its settings page.
/// Navigation is a callback supplied by the shell so the row stays UI-only (#307).</summary>
public partial class TabletOverviewItemViewModel : ObservableObject
{
    private readonly Action _navigate;
    private readonly Func<Task> _forget;

    public TabletOverviewItemViewModel(string name, bool isDetected, string statusText,
        string? statusDetail, Action navigate, Func<Task> forget)
    {
        Name = name;
        IsDetected = isDetected;
        StatusText = statusText;
        StatusDetail = statusDetail;
        _navigate = navigate;
        _forget = forget;
    }

    public string Name { get; }
    public bool IsDetected { get; }
    public string StatusText { get; }
    /// <summary>The card's third line: CONNECTED, or when the tablet was last seen.</summary>
    public string? StatusDetail { get; }
    public bool HasStatusDetail => !string.IsNullOrEmpty(StatusDetail);

    [RelayCommand]
    private void Open() => _navigate();

    /// <summary>Forget this tablet — remove its saved profile from the settings.</summary>
    [RelayCommand]
    private Task Forget() => _forget();
}
