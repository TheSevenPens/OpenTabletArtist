using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.Tests;

public class ProfileSwitchServiceTests
{
    // The service builds a snapshot path as Path.Combine(dir, name + ".json"), so key the fake store the
    // same way, from an OS-rooted directory. Backslash literals (C:\presets\Draw.json) don't match the
    // service's Path.Combine output on macOS/Linux (where the separator is '/'), so the Linux CI lane would
    // otherwise fail here even though the product is correct (#140).
    private static readonly string PresetsDir =
        Path.Combine(OperatingSystem.IsWindows() ? @"C:\" : "/", "presets");

    private static string Snapshot(string name) => Path.Combine(PresetsDir, name + ".json");

    /// <summary>A snapshot store where the test decides which preset files exist.</summary>
    private sealed class FakeStore : IPresetStore
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

    private static (ProfileSwitchService svc, FakeSettingsCoordinator coord, FakeStore store) Make(string dir)
    {
        var coord = new FakeSettingsCoordinator();
        var store = new FakeStore();
        return (new ProfileSwitchService(coord, store, () => dir), coord, store);
    }

    [Fact]
    public async Task SwitchTo_ExistingSnapshot_AppliesLiveOnly_AndSetsOverride()
    {
        var (svc, coord, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));

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
        var (svc, coord, _) = Make(PresetsDir);

        var ok = await svc.SwitchToAsync("Gone");

        Assert.False(ok);
        Assert.Equal(0, coord.LiveOnlyCalls);
        Assert.False(svc.HasOverride);
    }

    [Fact]
    public async Task RestoreDefault_ClearsOverride_AndReverts()
    {
        var (svc, coord, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        await svc.SwitchToAsync("Draw");

        await svc.RestoreDefaultAsync();

        Assert.Equal(1, coord.RestoreCalls);
        Assert.Null(svc.ActiveSnapshot);
        Assert.False(svc.HasOverride);
    }

    [Fact]
    public async Task RestoreDefault_WhenNotOverridden_IsNoOp()
    {
        var (svc, coord, _) = Make(PresetsDir);

        await svc.RestoreDefaultAsync();

        Assert.Equal(0, coord.RestoreCalls);
    }

    [Fact]
    public async Task Switched_Event_FiresWithName_ThenNullOnRestore()
    {
        var (svc, _, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        var events = new List<string?>();
        svc.Switched += n => events.Add(n);

        await svc.SwitchToAsync("Draw");
        await svc.RestoreDefaultAsync();

        Assert.Equal(new string?[] { "Draw", null }, events);
    }

    /// <summary>A hotkey whose preset has gone must say so. The press is discarded by the hotkey manager,
    /// so this event is the only thing standing between a deleted preset and a key that silently does
    /// nothing.</summary>
    [Fact]
    public async Task SwitchFailed_Event_FiresWhenTheSnapshotCannotBeLoaded()
    {
        var (svc, _, _) = Make(PresetsDir);
        var failures = new List<string>();
        svc.SwitchFailed += n => failures.Add(n);

        var ok = await svc.SwitchToAsync("Gone");

        Assert.False(ok);
        Assert.Equal(["Gone"], failures);
    }

    [Fact]
    public async Task SwitchFailed_Event_StaysQuietOnASuccessfulSwitch()
    {
        var (svc, _, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        var failures = new List<string>();
        svc.SwitchFailed += n => failures.Add(n);

        await svc.SwitchToAsync("Draw");

        Assert.Empty(failures);
    }

    // --- A restore that didn't happen must not look like one (#734) ---

    /// <summary>The core regression: when the saved default can't be read, the override is still running
    /// on the tablet. Clearing the cue here told the artist they were back on their own settings while
    /// the preset was still active — the exact mismatch between UI and hardware this must never produce.</summary>
    [Theory]
    [InlineData(SettingsRestoreStatus.SourceUnavailable)]
    [InlineData(SettingsRestoreStatus.Disconnected)]
    [InlineData(SettingsRestoreStatus.ApplyFailed)]
    public async Task RestoreDefault_WhenTheRestoreFails_KeepsTheOverrideIndicator(SettingsRestoreStatus status)
    {
        var (svc, coord, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        await svc.SwitchToAsync("Draw");
        coord.RestoreResult = new SettingsRestoreOutcome(status);

        var ok = await svc.RestoreDefaultAsync();

        Assert.False(ok);
        Assert.Equal("Draw", svc.ActiveSnapshot);
        Assert.True(svc.HasOverride);
    }

    [Fact]
    public async Task RestoreDefault_WhenTheRestoreFails_DoesNotAnnounceARestoration()
    {
        var (svc, coord, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        var events = new List<string?>();
        svc.Switched += n => events.Add(n);
        await svc.SwitchToAsync("Draw");
        coord.RestoreResult = SettingsRestoreOutcome.SourceUnavailable;

        await svc.RestoreDefaultAsync();

        // "Draw" from the switch, and nothing after it — no "Restored saved settings" toast.
        Assert.Equal(new string?[] { "Draw" }, events);
    }

    [Fact]
    public async Task RestoreFailed_Event_FiresWithTheReason()
    {
        var (svc, coord, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        var failures = new List<SettingsRestoreStatus>();
        svc.RestoreFailed += s => failures.Add(s);
        await svc.SwitchToAsync("Draw");
        coord.RestoreResult = SettingsRestoreOutcome.Disconnected;

        await svc.RestoreDefaultAsync();

        Assert.Equal([SettingsRestoreStatus.Disconnected], failures);
    }

    [Fact]
    public async Task RestoreFailed_Event_StaysQuietOnASuccessfulRestore()
    {
        var (svc, _, store) = Make(PresetsDir);
        store.Existing.Add(Snapshot("Draw"));
        var failures = new List<SettingsRestoreStatus>();
        svc.RestoreFailed += s => failures.Add(s);
        await svc.SwitchToAsync("Draw");

        var ok = await svc.RestoreDefaultAsync();

        Assert.True(ok);
        Assert.Empty(failures);
    }
}
