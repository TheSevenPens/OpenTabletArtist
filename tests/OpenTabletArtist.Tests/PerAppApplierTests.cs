using System;
using System.IO;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The applier's side of per-app switching (#737): telling a missing snapshot apart from a failed apply,
/// and ending an override through the coordinator rather than by re-applying the baseline ephemerally.
/// That second part is what keeps a transient snapshot from becoming the editor's baseline — the
/// coordinator can only stop the background reload overwriting it if it knows an override is live.
/// </summary>
public class PerAppApplierTests : IDisposable
{
    private readonly string _dir;

    public PerAppApplierTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ota-applier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private sealed class FakeCoordinator : ISettingsCoordinator
    {
        public Settings? CurrentSettings { get; set; } = new();
        public int EphemeralCalls;
        public int ClearCalls;
        /// <summary>When set, the daemon call throws — a reachable daemon that refused the change.</summary>
        public Exception? ThrowOnEphemeral;
        public Exception? ThrowOnClear;

        public bool HasEphemeralOverride { get; private set; }

        public Task ApplyEphemeralAsync(Settings settings)
        {
            EphemeralCalls++;
            if (ThrowOnEphemeral != null) throw ThrowOnEphemeral;
            HasEphemeralOverride = true;
            return Task.CompletedTask;
        }

        public Task ClearEphemeralOverrideAsync()
        {
            ClearCalls++;
            if (ThrowOnClear != null) throw ThrowOnClear;
            HasEphemeralOverride = false;
            return Task.CompletedTask;
        }

        public Task<SettingsApplyOutcome> ApplyAndSaveSettingsAsync(Settings s) =>
            Task.FromResult(SettingsApplyOutcome.Saved);
        public Task<SettingsApplyOutcome> RetryPersistAsync() => Task.FromResult(SettingsApplyOutcome.NoChange);
        public Task ApplyLiveOnlyAsync(Settings s) => Task.CompletedTask;
        public Task<SettingsRestoreOutcome> RestoreDefaultAsync() =>
            Task.FromResult(SettingsRestoreOutcome.Restored);
    }

    private PerAppApplier Make(FakeCoordinator coord) =>
        new(coord, new SettingsFileStore(), () => _dir);

    private void WriteSnapshot(string name) =>
        new SettingsFileStore().Save(new Settings(), Path.Combine(_dir, name + ".json"));

    [Fact]
    public async Task AnExistingSnapshot_IsAppliedEphemerally()
    {
        var coord = new FakeCoordinator();
        WriteSnapshot("Painting");

        var result = await Make(coord).ApplySnapshotAsync("Painting");

        Assert.Equal(PerAppApplyResult.Applied, result);
        Assert.Equal(1, coord.EphemeralCalls);
        Assert.True(coord.HasEphemeralOverride);
    }

    [Fact]
    public async Task AMissingSnapshot_IsReportedAsMissing_AndTouchesTheDaemonNotAtAll()
    {
        var coord = new FakeCoordinator();

        var result = await Make(coord).ApplySnapshotAsync("Gone");

        Assert.Equal(PerAppApplyResult.SnapshotMissing, result);
        Assert.Equal(0, coord.EphemeralCalls);
    }

    [Fact]
    public async Task NoPresetDirectory_IsReportedAsMissing()
    {
        var coord = new FakeCoordinator();
        var applier = new PerAppApplier(coord, new SettingsFileStore(), () => null);

        Assert.Equal(PerAppApplyResult.SnapshotMissing, await applier.ApplySnapshotAsync("Painting"));
    }

    /// <summary>
    /// The regression: a daemon failure used to be swallowed and reported as success. Reporting it as
    /// "missing" would be just as wrong in the other direction — it would warn the artist about a
    /// deleted profile that is sitting right there.
    /// </summary>
    [Fact]
    public async Task ASnapshotTheDaemonRefuses_IsApplyFailed_NotMissing()
    {
        var coord = new FakeCoordinator { ThrowOnEphemeral = new InvalidOperationException("rpc down") };
        WriteSnapshot("Painting");

        var result = await Make(coord).ApplySnapshotAsync("Painting");

        Assert.Equal(PerAppApplyResult.ApplyFailed, result);
        Assert.Equal(1, coord.EphemeralCalls);   // it really was attempted
    }

    /// <summary>
    /// Returning to the default must go through the coordinator's own "the override is over" path, not
    /// re-apply the baseline as another ephemeral override. Re-applying ephemerally would leave the
    /// coordinator believing an override is still live, and it would keep refusing to refresh its
    /// baseline from the daemon forever after.
    /// </summary>
    [Fact]
    public async Task ApplyingTheDefault_EndsTheOverride()
    {
        var coord = new FakeCoordinator();
        WriteSnapshot("Painting");
        await Make(coord).ApplySnapshotAsync("Painting");
        Assert.True(coord.HasEphemeralOverride);

        var ok = await Make(coord).ApplyDefaultAsync();

        Assert.True(ok);
        Assert.Equal(1, coord.ClearCalls);
        Assert.False(coord.HasEphemeralOverride);
    }

    [Fact]
    public async Task ApplyingTheDefault_ReportsFailure_WhenTheDaemonRefuses()
    {
        var coord = new FakeCoordinator { ThrowOnClear = new InvalidOperationException("rpc down") };

        Assert.False(await Make(coord).ApplyDefaultAsync());
    }

    [Fact]
    public async Task ApplyingTheDefault_WithNoCurrentSettings_ReportsFailure()
    {
        // Nothing loaded yet — there is no baseline to return to, so claiming success would tell the
        // switcher it is on the user's default when nothing was applied at all.
        var coord = new FakeCoordinator { CurrentSettings = null };

        Assert.False(await Make(coord).ApplyDefaultAsync());
        Assert.Equal(0, coord.ClearCalls);
    }
}
