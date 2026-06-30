using System.Threading.Tasks;
using OtdArtist.Domain;
using OtdArtist.ViewModels;
using Xunit;

namespace OtdArtist.Tests;

public class DashboardTabletViewModelTests
{
    [Fact]
    public void SpecsText_FormatsAreaPressureButtons()
    {
        var vm = new DashboardTabletViewModel(
            new DetectedTablet("Wacom CTL-480", "152 x 95 mm", "1024", "4"),
            _ => Task.CompletedTask);

        Assert.Equal("Wacom CTL-480", vm.Name);
        Assert.Equal("152 x 95 mm · 1024 pressure levels · 4 buttons", vm.SpecsText);
    }

    [Fact]
    public async Task OpenSettings_InvokesCallbackWithThisTabletsName()
    {
        string? opened = null;
        var vm = new DashboardTabletViewModel(
            new DetectedTablet("XP-Pen Deco", "229 x 127 mm", "8192", "8"),
            name => { opened = name; return Task.CompletedTask; });

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal("XP-Pen Deco", opened);
    }
}
