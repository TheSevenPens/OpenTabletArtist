using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The Supported Tablets dialog (#155): the full OTD catalog with live search, a "X of TOTAL" count,
/// numbered rows, and the connected tablet highlighted. Read-only over <see cref="SupportedTabletsCatalog"/>.
/// </summary>
public sealed partial class SupportedTabletsDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<SupportedTablet> _all;
    private readonly string? _detectedName;

    public SupportedTabletsDialogViewModel(string? detectedName)
    {
        _all = SupportedTabletsCatalog.All;
        _detectedName = string.IsNullOrWhiteSpace(detectedName) ? null : detectedName;
        Rebuild();
    }

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private List<SupportedTabletRow> _rows = new();
    [ObservableProperty] private string _countText = "";

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var query = SearchText?.Trim() ?? "";
        var filtered = query.Length == 0
            ? _all
            : _all.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var rows = new List<SupportedTabletRow>(filtered.Count);
        int n = 1;
        foreach (var t in filtered)
        {
            bool detected = _detectedName != null
                && string.Equals(t.Name, _detectedName, StringComparison.OrdinalIgnoreCase);
            rows.Add(new SupportedTabletRow(n++, t, detected));
        }
        Rows = rows;

        // "X of TOTAL" while filtered; the bare total otherwise.
        CountText = filtered.Count == _all.Count
            ? $"{_all.Count} tablets"
            : $"{filtered.Count} of {_all.Count}";
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
    }

    public int Number { get; }
    public bool IsDetected { get; }
    public string Name { get; }
    public string ActiveArea { get; }
    public string Pressure { get; }
    public string Buttons { get; }
}
