using System;
using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Landing page for the Tablets group (shown when its header is clicked / no tablet is selected).
/// Lists every known tablet — detected and remembered — with its detection status, last-seen time,
/// and specs, each navigable to its settings page, plus a link to OTD's supported-tablets list. The
/// row list and <see cref="HasTablets"/> are kept in sync by the shell as tablets come and go (#307).
/// </summary>
public partial class TabletsOverviewViewModel : ObservableObject
{
    /// <summary>OTD's supported-tablets page — surfaced so users can check whether their tablet (or
    /// one they're considering) is supported, without leaving the app to go hunting for it.</summary>
    public const string SupportedTabletsUrl = "https://opentabletdriver.net/Tablets";

    [ObservableProperty] private bool _hasTablets;

    /// <summary>One row per known tablet, rebuilt by the shell on each data load.</summary>
    [ObservableProperty] private List<TabletOverviewItemViewModel> _tablets = [];

    [RelayCommand]
    private void OpenSupportedTablets()
    {
        try { Process.Start(new ProcessStartInfo(SupportedTabletsUrl) { UseShellExecute = true }); }
        catch { }
    }
}

/// <summary>One tablet on the overview: detection status + last-seen + specs, navigable to its
/// settings page. Navigation is a callback supplied by the shell so the row stays UI-only (#307).</summary>
public partial class TabletOverviewItemViewModel : ObservableObject
{
    private readonly Action _navigate;

    public TabletOverviewItemViewModel(string name, bool isDetected, string statusText,
        string? lastSeenDetail, string specsText, Action navigate)
    {
        Name = name;
        IsDetected = isDetected;
        StatusText = statusText;
        LastSeenDetail = lastSeenDetail;
        SpecsText = specsText;
        _navigate = navigate;
    }

    public string Name { get; }
    public bool IsDetected { get; }
    public string StatusText { get; }
    public string? LastSeenDetail { get; }
    public string SpecsText { get; }
    public bool HasSpecs => !string.IsNullOrEmpty(SpecsText);
    public bool HasLastSeenDetail => !string.IsNullOrEmpty(LastSeenDetail);

    [RelayCommand]
    private void Open() => _navigate();
}
