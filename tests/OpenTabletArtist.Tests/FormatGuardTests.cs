using System.Threading.Tasks;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Nothing leaves this library carrying a null Absolute-mode area (#836).
///
/// The repair exists because OpenTabletDriver's own UX does
/// <c>p.AbsoluteModeSettings.Tablet.Width</c> in <c>MainForm.SaveSettings</c> and crashes on a null. It
/// used to run on the persisting path only, on the reasoning that the guard is about what gets
/// <em>written</em> and the other paths never write.
///
/// That reasoning was wrong. <c>MainForm.SyncSettings</c> pulls settings <b>from the daemon</b> on every
/// resync, so a null area applied live-only never touches our file and still reaches the UX — which then
/// crashes when the user saves. The file was never the only route.
/// </summary>
public class FormatGuardTests
{
    /// <summary>A profile as some app-side construction can leave it: no Absolute-mode settings at all.</summary>
    private static Settings WithNoArea() =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = "T", AbsoluteModeSettings = null } } };

    private static bool Repaired(Settings s) =>
        s.Profiles[0].AbsoluteModeSettings is { Tablet: not null, Display: not null };

    [Fact]
    public async Task ApplyAndSave_RepairsBeforeSending()
    {
        var (c, daemon, _) = Make();

        await c.ApplyAndSaveAsync(WithNoArea());

        Assert.True(Repaired(Assert.Single(daemon.Applied)));
    }

    /// <summary>
    /// Live-only sends to the daemon without writing, and the daemon is exactly how the UX gets it.
    /// </summary>
    [Fact]
    public async Task LiveOnly_RepairsBeforeSending()
    {
        var (c, daemon, _) = Make();

        await c.ApplyLiveOnlyAsync(WithNoArea());

        Assert.True(Repaired(Assert.Single(daemon.Applied)));
    }

    /// <summary>Per-app switching, same route to the same crash.</summary>
    [Fact]
    public async Task PerApp_RepairsBeforeSending()
    {
        var (c, daemon, _) = Make();

        await c.ApplyEphemeralAsync(WithNoArea());

        Assert.True(Repaired(Assert.Single(daemon.Applied)));
    }

    /// <summary>
    /// Restore reads the saved default off disk and sends it — so it can introduce content the daemon
    /// never had, from a file written by an older build or edited by hand.
    ///
    /// Not named in #836, which asked for "all three mutating paths". Included because it is the same
    /// defect by the same route, and leaving it would mean the contract still had to be stated per
    /// operation — the thing the issue is about.
    /// </summary>
    [Fact]
    public async Task RestoringTheSavedDefault_RepairsBeforeSending()
    {
        var (c, daemon, store) = Make();
        store.ToLoad = WithNoArea();

        var outcome = await c.RestoreDefaultAsync();

        Assert.Equal(SettingsRestoreStatus.Restored, outcome.Status);
        Assert.True(Repaired(Assert.Single(daemon.Applied)));
    }

    /// <summary>The caller's object is still never touched — repairing is not a licence to mutate it.</summary>
    [Fact]
    public async Task TheRepairHappensOnTheLibrarysCopy()
    {
        var (c, _, _) = Make();
        var mine = WithNoArea();

        await c.ApplyLiveOnlyAsync(mine);

        Assert.Null(mine.Profiles[0].AbsoluteModeSettings);
    }

    private sealed class LoadableStore : ISettingsFileStore
    {
        public Settings? ToLoad { get; set; }
        public void Save(Settings s, string path) { }
        public bool TrySave(Settings s, string path) => true;
        public bool TryLoad(string path, out Settings? settings)
        {
            settings = ToLoad;
            return ToLoad != null;
        }
    }

    private static (IOtdSettingsSession, FakeDaemonTransport, LoadableStore) Make()
    {
        var daemon = new FakeDaemonTransport();
        var store = new LoadableStore();
        var session = OtdSession.ForTesting(daemon, store, NullOtdLog.Instance,
            OtaSettingsPolicy.Instance, new FakeProcessLocator());
        return (session.OpenSettings(() => "A/settings.json", () => true, _ => { }), daemon, store);
    }
}
