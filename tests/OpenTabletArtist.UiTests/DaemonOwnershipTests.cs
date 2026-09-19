using System.Linq;
using Avalonia.Headless.XUnit;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// What OTA is allowed to do to a daemon depending on whose it is (#742).
///
/// Provenance is three-valued — ours, theirs, or unread — but every guard except the Stop/Restart
/// confirmation was written as <c>!IsForeignDaemon</c>, which matches both "ours" and "we couldn't
/// tell". So on a daemon OTA could not identify (elevated, or another user's) it behaved as though it
/// owned the thing: the data load disabled a stranger's third-party filters and wrote the result to the
/// daemon <b>and</b> to settings.json, inside a swallowed catch.
///
/// In the UI suite because the load path runs behind <c>Dispatcher.UIThread.VerifyAccess()</c>.
///
/// Nothing here may leave work running when the test ends. The headless suite shares one dispatcher, so
/// a stray timer or unawaited continuation from a finished test runs while another owns the test context
/// and the runner fails cleanup with "Cannot get KeyValueStorage on the idle test context" — which then
/// cascades into unrelated classes, intermittently. Two such leaks came out of writing these: a disposed
/// <c>AppSession</c> left its dispatcher timers running (fixed in <c>AppSession.Dispose</c>), and an
/// owned daemon triggers a fire-and-forget plugin install (short-circuited in the fixture below).
/// Assertions that need no dispatcher at all — the ownership flags themselves — live in the logic suite;
/// see <c>ForeignDaemonConfirmTests</c>.
/// </summary>
public class DaemonOwnershipTests
{
    private sealed class StubLifecycle : IDaemonLifecycleService
    {
        public string? ExpectedExePath() => null;
        public bool IsAppManaged(string? path) => false;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => null;
        public bool IsRunning() => false;
        public string? Launch() => null;
        public bool Stop(int processId) => true;
        public void StopAll() { }
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
    }

    private sealed class RecordingStore : ISettingsFileStore
    {
        public int Writes { get; private set; }
        public void Save(Settings settings, string path) => Writes++;
        public bool TrySave(Settings settings, string path) { Writes++; return true; }
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>A third-party filter OTA does not own — OpenTabletDriver's own noise reduction.</summary>
    private const string ForeignFilter = "OpenTabletDriver.Filters.Noise.NoiseReduction";

    private static Settings SettingsWithForeignFilter()
    {
        var profile = new Profile { Tablet = "T" };
        profile.Filters.Add(new PluginSettingStore(ForeignFilter) { Path = ForeignFilter, Enable = true });
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static bool ForeignFilterEnabled(Settings? s) =>
        s?.Profiles.First().Filters.First(f => f.Path == ForeignFilter).Enable ?? false;

    private static (AppSession session, FakeDaemonTransport daemon, RecordingStore store) Make(
        DaemonOwnership ownership)
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsWithForeignFilter(),
            AppInfo = new AppInfo
            {
                AppDataDirectory = "x",
                SettingsFile = "settings.json",
                // Empty on purpose. Left unset, AppInfo hands back a real default path, and the load's
                // fire-and-forget pressure-plugin install then runs on an owned daemon — unawaited
                // filesystem work that posts back to the dispatcher after the test has finished. These
                // tests are about ownership gating, not plugin install; an empty directory short-circuits it.
                PluginDirectory = "",
            },
        };
        var store = new RecordingStore();
        var session = new AppSession(daemon, new StubLifecycle(), store) { Ownership = ownership };
        return (session, daemon, store);
    }

    [AvaloniaFact]
    public async Task OnOurOwnDaemon_AnUnapprovedFilterIsDisabledAndWrittenBack()
    {
        var (session, daemon, store) = Make(DaemonOwnership.Owned);
        using var _s = session;

        await session.ReloadAsync();

        Assert.False(ForeignFilterEnabled(session.CurrentSettings));   // #465 still does its job
        Assert.NotEmpty(daemon.Applied);
        Assert.True(store.Writes > 0);
    }

    /// <summary>
    /// A cleanup that was never sent must not be written to disk (#803).
    ///
    /// This path called the daemon and the store directly, around the coordinator, and threw away what
    /// <c>SetSettingsAsync</c> returned. False means there is no transport -- the change never left the
    /// process -- and the next line persisted it anyway. Disk then held a repair the running daemon had
    /// never seen, which is the disagreement #734 exists to prevent, arrived at from the other side.
    ///
    /// The in-memory repair is unaffected either way, and that is the reason this was easy to miss:
    /// everything the user could see was correct.
    /// </summary>
    [AvaloniaFact]
    public async Task ACleanupTheDaemonNeverReceived_IsNotWrittenToDisk()
    {
        var (session, daemon, store) = Make(DaemonOwnership.Owned);
        using var _s = session;
        daemon.SetSettingsSucceeds = false;   // no transport: nothing is actually sent

        await session.ReloadAsync();

        Assert.Equal(0, store.Writes);
        Assert.False(ForeignFilterEnabled(session.CurrentSettings));   // the display is still repaired
    }

    [AvaloniaFact]
    public async Task OnSomeoneElsesDaemon_TheirFiltersAreLeftAlone()
    {
        var (session, daemon, store) = Make(DaemonOwnership.External);
        using var _s = session;

        await session.ReloadAsync();

        Assert.True(ForeignFilterEnabled(session.CurrentSettings));
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>
    /// The regression. "Not foreign" matched this state, so OTA disabled a stranger's filters and
    /// persisted the result — on the daemon it knew least about, and to a settings.json that may not
    /// even belong to this user.
    /// </summary>
    [AvaloniaFact]
    public async Task OnADaemonItCannotIdentify_OtaChangesNothing()
    {
        var (session, daemon, store) = Make(DaemonOwnership.Unknown);
        using var _s = session;

        await session.ReloadAsync();

        Assert.True(ForeignFilterEnabled(session.CurrentSettings));
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>A user-initiated save is different from an unasked rewrite: the artist asked for it, so
    /// it goes through whatever daemon is connected. Only the automatic paths require ownership.</summary>
    [AvaloniaFact]
    public async Task AUserInitiatedSave_StillWorksOnADaemonWeDoNotOwn()
    {
        var (session, daemon, store) = Make(DaemonOwnership.Unknown);
        using var _s = session;
        await session.ReloadAsync();

        var outcome = await session.ApplyAndSaveSettingsAsync(SettingsWithForeignFilter());

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.NotEmpty(daemon.Applied);
        Assert.True(store.Writes > 0);
    }

    /// <summary>...but even then it must not quietly disable filters that aren't ours.</summary>
    [AvaloniaFact]
    public async Task AUserInitiatedSave_DoesNotDisableFiltersOnADaemonWeDoNotOwn()
    {
        var (session, daemon, _) = Make(DaemonOwnership.Unknown);
        using var _s = session;
        await session.ReloadAsync();

        await session.ApplyAndSaveSettingsAsync(SettingsWithForeignFilter());

        Assert.True(ForeignFilterEnabled(daemon.Applied[^1]));
    }

    [AvaloniaFact]
    public async Task AUserInitiatedSaveOnOurOwnDaemon_StillDisablesUnapprovedFilters()
    {
        var (session, daemon, _) = Make(DaemonOwnership.Owned);
        using var _s = session;
        await session.ReloadAsync();

        await session.ApplyAndSaveSettingsAsync(SettingsWithForeignFilter());

        Assert.False(ForeignFilterEnabled(daemon.Applied[^1]));
    }
}
