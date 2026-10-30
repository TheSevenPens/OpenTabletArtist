using System;
using System.Collections.Generic;
using System.Linq;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoneConnected))]
    private bool _hasTablets;

    /// <summary>One row per known tablet, rebuilt by the shell on each data load.</summary>
    [ObservableProperty] private List<TabletOverviewItemViewModel> _tablets = [];

    /// <summary>The tablets that are plugged in right now: the ones Home shows as cards (#1050).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnected))]
    [NotifyPropertyChangedFor(nameof(ShowNoneConnected))]
    private List<TabletOverviewItemViewModel> _connected = [];

    /// <summary>The ones remembered from before. A machine that has met many tablets would otherwise push
    /// everything else on Home out of sight, so they sit behind one row that opens (#1050).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemembered))]
    [NotifyPropertyChangedFor(nameof(RememberedLabel))]
    private List<TabletOverviewItemViewModel> _remembered = [];

    /// <summary>Whether the remembered tablets are listed. Not kept between runs: the list is something you
    /// open to look something up, not a setting.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RememberedHint))]
    private bool _isRememberedExpanded;

    public bool HasConnected => Connected.Count > 0;
    public bool HasRemembered => Remembered.Count > 0;

    /// <summary>Tablets are known but none is plugged in: say so, since an empty card list reads as broken.</summary>
    public bool ShowNoneConnected => HasTablets && !HasConnected;

    public string RememberedLabel => Remembered.Count == 1 ? "1 remembered tablet" : $"{Remembered.Count} remembered tablets";

    public string RememberedHint => IsRememberedExpanded ? "hide" : "show";

    partial void OnTabletsChanged(List<TabletOverviewItemViewModel> value)
    {
        Connected = value.Where(t => t.IsDetected).ToList();
        Remembered = value.Where(t => !t.IsDetected).ToList();
    }

    [RelayCommand]
    private void ToggleRemembered() => IsRememberedExpanded = !IsRememberedExpanded;
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
