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
        string? lastSeenDetail, string mappingText, bool mappingNeedsAttention,
        Action navigate, Func<Task> forget)
    {
        Name = name;
        IsDetected = isDetected;
        StatusText = statusText;
        LastSeenDetail = lastSeenDetail;
        MappingText = mappingText;
        MappingNeedsAttention = mappingNeedsAttention;
        _navigate = navigate;
        _forget = forget;
    }

    public string Name { get; }
    public bool IsDetected { get; }
    public string StatusText { get; }
    public string? LastSeenDetail { get; }
    public bool HasLastSeenDetail => !string.IsNullOrEmpty(LastSeenDetail);

    /// <summary>Where this tablet's active area is mapped, e.g. "Mapped to Display 1", or a short
    /// "needs attention" phrase when it isn't a standard single-display mapping (#tablet-card-mapping).
    /// Observable so a live display change can refresh it in place (see <see cref="UpdateMapping"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapping))]
    [NotifyPropertyChangedFor(nameof(MappingIsNormal))]
    private string _mappingText;

    /// <summary>True when <see cref="MappingText"/> is a warning, so the card draws it emphasised.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MappingIsNormal))]
    private bool _mappingNeedsAttention;

    public bool HasMapping => !string.IsNullOrEmpty(MappingText);
    /// <summary>A normal (non-warning) mapping line — drawn in secondary text rather than the warning colour.</summary>
    public bool MappingIsNormal => HasMapping && !MappingNeedsAttention;

    /// <summary>Refresh the mapped-display line in place — used when the connected monitors change so the
    /// card updates without a full list rebuild (#tablet-card-mapping).</summary>
    public void UpdateMapping(string text, bool needsAttention)
    {
        MappingText = text;
        MappingNeedsAttention = needsAttention;
    }

    [RelayCommand]
    private void Open() => _navigate();

    /// <summary>Forget this tablet — remove its saved profile from the settings.</summary>
    [RelayCommand]
    private Task Forget() => _forget();
}
