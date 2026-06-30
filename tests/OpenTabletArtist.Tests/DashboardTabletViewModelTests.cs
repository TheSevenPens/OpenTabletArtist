using System;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DashboardTabletViewModelTests
{
    private static DashboardTabletViewModel NewCard(
        string name, Func<string, Task>? openSettings = null, Action<string>? setActive = null,
        bool isActive = false, bool showSetActive = false) =>
        new(new DetectedTablet(name, "152 x 95 mm", "1024", "4"),
            openSettings ?? (_ => Task.CompletedTask),
            setActive ?? (_ => { }),
            isActive, showSetActive);

    [Fact]
    public void SpecsText_FormatsAreaPressureButtons_AndCarriesActiveFlag()
    {
        var vm = NewCard("Wacom CTL-480", isActive: true);

        Assert.Equal("Wacom CTL-480", vm.Name);
        Assert.Equal("152 x 95 mm · 1024 pressure levels · 4 buttons", vm.SpecsText);
        Assert.True(vm.IsActive);
    }

    [Fact]
    public void NonActiveCard_ShowsSetActive_ActiveCardDoesNot()
    {
        Assert.True(NewCard("A", isActive: false, showSetActive: true).ShowSetActive);
        Assert.False(NewCard("A", isActive: true, showSetActive: false).ShowSetActive);
    }

    [Fact]
    public void SetActive_InvokesCallbackWithThisTabletsName()
    {
        string? activated = null;
        var vm = NewCard("XP-Pen Deco", setActive: name => activated = name, showSetActive: true);

        vm.SetActiveCommand.Execute(null);

        Assert.Equal("XP-Pen Deco", activated);
    }

    [Fact]
    public async Task OpenSettings_InvokesCallbackWithThisTabletsName()
    {
        string? opened = null;
        var vm = NewCard("XP-Pen Deco", openSettings: name => { opened = name; return Task.CompletedTask; });

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("XP-Pen Deco", opened);
    }
}
