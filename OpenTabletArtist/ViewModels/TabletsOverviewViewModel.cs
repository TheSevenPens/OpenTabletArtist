using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Landing page for the Tablets group (shown when its header is clicked / no tablet is selected).
/// Lists every known tablet — detected and remembered — with its detection status, last-seen time,
/// and specs, each navigable to its settings page, plus a link to OTD's supported-tablets list. The
/// row list and <see cref="HasTablets"/> are kept in sync by the shell as tablets come and go (#307).
/// </summary>
public partial class TabletsOverviewViewModel : ObservableObject
{
    private readonly IDialogService? _dialogs;

    public TabletsOverviewViewModel(IDialogService? dialogs = null) => _dialogs = dialogs;

    [ObservableProperty] private bool _hasTablets;

    /// <summary>One row per known tablet, rebuilt by the shell on each data load.</summary>
    [ObservableProperty] private List<TabletOverviewItemViewModel> _tablets = [];

    /// <summary>Show OTD's built-in supported-tablets catalog in an in-app dialog (#155), highlighting
    /// the connected tablet — instead of opening the OTD website in a browser.</summary>
    [RelayCommand]
    private async Task OpenSupportedTablets()
    {
        if (_dialogs == null) return;
        var detected = Tablets.FirstOrDefault(t => t.IsDetected)?.Name;
        await _dialogs.ShowSupportedTabletsAsync(detected);
    }
}

/// <summary>One tablet on the overview: detection status + last-seen + specs, navigable to its
/// settings page. Navigation is a callback supplied by the shell so the row stays UI-only (#307).</summary>
public partial class TabletOverviewItemViewModel : ObservableObject
{
    private readonly Action _navigate;
    private readonly Func<Task> _forget;

    public TabletOverviewItemViewModel(string name, bool isDetected, string statusText,
        string? lastSeenDetail, string specsText, Action navigate, Func<Task> forget)
    {
        Name = name;
        IsDetected = isDetected;
        StatusText = statusText;
        LastSeenDetail = lastSeenDetail;
        SpecsText = specsText;
        _navigate = navigate;
        _forget = forget;
    }

    public string Name { get; }
    public bool IsDetected { get; }
    public string StatusText { get; }
    public string? LastSeenDetail { get; }
    public string SpecsText { get; }
    public bool HasSpecs => !string.IsNullOrEmpty(SpecsText);
    public bool HasLastSeenDetail => !string.IsNullOrEmpty(LastSeenDetail);
    /// <summary>Managing connections lives on Home (#forget-home): the Forget action is offered here only
    /// for remembered, disconnected tablets — it's odd to remove one you're currently using.</summary>
    public bool CanForget => !IsDetected;

    [RelayCommand]
    private void Open() => _navigate();

    /// <summary>Forget this tablet — remove its saved profile from the settings.</summary>
    [RelayCommand]
    private Task Forget() => _forget();
}
