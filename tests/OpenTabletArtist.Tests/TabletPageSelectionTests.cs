using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>The TABLET page's default-selection policy (#542): first detected on start, the last-used one
/// when several are detected, graceful fallback when nothing is detected, and sticky selection across
/// background reconciliations.</summary>
public class TabletPageSelectionTests
{
    private sealed class Box { public string? Value; }

    // A host whose resolver hands back a throwaway detail VM and whose last-used store is in-memory, so the
    // policy is exercised without the daemon plumbing or the on-disk settings.
    private static TabletPageViewModel Make(string? lastUsed, out Box store)
    {
        store = new Box { Value = lastUsed };
        var box = store;
        return new TabletPageViewModel(_ => new TabletDetailViewModel(),
            () => box.Value, v => box.Value = v!);
    }

    private static IReadOnlyList<(string, bool)> Tablets(params (string name, bool detected)[] t)
    {
        var list = new List<(string, bool)>();
        foreach (var (n, d) in t) list.Add((n, d));
        return list;
    }

    [Fact]
    public void NoTablets_SelectsNothing_AndShowsPlaceholder()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets());
        Assert.Null(vm.SelectedTablet);
        Assert.False(vm.HasTablet);
    }

    [Fact]
    public void SingleTablet_IsSelected()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Wacom PTK-870", true)));
        Assert.Equal("Wacom PTK-870", vm.SelectedTablet?.Name);
        Assert.True(vm.HasTablet);
    }

    [Fact]
    public void MultipleDetected_NoLastUsed_PicksFirstDetected()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true), ("Remembered C", false)));
        Assert.Equal("Detected A", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void MultipleDetected_PrefersLastUsedAmongDetected()
    {
        var vm = Make(lastUsed: "Detected B", out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        Assert.Equal("Detected B", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void LastUsedNotDetected_FallsBackToFirstDetected()
    {
        // The remembered last-used isn't currently detected, so a detected tablet wins on startup.
        var vm = Make(lastUsed: "Remembered C", out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Remembered C", false)));
        Assert.Equal("Detected A", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void NoneDetected_PrefersLastUsedRemembered()
    {
        var vm = Make(lastUsed: "Remembered B", out _);
        vm.SetTablets(Tablets(("Remembered A", false), ("Remembered B", false)));
        Assert.Equal("Remembered B", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void NoneDetected_NoLastUsed_PicksFirstInOrder()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Remembered A", false), ("Remembered B", false)));
        Assert.Equal("Remembered A", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void Reconciliation_KeepsCurrentSelection()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");                          // user switches
        Assert.Equal("Detected B", vm.SelectedTablet?.Name);
        // A background data reload re-runs SetTablets; the current selection must survive.
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        Assert.Equal("Detected B", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void UserSelection_PersistsAsLastUsed()
    {
        var vm = Make(lastUsed: null, out var store);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");
        Assert.Equal("Detected B", store.Value);
    }

    [Fact]
    public void Reconciliation_DoesNotClobberLastUsed()
    {
        var vm = Make(lastUsed: "Detected A", out var store);
        // Auto-selection on reconcile must not overwrite the stored last-used with its pick.
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        Assert.Equal("Detected A", store.Value);
    }

    [Fact]
    public void ForgottenSelectedTablet_ReselectsSurvivor()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");
        // "Detected B" is forgotten; the next reconcile drops it and reselects a survivor.
        vm.SetTablets(Tablets(("Detected A", true)));
        Assert.Equal("Detected A", vm.SelectedTablet?.Name);
    }

    [Fact]
    public void SameSetReconcile_PreservesSelectionInstance()
    {
        // A no-change data poll (~15s) must reconcile in place, NOT rebuild the list — a rebuild nulls the
        // ComboBox selection and recreates the hosted detail view, snapping it back to its first tab.
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");
        var before = vm.SelectedTablet;
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        Assert.Same(before, vm.SelectedTablet);
    }

    [Fact]
    public void SameSetReconcile_UpdatesDetectionInPlace()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Wacom", true)));
        var choice = vm.SelectedTablet;
        // Detection flips but the set + order is unchanged: update the same instance in place.
        vm.SetTablets(Tablets(("Wacom", false)));
        Assert.Same(choice, vm.SelectedTablet);
        Assert.False(vm.SelectedTablet!.IsDetected);
    }

    [Fact]
    public void MembershipChange_AddingTablet_PreservesSelectionInstance()
    {
        // Adding a tablet is a membership change, but the previously-selected item must survive by instance
        // — a Clear()+rebuild would drop it, flashing the empty switcher / "no tablet" hero (#pen-split).
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");
        var before = vm.SelectedTablet;
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true), ("Detected C", true)));
        Assert.Same(before, vm.SelectedTablet);
        Assert.Equal(3, vm.Tablets.Count);
    }

    [Fact]
    public void MembershipChange_RemovingOtherTablet_PreservesSelectionInstance()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true), ("Detected C", true)));
        vm.Select("Detected B");
        var before = vm.SelectedTablet;
        // A different tablet (C) drops out; the selected one (B) keeps its instance.
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        Assert.Same(before, vm.SelectedTablet);
        Assert.Equal(2, vm.Tablets.Count);
    }

    [Fact]
    public void Reorder_PreservesSelectionInstanceAndReordersInPlace()
    {
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("A", true), ("B", true), ("C", true)));
        vm.Select("B");
        var before = vm.SelectedTablet;
        // The poll reports the same set in a new order; selection survives and the list is reordered.
        vm.SetTablets(Tablets(("C", true), ("B", true), ("A", true)));
        Assert.Same(before, vm.SelectedTablet);
        Assert.Equal(new[] { "C", "B", "A" }, vm.Tablets.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void SpuriousNull_WithTabletsPresent_KeepsSelection()
    {
        // The bound ComboBox writes a null back when its ItemsSource is (re)set as the page crossfades in.
        // With tablets still present that must be ignored so the selection / Content / switcher survive.
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets(("Detected A", true), ("Detected B", true)));
        vm.Select("Detected B");
        var before = vm.SelectedTablet;
        vm.SelectedTablet = null;                 // simulate the ComboBox's spurious writeback
        Assert.Same(before, vm.SelectedTablet);
        Assert.True(vm.HasTablet);
    }

    [Fact]
    public void Null_WithNoTablets_IsRespected()
    {
        // A genuine "nothing selected" (no tablets at all) must still be allowed through.
        var vm = Make(lastUsed: null, out _);
        vm.SetTablets(Tablets());
        vm.SelectedTablet = null;
        Assert.Null(vm.SelectedTablet);
        Assert.False(vm.HasTablet);
    }
}
