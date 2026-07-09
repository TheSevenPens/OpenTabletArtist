using System.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class SupportedTabletsTests
{
    // Reads the embedded OTD configs end-to-end (#155): proves the in-process catalog path works.
    [Fact]
    public void Catalog_LoadsTheBuiltInTablets()
    {
        var all = SupportedTabletsCatalog.All;
        Assert.True(all.Count > 100, $"expected the full OTD catalog (~339); got {all.Count}");
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
        // Sorted by name.
        Assert.Equal(all.Select(t => t.Name).OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase),
                     all.Select(t => t.Name));
    }

    [Fact]
    public void DialogVm_Unfiltered_ShowsAll_NumbersRows_HighlightsDetected()
    {
        var all = SupportedTabletsCatalog.All;
        var sample = all.First();
        var vm = new SupportedTabletsDialogViewModel(sample.Name);

        Assert.Equal(all.Count, vm.Rows.Count);
        Assert.Equal($"{all.Count} tablets", vm.CountText);   // bare total when not filtered
        Assert.Equal(1, vm.Rows[0].Number);                    // 1-based numbering
        Assert.Contains(vm.Rows, r => r.IsDetected && r.Name == sample.Name);
    }

    [Fact]
    public void DialogVm_Search_Filters_AndCountsXofTotal()
    {
        var all = SupportedTabletsCatalog.All;
        var vm = new SupportedTabletsDialogViewModel(null);

        vm.SearchText = all.First().Name;
        Assert.True(vm.Rows.Count >= 1 && vm.Rows.Count < all.Count);
        Assert.Equal($"{vm.Rows.Count} of {all.Count}", vm.CountText);
        Assert.Equal(1, vm.Rows[0].Number); // rows renumber within the filtered set

        vm.SearchText = "zzzzz-no-such-tablet";
        Assert.Empty(vm.Rows);
        Assert.Equal($"0 of {all.Count}", vm.CountText);

        vm.SearchText = "";
        Assert.Equal(all.Count, vm.Rows.Count);
        Assert.Equal($"{all.Count} tablets", vm.CountText);
    }

    [Fact]
    public void DialogVm_Brands_ListsAllBrandsFirst_ThenDistinctSorted()
    {
        var vm = new SupportedTabletsDialogViewModel(null);

        Assert.Equal(SupportedTabletsDialogViewModel.AllBrands, vm.Brands[0]);
        Assert.True(vm.Brands.Count > 2);
        Assert.Contains("Wacom", vm.Brands);
        var tail = vm.Brands.Skip(1).ToList();
        Assert.Equal(tail.Distinct().OrderBy(b => b, System.StringComparer.OrdinalIgnoreCase), tail);
    }

    [Fact]
    public void DialogVm_BrandFilter_LimitsToThatBrand_AndCountsXofTotal()
    {
        var all = SupportedTabletsCatalog.All;
        var vm = new SupportedTabletsDialogViewModel(null);

        vm.SelectedBrand = "Wacom";
        Assert.True(vm.Rows.Count >= 1 && vm.Rows.Count < all.Count);
        Assert.All(vm.Rows, r => Assert.StartsWith("Wacom", r.Name));
        Assert.Equal($"{vm.Rows.Count} of {all.Count}", vm.CountText);

        vm.SelectedBrand = SupportedTabletsDialogViewModel.AllBrands;
        Assert.Equal(all.Count, vm.Rows.Count);
    }

    [Fact]
    public void DialogVm_SortByButtons_OrdersRows_AndTogglesDirection()
    {
        var vm = new SupportedTabletsDialogViewModel(null);

        vm.SortByCommand.Execute("Buttons"); // ascending
        var asc = vm.Rows.Select(r => int.Parse(r.Buttons)).ToList();
        Assert.Equal(asc.OrderBy(x => x), asc);

        vm.SortByCommand.Execute("Buttons"); // same column again → descending
        var desc = vm.Rows.Select(r => int.Parse(r.Buttons)).ToList();
        Assert.Equal(desc.OrderByDescending(x => x), desc);
    }
}
