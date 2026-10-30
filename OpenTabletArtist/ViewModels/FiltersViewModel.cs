using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The Filters tab on the Advanced page (#1049): every known tablet's filters, one section per tablet.
/// </summary>
/// <remarks>
/// It lived on each tablet's own page, where it could only ever show that tablet. Filters are per tablet, not
/// global (#1052: the daemon builds each tablet's pipeline from that tablet's profile alone), so a home for them
/// on a page with no tablet switcher lists all of them rather than guessing which one is meant.
/// </remarks>
public sealed partial class FiltersViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceData _deviceData;

    public FiltersViewModel(IDeviceData deviceData)
    {
        _deviceData = deviceData;
        _deviceData.DataLoaded += Refresh;
        Refresh();
    }

    /// <summary>One section per known tablet: the plugged-in ones first, then the remembered ones by name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTablets))]
    private IReadOnlyList<TabletFiltersViewModel> _tablets = [];

    public bool HasTablets => Tablets.Count > 0;

    /// <summary>Rebuild from the saved profiles. Called when settings load and when the tab is opened, so it shows
    /// what is saved now, including a filter switched on or off from the Pen page since.</summary>
    public void Refresh() =>
        Tablets = _deviceData.Profiles
            .OrderByDescending(p => p.IsDetected)
            .ThenBy(p => p.Tablet, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new TabletFiltersViewModel(p.Tablet, p.StatusText, Cards(p.Profile.Filters)))
            .ToList();

    /// <summary>The cards for one profile's filters, in the order the driver runs them.</summary>
    internal static IReadOnlyList<FilterCardViewModel> Cards(IEnumerable<PluginSettingStore?>? filters) =>
        (filters ?? []).Select(f =>
        {
            var path = f?.Path ?? "Unknown";
            var typeName = path.Split('.').LastOrDefault() ?? "Unknown";
            return new FilterCardViewModel(
                title: FriendlyFilterName(typeName),
                fullPath: path,
                enabled: f?.Enable ?? true,
                origin: ProfileFilterMaintenance.Classify(path));
        }).ToList();

    /// <summary>Maps our plugin filter class names to the friendly labels they carry as
    /// <c>[PluginName]</c> in the plugin; anything else (third-party filters) keeps its type name.</summary>
    internal static string FriendlyFilterName(string typeName) => typeName switch
    {
        "DynamicsFilter" => "Pen Dynamics",
        "HoverFilter" => "Hover Limit",
        "CalibrationFilter" => "Calibration",
        _ => typeName,
    };

    public void Dispose() => _deviceData.DataLoaded -= Refresh;
}

/// <summary>One tablet's filters, as a section.</summary>
public sealed class TabletFiltersViewModel(string name, string status, IReadOnlyList<FilterCardViewModel> filters)
{
    public string Name { get; } = name;

    /// <summary>"Connected", or when the tablet was last seen.</summary>
    public string Status { get; } = status;

    public IReadOnlyList<FilterCardViewModel> Filters { get; } = filters;

    public bool HasFilters => Filters.Count > 0;
    public bool HasNoFilters => Filters.Count == 0;
}
