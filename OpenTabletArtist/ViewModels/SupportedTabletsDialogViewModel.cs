using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>Which column the Supported Tablets list is sorted by.</summary>
public enum SupportedTabletSort { Name, Area, Pressure, Buttons, Status }

/// <summary>
/// The Supported Tablets dialog (#155): the full OTD catalog with live search, a brand filter, sortable
/// columns, a "X of TOTAL" count, numbered rows, and the connected tablet highlighted. Read-only over
/// <see cref="SupportedTabletsCatalog"/>.
/// </summary>
public sealed partial class SupportedTabletsDialogViewModel : ObservableObject
{
    public const string AllBrands = "All brands";

    private readonly IReadOnlyList<SupportedTablet> _all;
    private readonly string? _detectedName;
    private SupportedTabletSort _sort = SupportedTabletSort.Name;
    private bool _sortDescending;

    public SupportedTabletsDialogViewModel(string? detectedName)
    {
        _all = SupportedTabletsCatalog.All;
        _detectedName = string.IsNullOrWhiteSpace(detectedName) ? null : detectedName;

        Brands = new List<string> { AllBrands };
        Brands.AddRange(_all.Select(t => t.Brand).Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase));

        Rebuild();
    }

    /// <summary>Filter options for the brand dropdown: "All brands" plus each distinct brand.</summary>
    public List<string> Brands { get; }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedBrand = AllBrands;
    [ObservableProperty] private List<SupportedTabletRow> _rows = new();
    [ObservableProperty] private string _countText = "";

    // Header labels carry the active-sort arrow so the current column + direction are obvious.
    public string NameHeader => "NAME" + Arrow(SupportedTabletSort.Name);
    public string AreaHeader => "ACTIVE AREA" + Arrow(SupportedTabletSort.Area);
    public string PressureHeader => "PRESSURE" + Arrow(SupportedTabletSort.Pressure);
    public string ButtonsHeader => "BUTTONS" + Arrow(SupportedTabletSort.Buttons);
    public string StatusHeader => "STATUS" + Arrow(SupportedTabletSort.Status);

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSelectedBrandChanged(string value) => Rebuild();

    /// <summary>Sort by a column; clicking the active column again reverses the direction.</summary>
    [RelayCommand]
    private void SortBy(string? column)
    {
        if (!Enum.TryParse<SupportedTabletSort>(column, out var next)) return;
        if (_sort == next) _sortDescending = !_sortDescending;
        else { _sort = next; _sortDescending = false; }
        Rebuild();
    }

    private string Arrow(SupportedTabletSort column) =>
        _sort != column ? "" : _sortDescending ? "  ▼" : "  ▲";

    private void Rebuild()
    {
        IEnumerable<SupportedTablet> query = _all;

        if (SelectedBrand != AllBrands)
            query = query.Where(t => string.Equals(t.Brand, SelectedBrand, StringComparison.OrdinalIgnoreCase));

        var search = SearchText?.Trim() ?? "";
        if (search.Length > 0)
            query = query.Where(t => t.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var filtered = query.ToList();

        // Sort by the chosen column; ties break by name so the order is deterministic.
        IOrderedEnumerable<SupportedTablet> ordered = _sort switch
        {
            SupportedTabletSort.Area => _sortDescending
                ? filtered.OrderByDescending(t => t.AreaValue) : filtered.OrderBy(t => t.AreaValue),
            SupportedTabletSort.Pressure => _sortDescending
                ? filtered.OrderByDescending(t => t.PressureValue) : filtered.OrderBy(t => t.PressureValue),
            SupportedTabletSort.Buttons => _sortDescending
                ? filtered.OrderByDescending(t => t.ButtonsValue) : filtered.OrderBy(t => t.ButtonsValue),
            SupportedTabletSort.Status => _sortDescending
                ? filtered.OrderByDescending(t => t.StatusRank) : filtered.OrderBy(t => t.StatusRank),
            _ => _sortDescending
                ? filtered.OrderByDescending(t => t.Name, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
        };
        var sorted = ordered.ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var rows = new List<SupportedTabletRow>(sorted.Count);
        int n = 1;
        foreach (var t in sorted)
        {
            bool detected = _detectedName != null
                && string.Equals(t.Name, _detectedName, StringComparison.OrdinalIgnoreCase);
            rows.Add(new SupportedTabletRow(n++, t, detected));
        }
        Rows = rows;

        // "X of TOTAL" while filtered (by brand and/or search); the bare total otherwise.
        CountText = filtered.Count == _all.Count
            ? $"{_all.Count} tablets"
            : $"{filtered.Count} of {_all.Count}";

        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(AreaHeader));
        OnPropertyChanged(nameof(PressureHeader));
        OnPropertyChanged(nameof(ButtonsHeader));
        OnPropertyChanged(nameof(StatusHeader));
    }
}

/// <summary>A numbered row in the Supported Tablets dialog.</summary>
public sealed class SupportedTabletRow
{
    public SupportedTabletRow(int number, SupportedTablet data, bool isDetected)
    {
        Number = number;
        IsDetected = isDetected;
        Name = data.Name;
        ActiveArea = data.ActiveArea;
        Pressure = data.Pressure;
        Buttons = data.Buttons;
        Status = data.Status;
        Notes = data.Notes;
    }

    public int Number { get; }
    public bool IsDetected { get; }
    public string Name { get; }
    public string ActiveArea { get; }
    public string Pressure { get; }
    public string Buttons { get; }
    public string Status { get; }
    public string Notes { get; }

    public bool HasStatus => Status.Length > 0;
    public bool HasNotes => Notes.Length > 0;
    // Pill colour classes: green Supported, amber Has Quirks, neutral Missing Features.
    public bool IsSupported => Status == "Supported";
    public bool IsQuirks => Status == "Has Quirks";
    public bool IsMissing => Status == "Missing Features";
}
