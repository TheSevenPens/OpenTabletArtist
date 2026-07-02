using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class MonitorCycleServiceTests
{
    private sealed class RecordingCoordinator : ISettingsCoordinator
    {
        public Settings? CurrentSettings { get; set; }
        public Settings? SavedAndApplied { get; private set; }
        public Task ApplyAndSaveSettingsAsync(Settings s) { SavedAndApplied = s; return Task.CompletedTask; }
        public Task ApplyLiveOnlyAsync(Settings s) => Task.CompletedTask;
        public Task ApplyEphemeralAsync(Settings s) => Task.CompletedTask;
        public Task RestoreDefaultAsync() => Task.CompletedTask;
    }

    private static DisplayInfo Display(int number, int x, int y, int w, int h) =>
        new(number, $"Monitor {number}", w, h, x, y, IsPrimary: number == 1);

    private static readonly IReadOnlyList<DisplayInfo> TwoDisplays = new[]
    {
        Display(1, 0, 0, 1920, 1080),
        Display(2, 1920, 0, 2560, 1440),
    };

    // A tablet profile already mapped to the given display (tablet area pre-sized so the null-digitizer
    // fallback in the service has a non-degenerate area to work from).
    private static Settings SettingsMappedTo(DisplayInfo display, string tablet = "Test Tablet")
    {
        var profile = new Profile
        {
            Tablet = tablet,
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings(),
                Tablet = new AreaSettings { Width = 100, Height = 100 },
            },
        };
        DisplayMappingApplier.ApplyToProfile(profile, (100f, 100f), display);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static MonitorCycleService NewService(RecordingCoordinator coord, FakeDeviceData device,
        IReadOnlyList<DisplayInfo>? displays = null)
        => new(coord, device, () => displays ?? TwoDisplays);

    [Fact]
    public async Task Cycle_MovesActiveTabletToNextDisplay()
    {
        var coord = new RecordingCoordinator { CurrentSettings = SettingsMappedTo(TwoDisplays[0]) };
        var device = new FakeDeviceData { ActiveTabletName = "Test Tablet" };
        var svc = NewService(coord, device);
        string? message = null; svc.Cycled += m => message = m;

        await svc.CycleAsync();

        Assert.NotNull(coord.SavedAndApplied);
        var profile = coord.CurrentSettings!.Profiles[0];
        Assert.Equal(2, DisplayMappingApplier.CurrentlyMapped(profile, TwoDisplays)!.Number);
        Assert.Contains("Test Tablet", message);
    }

    [Fact]
    public async Task Cycle_WrapsFromLastToFirst()
    {
        var coord = new RecordingCoordinator { CurrentSettings = SettingsMappedTo(TwoDisplays[1]) };
        var device = new FakeDeviceData { ActiveTabletName = "Test Tablet" };
        var svc = NewService(coord, device);

        await svc.CycleAsync();

        var profile = coord.CurrentSettings!.Profiles[0];
        Assert.Equal(1, DisplayMappingApplier.CurrentlyMapped(profile, TwoDisplays)!.Number);
    }

    [Fact]
    public async Task Cycle_NoOp_WhenSingleDisplay()
    {
        var coord = new RecordingCoordinator { CurrentSettings = SettingsMappedTo(TwoDisplays[0]) };
        var device = new FakeDeviceData { ActiveTabletName = "Test Tablet" };
        var svc = NewService(coord, device, new[] { Display(1, 0, 0, 1920, 1080) });
        string? message = null; svc.Cycled += m => message = m;

        await svc.CycleAsync();

        Assert.Null(coord.SavedAndApplied);        // nothing persisted
        Assert.Contains("one display", message);
    }

    [Fact]
    public async Task Cycle_NoOp_WhenNoActiveTablet()
    {
        var coord = new RecordingCoordinator { CurrentSettings = SettingsMappedTo(TwoDisplays[0]) };
        var device = new FakeDeviceData { ActiveTabletName = null };  // and no detected tablets
        var svc = NewService(coord, device);
        string? message = null; svc.Cycled += m => message = m;

        await svc.CycleAsync();

        Assert.Null(coord.SavedAndApplied);
        Assert.Contains("active tablet", message);
    }
}
