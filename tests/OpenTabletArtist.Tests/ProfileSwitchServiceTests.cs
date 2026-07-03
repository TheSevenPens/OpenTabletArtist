using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileSwitchServiceTests
{
    private sealed class FakeCoordinator : ISettingsCoordinator
    {
        public Settings? CurrentSettings { get; set; }
        public int LiveOnlyCalls;
        public int RestoreCalls;
        public int SaveCalls;
        public Task ApplyAndSaveSettingsAsync(Settings settings) { SaveCalls++; return Task.CompletedTask; }
        public Task ApplyLiveOnlyAsync(Settings settings) { LiveOnlyCalls++; return Task.CompletedTask; }
        public int EphemeralCalls;
        public Task ApplyEphemeralAsync(Settings settings) { EphemeralCalls++; return Task.CompletedTask; }
        public Task RestoreDefaultAsync() { RestoreCalls++; return Task.CompletedTask; }
    }

    private sealed class FakeStore : ISettingsFileStore
    {
        public readonly HashSet<string> Existing = new();
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings)
        {
            if (Existing.Contains(path)) { settings = new Settings(); return true; }
            settings = null;
            return false;
        }
    }

    private static (ProfileSwitchService svc, FakeCoordinator coord, FakeStore store) Make(string dir)
    {
        var coord = new FakeCoordinator();
        var store = new FakeStore();
        return (new ProfileSwitchService(coord, store, () => dir), coord, store);
    }

    [Fact]
    public async Task SwitchTo_ExistingSnapshot_AppliesLiveOnly_AndSetsOverride()
    {
        var (svc, coord, store) = Make(@"C:\presets");
        store.Existing.Add(@"C:\presets\Draw.json");

        var ok = await svc.SwitchToAsync("Draw");

        Assert.True(ok);
        Assert.Equal(1, coord.LiveOnlyCalls);
        Assert.Equal(0, coord.SaveCalls); // live-only must NOT persist
        Assert.Equal("Draw", svc.ActiveSnapshot);
        Assert.True(svc.HasOverride);
    }

    [Fact]
    public async Task SwitchTo_MissingSnapshot_ReturnsFalse_NoOverride()
    {
        var (svc, coord, _) = Make(@"C:\presets");

        var ok = await svc.SwitchToAsync("Gone");

        Assert.False(ok);
        Assert.Equal(0, coord.LiveOnlyCalls);
        Assert.False(svc.HasOverride);
    }

    [Fact]
    public async Task RestoreDefault_ClearsOverride_AndReverts()
    {
        var (svc, coord, store) = Make(@"C:\presets");
        store.Existing.Add(@"C:\presets\Draw.json");
        await svc.SwitchToAsync("Draw");

        await svc.RestoreDefaultAsync();

        Assert.Equal(1, coord.RestoreCalls);
        Assert.Null(svc.ActiveSnapshot);
        Assert.False(svc.HasOverride);
    }

    [Fact]
    public async Task RestoreDefault_WhenNotOverridden_IsNoOp()
    {
        var (svc, coord, _) = Make(@"C:\presets");

        await svc.RestoreDefaultAsync();

        Assert.Equal(0, coord.RestoreCalls);
    }

    [Fact]
    public async Task Switched_Event_FiresWithName_ThenNullOnRestore()
    {
        var (svc, _, store) = Make(@"C:\presets");
        store.Existing.Add(@"C:\presets\Draw.json");
        var events = new List<string?>();
        svc.Switched += n => events.Add(n);

        await svc.SwitchToAsync("Draw");
        await svc.RestoreDefaultAsync();

        Assert.Equal(new string?[] { "Draw", null }, events);
    }
}
