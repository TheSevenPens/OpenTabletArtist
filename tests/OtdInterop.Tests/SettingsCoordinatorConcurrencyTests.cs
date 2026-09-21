using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;
using OtdInterop;

namespace OtdInterop.Tests;

/// <summary>
/// What happens when settings operations overlap — #774, #775, #776, from the second follow-up review
/// of #731.
///
/// Every earlier coordinator test drove a daemon that answered immediately, so no test in the suite could
/// observe an interleaving even in principle. The defects those tests missed all live in the same place:
/// each operation reads shared state, awaits the daemon, and writes that state back, and nothing said
/// what happens when a second operation arrives in the middle.
///
/// These hold the daemon call open with a <see cref="TaskCompletionSource"/> rather than sleeping, so the
/// interleaving is exact and the test is not timing-dependent.
///
/// <c>SettingsCoordinator</c> is headless and thread-agnostic by design, so it is exercised directly here
/// rather than through <c>AppSession</c> and a dispatcher.
/// </summary>
public class SettingsCoordinatorConcurrencyTests
{
    /// <summary>
    /// Stateful on purpose: <c>TryLoad</c> returns whatever was last written, the way a real file does.
    /// A fake with a fixed load value would let "restore the saved default" restore something that is not
    /// on disk, which is precisely the agreement these tests exist to check.
    /// </summary>
    /// <summary>
    /// Stateful and keyed by path: <c>TryLoad</c> returns whatever was last written there, the way a real
    /// file does. Keyed rather than single-file because two daemons have two settings files, and "A's
    /// pending save never reached B's file" is not expressible against one (#787).
    /// </summary>
    private sealed class FakeStore : ISettingsFileStore
    {
        private readonly Dictionary<string, Settings> _files = new(StringComparer.OrdinalIgnoreCase);

        public bool SaveSucceeds { get; set; } = true;
        public List<string> Writes { get; } = new();

        /// <summary>What a restart would load from <paramref name="path"/>, or null if nothing is there.</summary>
        public Settings? OnDiskAt(string path) =>
            _files.TryGetValue(path, out var s) ? Clone(s) : null;

        /// <summary>The default path these tests use when only one daemon is involved.</summary>
        public Settings? OnDisk => OnDiskAt(DefaultPath);

        public void Seed(Settings settings, string path = DefaultPath) => _files[path] = Clone(settings);

        public void Save(Settings settings, string path) => TrySave(settings, path);

        public bool TrySave(Settings settings, string path)
        {
            if (!SaveSucceeds) return false;
            // Clone on write: the caller may keep mutating its object, and a real file would not change
            // underneath us when it does.
            _files[path] = Clone(settings);
            Writes.Add(Json(settings));   // content only; which file it went to is OnDiskAt's job
            return true;
        }

        public bool TryLoad(string path, out Settings? settings)
        {
            settings = OnDiskAt(path);
            return settings != null;
        }
    }

    private const string DefaultPath = "A/settings.json";

    private static Settings Clone(Settings s) =>
        JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(s))!;

    /// <summary>
    /// A close woken by an abandonment answers false, on its own (#893).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to answer true. The wait is released either by the last operation finishing or by an
    /// <c>Abandon</c> that gave up on it, and this took the same path for both — leaving the session's
    /// <c>settled &amp;&amp; !_tornDown</c> to repair the answer afterwards. So a question this object can
    /// answer about its own work depended on an ordering somewhere else, and that ordering was wrong
    /// twice (#891, #893). Each time, abandoned work was reported as having finished.
    /// </para>
    /// <para>
    /// Tested here rather than through the session on purpose: through the session it passes on the outer
    /// guard even when this answer is wrong, which is how it stayed wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CloseAsync_AnswersFalse_WhenWhatItWaitedOnWasAbandoned()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        _ = coordinator.ApplyAndSaveAsync(SettingsFor("Held", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = coordinator.CloseAsync(TimeSpan.FromMinutes(5));
        coordinator.Abandon();

        Assert.False(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.False(held.Task.IsCompleted);   // the work was never finished, only given up on
    }

    /// <summary>Work that actually finishes still answers true — the case the fix must not break.</summary>
    /// <remarks>
    /// True means the admitted work finished, not that it saved successfully; an operation can finish
    /// having failed to save, and that belongs to its save-state rather than to the shape of the close.
    /// </remarks>
    [Fact]
    public async Task CloseAsync_AnswersTrue_WhenTheWorkFinishes()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        var applying = coordinator.ApplyAndSaveAsync(SettingsFor("Finishes", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        var closing = coordinator.CloseAsync(TimeSpan.FromMinutes(5));
        held.TrySetResult(true);
        await applying.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(await closing.WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>And a close that arrives after the abandonment answers false from a standing start.</summary>
    [Fact]
    public async Task CloseAsync_AnswersFalse_WhenItArrivesAfterTheAbandonment()
    {
        var (coordinator, daemon, _, _) = Make();
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            sending.TrySetResult();
            return held.Task;
        };

        _ = coordinator.ApplyAndSaveAsync(SettingsFor("Held", locked: false));
        await sending.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        coordinator.Abandon();

        Assert.False(await coordinator.CloseAsync(TimeSpan.FromMinutes(5))
            .WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// An edit made somewhere else since this session last read is not overwritten (#491).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SetSettings</c> replaces the whole object and keeps no version, so an apply built on a stale
    /// read overwrites whatever OpenTabletDriver's own UX put there, in the daemon and on disk, with
    /// nothing to notice it by. The window is small — a focus reload and a 30 s poll bound it — but it was
    /// unbounded in the sense that mattered: nothing looked.
    /// </para>
    /// <para>
    /// Holding the change is the deliberate choice (#491). Both ways out lose an edit, so which one goes
    /// is the artist's decision rather than this library's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnApplyThatWouldOverwriteSomebodyElsesEdit_IsHeldInstead()
    {
        var (coordinator, daemon, store, states) = Make();

        // A baseline first: this session has to have read the daemon before it can notice a change. An
        // apply records what the daemon accepted, which is the same thing a load would have left.
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));
        states.Clear();

        // Someone else writes, and this session does not know.
        daemon.Settings = SettingsFor("TheirEdit", locked: false);

        Settings? sent = null;
        daemon.SetSettingsHandler = s =>
        {
            sent = s;
            return Task.FromResult(true);
        };

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, outcome.Status);
        Assert.Null(sent);                                   // nothing reached the daemon
        Assert.Equal("TheirEdit", Tablet(daemon.Settings));  // and theirs is still there
        Assert.Equal("Baseline", Tablet(store.OnDisk));      // nor did it reach the file
        Assert.Contains(SettingsSaveState.ChangedElsewhere, states);
    }

    /// <summary>Nobody else wrote, so the apply goes through exactly as before.</summary>
    /// <remarks>
    /// The case that must not regress: every apply now asks the daemon what it holds first, and if that
    /// question answered "changed" too easily, every ordinary edit would be refused.
    /// </remarks>
    [Fact]
    public async Task WithNobodyElseWriting_TheApplyGoesThrough()
    {
        var (coordinator, daemon, store, _) = Make();

        // With a baseline in place, so the comparison actually runs rather than being skipped for want
        // of anything to compare against.
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Equal("MyEdit", Tablet(daemon.Settings));
        Assert.Equal("MyEdit", Tablet(store.OnDisk));
    }

    /// <summary>
    /// A check that cannot be made holds the change too, and says which of the two happened (#905).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserted the opposite until the review of #904. Proceeding on an inconclusive read abandons
    /// the protection exactly where the state is least certain: "the daemon would not tell me what it
    /// holds" is not evidence that it holds what we last saw. The reported status is distinct from a
    /// confirmed conflict, because the artist's next move differs — there is nothing known to be
    /// waiting on the other side, and trying again shortly may be all it needs.
    /// </para>
    /// <para>
    /// The cost is real and was weighed: a daemon that answers writes but not reads now refuses edits it
    /// would previously have taken. Against that, the whole point of holding a change is that it is not
    /// written over somebody else's, and a read nobody answered cannot establish that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WhenTheDaemonCannotBeAsked_TheChangeIsHeldToo()
    {
        var (coordinator, daemon, store, states) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));
        states.Clear();

        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.CouldNotCheck, outcome.Status);
        Assert.Equal("Baseline", Tablet(daemon.Settings));
        Assert.Equal("Baseline", Tablet(store.OnDisk));
        Assert.Contains(SettingsSaveState.CouldNotCheck, states);
    }

    /// <summary>A daemon that answers the read with nothing has told us nothing either.</summary>
    [Fact]
    public async Task WhenTheDaemonAnswersWithNothing_TheChangeIsHeld()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));

        daemon.GetSettingsHandler = () => Task.FromResult<Settings?>(null);

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.CouldNotCheck, outcome.Status);
        Assert.Equal("Baseline", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A read that never answers is abandoned on its budget rather than holding the gate open (#905).
    /// </summary>
    /// <remarks>
    /// The budget exists because this runs inside the mutation gate: a read that hangs does not only
    /// delay its own apply, it holds every queued edit and persistence retry behind it. The wait is
    /// abandoned; the read itself is not cancellable and may still be out there, which is why nothing it
    /// returns afterwards is allowed to reach a decision this apply has already made.
    /// </remarks>
    [Fact]
    public async Task WhenTheDaemonNeverAnswersTheRead_TheChangeIsHeldOnTheBudget()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));

        var never = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () => never.Task;

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.CouldNotCheck, outcome.Status);
        Assert.Equal("Baseline", Tablet(daemon.Settings));
        Assert.False(never.Task.IsCompleted);   // the read was left running, not resolved by the wait

        never.TrySetResult(null);
    }

    /// <summary>Presenting the conflict writes the change over it (#906).</summary>
    /// <remarks>
    /// The other half of #491. Holding an edit is only defensible if the artist has a way to say "I have
    /// seen what is there and I still want mine".
    /// </remarks>
    [Fact]
    public async Task WithTheConflictPresented_TheChangeIsWrittenOverIt()
    {
        var (coordinator, daemon, store, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));

        daemon.Settings = SettingsFor("TheirEdit", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, held.Status);
        Assert.NotNull(held.Conflict);

        var overwritten = await coordinator.OverwriteAsync(
            SettingsFor("MyEdit", locked: false), held.Conflict!);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, overwritten.Status);
        Assert.Equal("MyEdit", Tablet(daemon.Settings));
        Assert.Equal("MyEdit", Tablet(store.OnDisk));
    }

    /// <summary>
    /// Applying the same edit again is not consent (#905, #906).
    /// </summary>
    /// <remarks>
    /// The distinction the whole token exists for. An artist who edits a value, sees it held, and edits
    /// again has not decided anything about somebody else's work — and an application that treated
    /// persistence as permission would discard that work on the second keystroke.
    /// </remarks>
    [Fact]
    public async Task ApplyingTheSameChangeAgain_IsStillHeld()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));
        daemon.Settings = SettingsFor("TheirEdit", locked: false);

        var first = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));
        var second = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, first.Status);
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, second.Status);
        Assert.Equal("TheirEdit", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A conflict authorises overwriting what it described, not whatever has arrived since (#906).
    /// </summary>
    /// <remarks>
    /// The token is not a permission slip. Between being told about a conflict and deciding about it, the
    /// artist may take a while — and a third change in that window is something nobody has seen, so it is
    /// reported rather than flattened. The refusal carries the new conflict, so the choice can be made
    /// again against what is actually there.
    /// </remarks>
    [Fact]
    public async Task IfTheDaemonMovesAgainBeforeTheOverwrite_ItIsRefusedWithTheNewConflict()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));

        daemon.Settings = SettingsFor("TheirEdit", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        // Somebody writes again while the artist is still deciding.
        daemon.Settings = SettingsFor("SomebodyElseAgain", locked: false);

        var refused = await coordinator.OverwriteAsync(
            SettingsFor("MyEdit", locked: false), held.Conflict!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, refused.Status);
        Assert.Equal("SomebodyElseAgain", Tablet(daemon.Settings));
        Assert.NotNull(refused.Conflict);
        Assert.NotEqual(held.Conflict, refused.Conflict);

        // And the new one authorises the write, because it describes what is actually there.
        var overwritten = await coordinator.OverwriteAsync(
            SettingsFor("MyEdit", locked: false), refused.Conflict!);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, overwritten.Status);
        Assert.Equal("MyEdit", Tablet(daemon.Settings));
    }

    /// <summary>Nothing is reported to consent to when the daemon never said what it holds.</summary>
    /// <remarks>
    /// <c>CouldNotCheck</c> is the absence of an observation, so there is no conflict to describe and
    /// none is handed out. A caller cannot authorise an overwrite of something nobody saw.
    /// </remarks>
    [Fact]
    public async Task AnUncheckedHold_CarriesNoConflictToAuthorise()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Baseline", locked: false));
        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");

        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("MyEdit", locked: false));

        Assert.Equal(SettingsApplyStatus.CouldNotCheck, held.Status);
        Assert.Null(held.Conflict);
    }

    /// <summary>
    /// A reload between the conflict and the decision does not turn the token into a free pass (#910).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The token check used to live inside the ordinary comparison, and only in its "changed" branch. That
    /// comparison's baseline advances on every reload — including the automatic ones that happen while an
    /// editor is deliberately holding a draft. So: conflict reported against B, daemon moves to C, a
    /// reload makes C the baseline, and the overwrite is classified "unchanged" and sent without anyone
    /// looking at the token. The artist's change went over a state they were never shown.
    /// </para>
    /// <para>
    /// An overwrite now asks its own question — is the daemon still holding what the artist looked at —
    /// which no reload can answer on their behalf.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AReloadBetweenTheConflictAndTheOverwrite_DoesNotAuthoriseIt()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("B", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, held.Status);

        // The world moves again, and an ordinary reload adopts it as the new baseline.
        daemon.Settings = SettingsFor("C", locked: false);
        await coordinator.ReloadFromDaemonAsync();

        var refused = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), held.Conflict!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, refused.Status);
        Assert.Equal("C", Tablet(daemon.Settings));
        Assert.NotNull(refused.Conflict);
    }

    /// <summary>
    /// A token from another session is not consent, however well its numbers match (#910).
    /// </summary>
    /// <remarks>
    /// Channel numbers are a per-session counter, so two sessions hand out the same small integers. A
    /// token carrying only a channel was therefore accepted by a session that never issued it, given
    /// settings that happened to match — which is a coincidence of counters rather than a decision
    /// anybody made.
    /// </remarks>
    [Fact]
    public async Task AConflictFromAnotherSession_IsNotConsentHere()
    {
        var elsewhere = Make();
        await elsewhere.coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));
        elsewhere.daemon.Settings = SettingsFor("Theirs", locked: false);
        var theirToken = (await elsewhere.coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false)))
            .Conflict;
        Assert.NotNull(theirToken);

        // A different session, holding the same settings, on a channel that happens to be numbered alike.
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));
        daemon.Settings = SettingsFor("Theirs", locked: false);

        var refused = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), theirToken!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, refused.Status);
        Assert.Equal("Theirs", Tablet(daemon.Settings));
    }

    /// <summary>A refusal hands back a token that does authorise the write, so the artist is not stuck.</summary>
    /// <remarks>
    /// The other side of refusing: each refusal describes what is actually there, so deciding again is a
    /// decision about the present rather than a loop.
    /// </remarks>
    [Fact]
    public async Task TheRefusalsOwnConflict_AuthorisesTheOverwrite()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("B", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));

        daemon.Settings = SettingsFor("C", locked: false);
        var refused = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), held.Conflict!);

        var done = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), refused.Conflict!);

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, done.Status);
        Assert.Equal("Mine", Tablet(daemon.Settings));
    }

    /// <summary>
    /// An overwrite whose daemon will not say what it holds is held, not written (#910).
    /// </summary>
    /// <remarks>
    /// Consent is to replacing something in particular. If nobody can see what is there, nobody can be
    /// said to have agreed to replace it — and a token is evidence of a past decision, not a standing
    /// permission to write blind.
    /// </remarks>
    [Fact]
    public async Task AnOverwriteAgainstADaemonThatWillNotAnswer_IsHeld()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("Theirs", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));

        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");

        var refused = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), held.Conflict!);

        Assert.Equal(SettingsApplyStatus.CouldNotCheck, refused.Status);
        Assert.Equal("Theirs", Tablet(daemon.Settings));
        Assert.Null(refused.Conflict);
    }

    /// <summary>
    /// A hold does not outlive the daemon it was taken against (#910).
    /// </summary>
    /// <remarks>
    /// The pin exists so a held draft is compared against what it was held against rather than whatever a
    /// reload has since learned. Left standing across a daemon change it becomes the opposite: the new
    /// daemon's settings are compared against the old one's, never match, and every apply is held —
    /// permanently, because neither release is reachable. No write can succeed to clear it, and the
    /// editor has already given its draft up, so it will never accept anything either.
    /// </remarks>
    [Fact]
    public async Task AHoldDoesNotSurviveTheDaemonItWasTakenAgainst()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("TheirEdit", locked: false);
        Assert.Equal(
            SettingsApplyStatus.ChangedElsewhere,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false))).Status);

        // A different daemon answers, and this session learns what it holds.
        SwitchDaemon(coordinator, daemon);
        daemon.Settings = SettingsFor("NewDaemon", locked: false);
        await coordinator.ReloadFromDaemonAsync();

        // An ordinary edit on the new daemon is ordinary. It is not still answering for the old one.
        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("OnTheNewOne", locked: false));

        // Reached the daemon is the whole assertion. Whether it also reached disk depends on this
        // session having learned the new daemon's settings path, which is a different question and not
        // one a stale hold has any business answering.
        Assert.True(outcome.ChangedTheDaemon, $"the apply was held: {outcome.Status}");
        Assert.Equal("OnTheNewOne", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A reload landing during the check does not become the thing the hold protects (#910).
    /// </summary>
    /// <remarks>
    /// The hold used to be recorded from whatever the baseline was when the result came back, rather than
    /// from the state the comparison had actually been made against. A reload arriving while the daemon
    /// was being read therefore became the protected state — so the next ordinary submission of the same
    /// draft matched it, and wrote.
    /// </remarks>
    [Fact]
    public async Task AReloadDuringTheCheck_DoesNotBecomeWhatTheHoldProtects()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        // Somebody else has already written B; this session does not know yet.
        daemon.Settings = SettingsFor("B", locked: false);

        var reading = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.GetSettingsHandler = () =>
        {
            daemon.GetSettingsHandler = null;   // only the first read is held
            started.TrySetResult();
            return reading.Task;
        };

        var applying = coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        await started.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);

        // A reload lands while the check is still out, and adopts B as the ordinary baseline.
        await coordinator.ReloadFromDaemonAsync();

        reading.SetResult(SettingsFor("B", locked: false));

        var first = await applying;
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, first.Status);
        Assert.NotNull(first.Held);

        // The same draft again, presenting the hold it came back with. It was held against A, and the
        // hold must still say A rather than the B the reload adopted while the read was out.
        var second = await coordinator.ResubmitAsync(SettingsFor("Mine", locked: false), first.Held!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, second.Status);
        Assert.Equal("B", Tablet(daemon.Settings));
    }

    /// <summary>
    /// An overwrite that was authorised but did not land leaves the change held (#910).
    /// </summary>
    /// <remarks>
    /// Authorising a write is not the write happening. The release used to sit in the authorisation step,
    /// so a send that came back false — no transport — cleared the hold anyway, and the next ordinary
    /// apply sailed through and overwrote the edit the artist had never agreed to replace.
    /// </remarks>
    [Fact]
    public async Task AnAuthorisedOverwriteThatDoesNotLand_LeavesTheChangeHeld()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("B", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        Assert.NotNull(held.Conflict);
        Assert.NotNull(held.Held);

        // An ordinary reload learns B. Without this the hold's absence would not show: the ordinary
        // baseline would still be A, so the resubmission below would meet a conflict either way and the
        // test would pass while proving nothing. This is what makes the release observable.
        await coordinator.ReloadFromDaemonAsync();

        // Authorised, and the send fails.
        daemon.SetSettingsHandler = _ => Task.FromResult(false);
        var failed = await coordinator.OverwriteAsync(SettingsFor("Mine", locked: false), held.Conflict!);
        Assert.NotEqual(SettingsApplyStatus.AppliedAndSaved, failed.Status);

        // Sending works again, and the artist submits their draft again, still holding it: nothing they
        // did resolved it, so the hold goes with it.
        daemon.SetSettingsHandler = null;
        var afterwards = await coordinator.ResubmitAsync(SettingsFor("Mine", locked: false), held.Held!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, afterwards.Status);
        Assert.Equal("B", Tablet(daemon.Settings));
    }

    /// <summary>
    /// Accepting a snapshot that is no longer current does not release the hold (#910).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Acceptance used to take no argument and mean "whatever you hold now". An editor can be showing a
    /// snapshot while a later read advances this session underneath it — so a blind release turned a
    /// draft built on the older snapshot into permission to overwrite the newer one, which is the token
    /// bug one level up.
    /// </para>
    /// <para>
    /// Refusing is the safe direction. What the caller is looking at is out of date, and choosing again
    /// against what is actually there is theirs to do, not this session's to do for them by substituting
    /// the latest state silently.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AcceptingASnapshotThatIsNoLongerCurrent_LeavesTheChangeHeld()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        // What an editor is showing.
        var shown = coordinator.GetCurrent()!.Stamp;

        daemon.Settings = SettingsFor("Theirs", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, held.Status);
        Assert.NotNull(held.Held);

        // The session moves on while that editor is still showing the older snapshot.
        daemon.Settings = SettingsFor("TheirsAgain", locked: false);
        await coordinator.ReloadFromDaemonAsync();

        Assert.False(coordinator.AcceptCurrentState(shown), "a stale acceptance should be refused");

        // Refused, so the caller keeps its hold and the draft cannot go over what nobody agreed to.
        var afterwards = await coordinator.ResubmitAsync(SettingsFor("Mine", locked: false), held.Held!);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, afterwards.Status);
        Assert.Equal("TheirsAgain", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A publication is captured whole: the stamp that comes back names the settings that come with it
    /// (#910).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The settings and the revision used to be two fields, assigned one after the other and read the
    /// same way. Nothing about that is visibly wrong until you ask what a reader between the two writes
    /// sees, or what a publication landing between the two reads does — either way, one publication's
    /// settings come back under another's stamp. A caller that accepts that stamp is agreeing to
    /// something it was never shown, which is the whole thing the stamp was added to prevent.
    /// </para>
    /// <para>
    /// Checked by making each publication identifiable: the settings carry the version they were
    /// published at, so a mismatched pair is visible in the pair itself rather than inferred from
    /// timing. No thread and no sleep — what is being established is that capture is coherent, and
    /// coherence is a property of a single read.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryPublication_ComesBackAsTheSettingsAndStampThatBelongTogether()
    {
        var (coordinator, _, _, _) = Make();

        for (var i = 0; i < 5; i++)
        {
            await coordinator.ApplyAndSaveAsync(SettingsFor($"rev{i}", locked: false));

            var published = coordinator.GetCurrent();
            Assert.NotNull(published);

            // The pair names itself: these settings were published at this revision, or they were not.
            Assert.Equal($"rev{i}", Tablet(published!.Settings));
            Assert.Equal(coordinator.GetCurrent()!.Stamp, published.Stamp);
        }
    }

    /// <summary>
    /// A stamp handed out before a daemon switch does not compare equal to one handed out after it
    /// (#910).
    /// </summary>
    /// <remarks>
    /// The session generation is half of a stamp. While it was added when a stamp was asked for rather
    /// than when one was published, the published stamp silently started reading as the new session's
    /// while still naming the old session's revision — so a snapshot taken before a switch could be
    /// accepted afterwards, against a daemon it had never described.
    /// </remarks>
    [Fact]
    public async Task AStampFromBeforeADaemonSwitch_IsNotTheSameAsOneFromAfterIt()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        var before = coordinator.GetCurrent()!.Stamp;

        SwitchDaemon(coordinator, daemon);

        Assert.NotEqual(before, coordinator.GetCurrent()!.Stamp);
        Assert.False(coordinator.AcceptCurrentState(before),
            "a snapshot from the previous daemon was accepted against this one");
    }

    /// <summary>
    /// A stamp naming a version this session has not reached is refused too (#910).
    /// </summary>
    /// <remarks>
    /// The test used to be "not superseded by the current stamp", which is loose in one direction: a
    /// stamp ahead of this session is superseded by nothing, so it passed. Whatever produced such a
    /// stamp — a caller that built one, a snapshot from somewhere else — this session cannot vouch for
    /// it, and "I am looking at what you are publishing" is an equality rather than an inequality.
    /// </remarks>
    [Fact]
    public async Task AcceptingAStampThisSessionHasNotReached_IsRefused()
    {
        var (coordinator, _, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        var current = coordinator.GetCurrent()!.Stamp;
        var ahead = current with { Version = current.Version + 1 };

        Assert.False(coordinator.AcceptCurrentState(ahead), "a stamp from nowhere should be refused");
        Assert.True(coordinator.AcceptCurrentState(current), "and the real one still accepted");
    }

    /// <summary>Accepting what is actually current is answered yes, and the draft then lands.</summary>
    /// <remarks>
    /// The other direction, without which refusing would simply be a way of never letting the artist work
    /// again. Since #906 the release itself is the caller dropping its hold rather than anything this
    /// session clears — so what is checked here is that acceptance answers yes, and that a submission
    /// made without a hold is weighed against the ordinary baseline and lands.
    /// </remarks>
    [Fact]
    public async Task AcceptingTheCurrentSnapshot_ReleasesTheHold()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("Theirs", locked: false);
        await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));

        // The artist reloads, sees what is there, and takes it.
        await coordinator.ReloadFromDaemonAsync();
        Assert.True(coordinator.AcceptCurrentState(coordinator.GetCurrent()!.Stamp));

        var afterwards = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));

        Assert.True(afterwards.ChangedTheDaemon, $"the edit was held: {afterwards.Status}");
        Assert.Equal("Mine", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A hold from another session is refused, not quietly downgraded to an ordinary apply (#906).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dangerous part is what the fallback looked like. An unusable hold used to be ignored, which
    /// fell back to this session's own baseline — and that reads as the conservative choice until you
    /// notice the baseline can match the daemon exactly. Then the ordinary check passes, and a draft
    /// belonging to a different session is written into this one.
    /// </para>
    /// <para>
    /// Refusing is not a judgement about the other session. It is this one saying it cannot speak for an
    /// observation it never made.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AHoldFromAnotherSession_IsRefusedRatherThanIgnored()
    {
        // Somewhere else, a draft is held and given a hold.
        var elsewhere = Make();
        await elsewhere.coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));
        elsewhere.daemon.Settings = SettingsFor("Theirs", locked: false);
        var theirHold = (await elsewhere.coordinator.ApplyAndSaveAsync(SettingsFor("Old", locked: false)))
            .Held;
        Assert.NotNull(theirHold);

        // Here, everything agrees: the baseline is what the daemon holds, so an ordinary apply would go
        // straight through. That is exactly what made the old fallback dangerous.
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Ours", locked: false));

        var refused = await coordinator.ResubmitAsync(SettingsFor("Old", locked: false), theirHold!);

        Assert.Equal(SettingsApplyStatus.HoldNotApplicable, refused.Status);
        Assert.Equal("Ours", Tablet(daemon.Settings));
    }

    /// <summary>
    /// And so is one taken on a connection that has since been replaced (#906).
    /// </summary>
    /// <remarks>
    /// The same hole by the other route, and the more reachable of the two: one session, one editor, a
    /// reconnect in between. The hold names an incarnation that is gone, so the state it describes is
    /// not a state this connection was ever compared with.
    /// </remarks>
    [Fact]
    public async Task AHoldTakenOnAConnectionThatHasBeenReplaced_IsRefused()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("Theirs", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        Assert.NotNull(held.Held);

        // The connection is replaced, and the new one happens to hold what this session last read.
        SwitchDaemon(coordinator, daemon);
        daemon.Settings = SettingsFor("A", locked: false);
        await coordinator.ReloadFromDaemonAsync();

        var refused = await coordinator.ResubmitAsync(SettingsFor("Mine", locked: false), held.Held!);

        Assert.Equal(SettingsApplyStatus.HoldNotApplicable, refused.Status);
        Assert.Equal("A", Tablet(daemon.Settings));
    }

    /// <summary>
    /// The control: after that reconnect an ordinary edit still lands (#906).
    /// </summary>
    /// <remarks>
    /// Without this the refusal above would be satisfied by a session that had simply stopped writing,
    /// and the two tests together would say nothing about where the line is.
    /// </remarks>
    [Fact]
    public async Task AfterAReconnect_AnOrdinaryEditStillLands()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        daemon.Settings = SettingsFor("Theirs", locked: false);
        await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));

        SwitchDaemon(coordinator, daemon);
        daemon.Settings = SettingsFor("A", locked: false);
        await coordinator.ReloadFromDaemonAsync();

        var landed = await coordinator.ApplyAndSaveAsync(SettingsFor("Fresh", locked: false));

        Assert.True(landed.ChangedTheDaemon, $"a fresh edit was refused: {landed.Status}");
        Assert.Equal("Fresh", Tablet(daemon.Settings));
    }

    /// <summary>
    /// A draft is weighed against its own expectation, which is not the same as "never easier" (#906).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant I first wrote down for a hold was that it can only ever make a write harder. That is
    /// wrong, and this is the sequence that shows it: the daemon holds A, moves to B, and comes back to
    /// exactly A. An ordinary submission compares A against the reloaded baseline B and holds; the held
    /// draft, carrying its expectation of A, compares A against A and writes.
    /// </para>
    /// <para>
    /// Which is correct, and worth pinning precisely because it contradicts the tidier claim. The draft
    /// was built on A and the daemon is holding A, so nothing the artist has not seen is being replaced.
    /// The invariant is <b>compare this draft against its own unchanged expectation</b> — a hold is
    /// neither a weakening nor a strengthening of the check, but a statement of whose state the check is
    /// about. Comparing state is all this can ever do; it does not detect every edit in between.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADaemonThatReturnsToWhatTheDraftExpected_TakesTheDraft()
    {
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: false));

        // What the daemon actually ended up holding, which is the request after policy edited it. A
        // freshly built "A" is not the same bytes, and putting that back would be testing the
        // normalisation rather than the hold.
        var actuallyA = Clone(daemon.Settings!);

        // Somebody else writes B, and this session's draft is held against A.
        daemon.Settings = SettingsFor("B", locked: false);
        var held = await coordinator.ApplyAndSaveAsync(SettingsFor("Mine", locked: false));
        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, held.Status);

        // A reload adopts B as the ordinary baseline, and then they undo themselves.
        await coordinator.ReloadFromDaemonAsync();
        daemon.Settings = actuallyA;

        var resubmitted = await coordinator.ResubmitAsync(SettingsFor("Mine", locked: false), held.Held!);

        Assert.True(resubmitted.ChangedTheDaemon, $"the draft was held against its own state: {resubmitted.Status}");
        Assert.Equal("Mine", Tablet(daemon.Settings));
    }

    /// <summary>Nor does a hold, which carries somebody's settings for the same reasons (#906).</summary>
    /// <remarks>
    /// The same question asked of the other opaque value. It is worth asking twice rather than assuming
    /// the answer carries across: these are separate types, and the compiler's generated
    /// <c>PrintMembers</c> is decided per type by what that type happens to expose.
    /// </remarks>
    [Fact]
    public void AHoldDoesNotPrintWhatItHolds()
    {
        var hold = new SettingsHold(Guid.NewGuid(), 7, "{\"secret\":\"settings\"}");

        var printed = hold.ToString();

        Assert.DoesNotContain("secret", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("7", printed, StringComparison.Ordinal);
    }

    /// <summary>The token prints nothing about the settings it describes (#910).</summary>
    /// <remarks>
    /// Opacity here means a caller cannot depend on the representation through the public API — it is not
    /// secrecy from a debugger. But a record's generated <c>ToString</c> prints its properties, and these
    /// ones carry a serialized copy of somebody's settings, so it is worth knowing which side of that
    /// line the compiler put us on. It prints neither.
    /// </remarks>
    [Fact]
    public void AConflictDoesNotPrintWhatItHolds()
    {
        var conflict = new SettingsConflict(Guid.NewGuid(), 7, "{\"secret\":\"settings\"}");

        var printed = conflict.ToString();

        Assert.DoesNotContain("secret", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("7", printed, StringComparison.Ordinal);
    }

    private static string Json(Settings? s) => JsonConvert.SerializeObject(s);

    private static Settings SettingsFor(string tablet, bool locked) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private static string Tablet(Settings? s) => s?.Profiles[0].Tablet ?? "";

    /// <summary>Mutable so a test can move the destination the way a reconnect to another daemon does.</summary>
    private sealed class PathHolder { public string Value { get; set; } = DefaultPath; }

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, FakeStore store,
        List<SettingsSaveState> states) Make() => Make(new PathHolder());

    private static (SettingsCoordinator coordinator, FakeDaemonTransport daemon, FakeStore store,
        List<SettingsSaveState> states) Make(PathHolder path)
    {
        var daemon = new FakeDaemonTransport();
        var store = new FakeStore();
        var states = new List<SettingsSaveState>();
        var coordinator = new SettingsCoordinator(
            daemon, store,
            isOwnedDaemon: () => true,
            onSaveState: states.Add,
            log: NullOtdLog.Instance,
            policy: NoPolicy.Instance);

        // The connection starts identified, which is the ordinary state and what every test written
        // before #828 assumed. A test that wants the window BEFORE identification reconnects and does not
        // call Identify.
        Identify(coordinator, daemon, path);

        return (coordinator, daemon, store, states);
    }

    /// <summary>
    /// Tells the coordinator where the daemon on the current channel keeps its settings.
    /// </summary>
    /// <remarks>
    /// What <see cref="OtdSession"/> does for itself once it has asked the daemon's <c>AppInfo</c>. A
    /// coordinator built directly, as these tests build it, has no session to do that — so a test that
    /// reconnects and then expects a save to land has to say that the new connection was identified,
    /// because otherwise it was not.
    /// </remarks>
    private static void Identify(SettingsCoordinator coordinator, FakeDaemonTransport daemon,
        PathHolder path) => coordinator.LearnDestination(path.Value, daemon.Incarnation);

    /// <summary>
    /// A daemon change as it actually happens: a new channel, and then the reset for it.
    /// </summary>
    /// <remarks>
    /// These tests used to reset without moving the channel, which nothing real does — one pipe
    /// connection is one daemon process, so a different daemon always means a different channel. It
    /// stopped being a harmless simplification once the reset had to tell a pending write the NEW daemon
    /// accepted from one belonging to the daemon that has gone: with the channel left still, every
    /// pending write looked like the new daemon's.
    /// </remarks>
    private static void SwitchDaemon(SettingsCoordinator coordinator, FakeDaemonTransport daemon)
    {
        daemon.ReconnectSilently();
        coordinator.ResetForNewDaemon(daemon.Incarnation);
    }

    /// <summary>A daemon call that does not answer until the test says so.</summary>
    private static TaskCompletionSource<bool> HoldNextSetSettings(FakeDaemonTransport daemon,
        Action<Settings>? onSent = null)
    {
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var used = false;
        daemon.SetSettingsHandler = sent =>
        {
            // Later calls answer immediately, and honour whatever the test has since set.
            if (used) return Task.FromResult(daemon.SetSettingsSucceeds);
            used = true;
            onSent?.Invoke(sent);
            return hold.Task;
        };
        return hold;
    }

    // --- #774: the revision an apply owns -------------------------------------------------

    [Fact]
    public async Task AnEditDuringAnInFlightApply_IsNotWhatGetsPersisted()
    {
        var (coordinator, daemon, store, _) = Make();
        // One object, edited in place — how the tablet editor actually works.
        var shared = SettingsFor("Tablet", locked: true);

        string? sent = null;
        var hold = HoldNextSetSettings(daemon, s => sent = Json(s));

        var apply = coordinator.ApplyAndSaveAsync(shared);
        shared.LockUsableAreaDisplay = false;   // a second edit lands while the RPC is pending
        hold.SetResult(true);                   // the daemon accepts the FIRST revision

        var outcome = await apply;

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
        // The stronger claim, and the one that generalises: what went to the daemon and what went to disk
        // are the same revision, whatever happened to the caller's object in between.
        Assert.Equal(sent, Assert.Single(store.Writes));
    }

    [Fact]
    public async Task AnEditDuringAnInFlightApply_IsNotWhatTheRetryPersists()
    {
        var (coordinator, daemon, store, _) = Make();
        var shared = SettingsFor("Tablet", locked: true);
        store.SaveSucceeds = false;

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(shared);
        shared.LockUsableAreaDisplay = false;
        hold.SetResult(true);

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved, (await apply).Status);
        Assert.True(coordinator.HasUnsavedChange);

        // The disk comes back, and the retry writes the revision the daemon accepted — not the edit that
        // arrived while it was being applied.
        store.SaveSucceeds = true;
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await coordinator.RetryPersistAsync()).Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
    }

    [Fact]
    public async Task AnEditTheDaemonRefused_IsNeverPersisted()
    {
        var (coordinator, daemon, store, _) = Make();
        var shared = SettingsFor("Tablet", locked: true);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(shared);

        // The second edit is rejected outright, so it must not reach disk by any route.
        shared.LockUsableAreaDisplay = false;
        daemon.SetSettingsSucceeds = false;
        // Started, not awaited: operations are serialized now, so awaiting it here would wait on the
        // apply that this test has not released yet.
        var second = coordinator.ApplyAndSaveAsync(shared);
        hold.SetResult(true);
        await apply;

        Assert.Equal(SettingsApplyStatus.Disconnected, (await second).Status);
        Assert.True(store.OnDisk!.LockUsableAreaDisplay);
    }

    // --- #775: apply and restore have an order --------------------------------------------

    [Fact]
    public async Task ARestoreDuringAnInFlightApply_LeavesTheDaemonAndTheFileAgreeing()
    {
        var (coordinator, daemon, store, _) = Make();
        store.Seed(SettingsFor("Saved", locked: false));

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        var restore = coordinator.RestoreDefaultAsync();   // asked for while the apply is still out
        hold.SetResult(true);

        await apply;
        Assert.True((await restore).IsRestored);

        // The property that has to hold however the two are ordered: what the tablet is running and what
        // a restart would load are the same thing.
        Assert.Equal(Tablet(store.OnDisk), Tablet(daemon.Settings));
        Assert.False(coordinator.HasUnsavedChange);
    }

    /// <summary>
    /// The discriminating case. With the apply's disk write failing, the apply leaves a pending save
    /// behind it — and #764's fix (discarding it on restore) ran before the apply had set it.
    /// </summary>
    [Fact]
    public async Task ARestoreDuringAnInFlightApply_DoesNotLeaveTheDiscardedEditPendingOnDisk()
    {
        var (coordinator, daemon, store, _) = Make();
        store.Seed(SettingsFor("Saved", locked: false));
        store.SaveSucceeds = false;

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        var restore = coordinator.RestoreDefaultAsync();
        hold.SetResult(true);

        await apply;
        Assert.True((await restore).IsRestored);

        // Nothing is outstanding: the edit was discarded by the restore, so no later retry may write it.
        Assert.False(coordinator.HasUnsavedChange);

        store.SaveSucceeds = true;
        await coordinator.RetryPendingPersistAsync();
        Assert.Equal("Saved", Tablet(store.OnDisk));
    }

    // --- #776: the save chip after a restore ----------------------------------------------

    [Fact]
    public async Task ASuccessfulRestore_ClearsTheFailedSaveState()
    {
        var (coordinator, _, store, states) = Make();
        store.Seed(SettingsFor("Saved", locked: false));
        store.SaveSucceeds = false;

        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true))).Status);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        Assert.True((await coordinator.RestoreDefaultAsync()).IsRestored);

        // The chip said "Couldn't save — your change is live but won't survive a restart" about a change
        // the user had just deliberately thrown away.
        Assert.False(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.None, states[^1]);
    }

    [Fact]
    public async Task AFailedRestore_LeavesTheFailureStanding()
    {
        var (coordinator, _, store, states) = Make();
        store.SaveSucceeds = false;   // nothing on disk to restore from, either

        await coordinator.ApplyAndSaveAsync(SettingsFor("Edited", locked: true));
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        Assert.Equal(SettingsRestoreStatus.SourceUnavailable,
            (await coordinator.RestoreDefaultAsync()).Status);

        // The change really is still live and still unsaved. Clearing the warning here would be the
        // opposite lie to the one #776 fixes.
        Assert.True(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);
    }

    // --- #787: a session belongs to one daemon ---------------------------------------------

    private const string OtherPath = "B/settings.json";

    /// <summary>
    /// The case that matters. A save fails against daemon A, the user switches to B — which has its own
    /// application data directory — and the retry must not redirect A's settings into B's file. The
    /// destination is resolved at retry time, so without binding, it follows whichever daemon is
    /// connected now.
    /// </summary>
    [Fact]
    public async Task APendingSaveIsNotWrittenToADifferentDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, _, store, _) = Make(path);
        store.Seed(SettingsFor("B's own settings", locked: false), OtherPath);
        store.SaveSucceeds = false;

        // Applied against A, but the disk refused it.
        Assert.Equal(SettingsApplyStatus.AppliedNotSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true))).Status);
        Assert.True(coordinator.HasUnsavedChange);

        // The user stops A and starts B. The disk is writable again — the earlier failure was A's.
        path.Value = OtherPath;
        store.SaveSucceeds = true;

        await coordinator.RetryPendingPersistAsync();

        Assert.Equal("B's own settings", Tablet(store.OnDiskAt(OtherPath)));
        Assert.False(coordinator.HasUnsavedChange);   // dropped, not carried to yet another daemon
    }

    [Fact]
    public async Task APendingSaveStillRetriesAgainstItsOwnFile()
    {
        // The guard must not break the thing the retry exists for: a momentarily locked file, same daemon.
        var path = new PathHolder();
        var (coordinator, _, store, _) = Make(path);
        store.SaveSucceeds = false;

        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);

        store.SaveSucceeds = true;
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.RetryPendingPersistAsync()).Status);
        Assert.Equal("A's edit", Tablet(store.OnDisk));
    }

    /// <summary>
    /// The second manifestation. An ephemeral override is a fact about one daemon; while it is set, the
    /// load path deliberately does not adopt what the daemon reports. Carried across a switch, it stops
    /// the new daemon's settings ever being read, so OTA edits and offers to persist the old one's.
    /// </summary>
    [Fact]
    public async Task AnOverrideDoesNotSurviveADaemonChange()
    {
        var (coordinator, daemon, _, _) = Make();
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A's per-app snapshot", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        SwitchDaemon(coordinator, daemon);

        Assert.False(coordinator.HasEphemeralOverride);
    }

    [Fact]
    public async Task ResetForNewDaemon_DropsEverythingBoundToTheOldOne()
    {
        var (coordinator, daemon, store, states) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        SwitchDaemon(coordinator, daemon);

        Assert.False(coordinator.HasUnsavedChange);

        // The reset itself tells the host NOTHING. Announcing is a call into host code, and host code can
        // reenter -- bring up another daemon, whose transition then commits while this one is half-done.
        // So the state change finishes first and the caller announces afterwards.
        Assert.Equal(SettingsSaveState.Failed, states[^1]);

        coordinator.AnnounceDiscardedChange();

        // The chip was describing A's unsaved change; it is not the new daemon's problem.
        Assert.Equal(SettingsSaveState.None, states[^1]);
    }

    /// <summary>
    /// The reset reports whether anything the user would notice was lost, so the caller can tell them
    /// (#787). Everything else it drops is bookkeeping they never saw; a pending write is an edit they
    /// made, and a save chip going quiet is not an explanation for losing it.
    /// </summary>
    [Fact]
    public async Task ResetForNewDaemon_SaysWhenAnEditWasThrownAway()
    {
        var (coordinator, daemon, store, _) = Make();
        store.SaveSucceeds = false;
        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.True(coordinator.HasUnsavedChange);

        daemon.ReconnectSilently();
        Assert.True(coordinator.ResetForNewDaemon(daemon.Incarnation));
    }

    [Fact]
    public async Task ResetForNewDaemon_SaysNothingWhenThereWasNothingToLose()
    {
        // A switch with no pending edit is routine. Announcing it would train the user to dismiss the
        // notice that matters.
        var (coordinator, daemon, _, _) = Make();
        await coordinator.ApplyAndSaveAsync(SettingsFor("Saved fine", locked: true));
        Assert.False(coordinator.HasUnsavedChange);

        daemon.ReconnectSilently();
        Assert.False(coordinator.ResetForNewDaemon(daemon.Incarnation));
    }

    /// <summary>
    /// The no-op guard compares against the last thing written to disk. Left over from A, it could skip
    /// an apply that B has never seen — the settings match what A's file held, not what B is running.
    /// </summary>
    [Fact]
    public async Task AfterADaemonChange_AnIdenticalApplyIsNotSkipped()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);
        var edit = SettingsFor("Same", locked: true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, (await coordinator.ApplyAndSaveAsync(edit)).Status);

        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        daemon.Applied.Clear();

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true));

        Assert.NotEqual(SettingsApplyStatus.NoChange, outcome.Status);
        Assert.Single(daemon.Applied);   // it actually reached the new daemon
    }
    // --- #807 Phase 5: the baseline is the library's to record -------------------------------

    /// <summary>
    /// While a transient override is running, re-applying what the daemon held before it is not skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The no-op guard's subject is "would this change anything", and during an override the daemon is
    /// running something else entirely — so the settings it held before are not what it has now, and
    /// sending them is a real change. Skipping it would leave the daemon on the override with the editor
    /// believing it had been put back (#737).
    /// </para>
    /// <para>
    /// It follows from the baseline being what the daemon <em>accepted</em>: during an override that is
    /// the snapshot, so the user's own settings no longer match it and the guard cannot fire. The
    /// host-driven version needed a special case to reach the same answer, because it recorded what this
    /// session was publishing, which during an override is exactly what the daemon is not running.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DuringAnOverride_ReapplyingWhatTheDaemonHeldBefore_IsStillSent()
    {
        var (coordinator, daemon, _, _) = Make();

        var original = SettingsFor("A's own", locked: true);
        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(original)).Status);

        // The daemon is now running something else on this session's behalf.
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A per-app snapshot", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        daemon.Applied.Clear();
        var again = await coordinator.ApplyAndSaveAsync(SettingsFor("A's own", locked: true));

        // Not NoChange: the daemon does not hold these, whatever this session last published.
        Assert.NotEqual(SettingsApplyStatus.NoChange, again.Status);
        Assert.Single(daemon.Applied);
    }

    /// <summary>
    /// An accepted apply becomes the baseline, so repeating it is recognised as changing nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard needs a baseline, and until now the only thing that set one was the host calling
    /// <c>RecordLoadedBaseline</c> after its load. So the coordinator on its own could apply the same
    /// settings for ever and send every one of them; whether the guard worked depended on a host having
    /// done something unrelated first.
    /// </para>
    /// <para>
    /// Recorded where the daemon accepts instead, which is the only moment this session knows what the
    /// daemon has. Removing that recording makes the second apply a real send, which is what this
    /// detects.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAcceptedApply_BecomesTheBaselineForTheNextOne()
    {
        var (coordinator, daemon, _, _) = Make();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true))).Status);
        Assert.Single(daemon.Applied);

        var again = await coordinator.ApplyAndSaveAsync(SettingsFor("Same", locked: true));

        Assert.Equal(SettingsApplyStatus.NoChange, again.Status);
        Assert.Single(daemon.Applied);          // and nothing further reached the daemon
    }

    /// <summary>
    /// Explicitly applying the settings the daemon already runs under an override still ends the
    /// override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The no-op guard asks whether the transport write would change anything. That is not the same
    /// question as whether the <em>operation</em> would: an apply-and-save also publishes the revision and
    /// ends any per-app override, and those are necessary even when the daemon is already running the
    /// bytes. Returning early skipped them, so the override stayed live and this session went on
    /// publishing something else — which a later clear-override would then send back to the daemon.
    /// </para>
    /// <para>
    /// Enabled by recording the baseline from what the daemon accepted, which let the guard fire during
    /// an override for the first time. Codex's probe on #858; the risk was flagged and real.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ApplyingWhatAnOverrideAlreadyRuns_StillEndsTheOverride()
    {
        var (coordinator, daemon, _, _) = Make();

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved,
            (await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: true))).Status);

        // The editor moves to B live-only: published B, disk still A.
        Assert.True((await coordinator.ApplyLiveOnlyAsync(SettingsFor("B", locked: true))).IsLive);

        // And an override puts the daemon back on A.
        Assert.True((await coordinator.ApplyEphemeralAsync(SettingsFor("A", locked: true))).IsLive);
        Assert.True(coordinator.HasEphemeralOverride);

        // Now the user explicitly saves A. The daemon already has those bytes; the session does not.
        await coordinator.ApplyAndSaveAsync(SettingsFor("A", locked: true));

        Assert.False(coordinator.HasEphemeralOverride);
        Assert.Equal("A", Tablet(coordinator.GetCurrent()?.Settings));

        // And ending an override that is already over sends nothing stale back.
        daemon.Applied.Clear();
        await coordinator.ClearEphemeralOverrideAsync();
        Assert.DoesNotContain(daemon.Applied, sent => Tablet(sent) == "B");
    }

    /// <summary>
    /// A superseded daemon's successful reply does not become the current daemon's baseline.
    /// </summary>
    /// <remarks>
    /// Advancing the observation epoch for a reply that arrives late is conservative and right: an
    /// outstanding read is stale either way. Recording its <em>content</em> is not, because the content
    /// describes a daemon this session has moved off. Both used to happen in the same helper, before the
    /// check that rejects the work.
    /// </remarks>
    [Fact]
    public async Task ASupersededReply_DoesNotBecomeTheBaseline()
    {
        var (coordinator, daemon, _, _) = Make();

        // A live-only apply to the old daemon, held mid-flight. It owns the mutation gate while held, so
        // nothing else that mutates can run -- which is why the new daemon's state is established by a
        // reload, which is a read and is not gated. My first attempt used an apply here and deadlocked.
        var hold = HoldNextSetSettings(daemon);
        var stale = coordinator.ApplyLiveOnlyAsync(SettingsFor("Old daemon's", locked: true));

        SwitchDaemon(coordinator, daemon);
        daemon.Settings = SettingsFor("New daemon's", locked: true);
        Assert.Equal(SettingsReloadStatus.Adopted, (await coordinator.ReloadFromDaemonAsync()).Status);

        hold.SetResult(true);                       // the old daemon answers, successfully, far too late
        Assert.Equal(SettingsApplyStatus.Superseded, (await stale).Status);

        // Read directly, because the guard needs the persisted baseline to agree as well and a reset
        // clears that -- so no sequence reachable from outside can turn this contamination into a
        // skipped apply on its own. It is still the wrong value to be holding, and holding it is what
        // this asserts.
        Assert.Equal(Json(SettingsFor("New daemon's", locked: true)),
            coordinator.LastObservedDaemonSettingsJson);
    }

    // --- #828 readiness: a connection nobody has identified yet ------------------------------

    /// <summary>
    /// Work admitted after a switch, but before anything has identified the new daemon, must not write
    /// the new daemon's settings into the old daemon's file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half of #828 that channel binding does not cover, and the reason binding alone was never
    /// enough. The bound channel makes the <em>send</em> correct: it goes to B, which is the daemon that
    /// is actually connected. The destination on disk is a different question, and the answer still comes
    /// from the host — which learns B's settings file from B's <c>AppInfo</c>, during a data load that
    /// has not happened yet. So the apply is live on B and persisted into A's file.
    /// </para>
    /// <para>
    /// A's file belongs to an install OTA was never asked to touch, and which the user may also be
    /// driving with OpenTabletDriver's own UX. This is the same hazard #787 fixed for a <em>pending</em>
    /// write, where the destination had moved under a retry; what was left is the destination never
    /// having been right in the first place.
    /// </para>
    /// <para>
    /// Written before the fix, per #828's own instruction that the ordering tests drive the design.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnApplyAdmittedBeforeTheNewDaemonIsIdentified_DoesNotWriteIntoTheOldDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        await coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        Assert.Equal("A's edit", Tablet(store.OnDiskAt(DefaultPath)));

        // B answers. Nothing has identified it yet, so the host still believes the settings file is A's:
        // that only moves once a data load has read B's AppInfo.
        daemon.Reconnect();

        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("B's edit", locked: true));

        // Whatever else happens, A's file must still hold A's edit.
        Assert.Equal("A's edit", Tablet(store.OnDiskAt(DefaultPath)));
        Assert.NotEqual(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
    }

    // --- #803: a daemon change invalidates work that is queued or in flight ------------------
    //
    // #787 stopped the coordinator from CARRYING state between daemons. It did not stop an operation
    // that straddles the change, and serializing every mutating path (#777) could not: the reset is
    // deliberately NOT behind the semaphore, because it must not queue behind work belonging to a daemon
    // that has gone. So there are two windows left, and each of these pins one of them.

    /// <summary>
    /// The window between "the RPC was sent" and "the result is written".
    ///
    /// The destination used to be resolved after the await, from whichever daemon was connected by then.
    /// An apply sent to A, completing after the user switched to B, therefore wrote A's settings into
    /// <b>B's</b> settings.json — the exact failure #789 closed for the pending-save retry and left open
    /// on the first write.
    /// </summary>
    [Fact]
    public async Task AnApplyThatOutlivesTheDaemon_DoesNotWriteToTheNewDaemonsFile()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);
        store.Seed(SettingsFor("B's own settings", locked: false), OtherPath);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));

        // The user stops A and starts B while the apply is still waiting on A.
        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives

        hold.SetResult(true);   // A answers, too late to matter
        var outcome = await apply;

        // Asserted before the status, because this is the damage: B's file, holding A's edit.
        Assert.Equal("B's own settings", Tablet(store.OnDiskAt(OtherPath)));
        Assert.Equal(SettingsApplyStatus.Superseded, outcome.Status);
        Assert.False(coordinator.HasUnsavedChange);   // nor is it left pending against B
    }

    /// <summary>
    /// The other window: an operation that is still <em>queued</em> when the daemon changes.
    ///
    /// The generation has to be captured before the wait, not after acquiring the semaphore — time spent
    /// queued is exactly when the daemon can change underneath a caller. Sampling it inside would see the
    /// new value and send A's edit to B.
    /// </summary>
    [Fact]
    public async Task AnApplyQueuedWhenTheDaemonChanges_IsNeverSent()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        var hold = HoldNextSetSettings(daemon);
        var first = coordinator.ApplyAndSaveAsync(SettingsFor("A's first edit", locked: true));

        // Queued behind the first, still meant for A. Started, not awaited — awaiting here would
        // deadlock the test against the semaphore the first apply is holding.
        var queued = coordinator.ApplyAndSaveAsync(SettingsFor("A's second edit", locked: false));

        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        hold.SetResult(true);

        await first;
        Assert.Equal(SettingsApplyStatus.Superseded, (await queued).Status);

        // One call reached the daemon — the one that was already in flight. The queued edit was dropped
        // rather than delivered to a daemon it was never meant for.
        Assert.Single(daemon.Applied);
        Assert.Null(store.OnDiskAt(OtherPath));
    }

    /// <summary>
    /// The same window for a per-app override. Recording one against B would suppress B's settings read
    /// on the strength of an override B never received — #737's failure, reintroduced by a daemon switch.
    /// </summary>
    [Fact]
    public async Task AnEphemeralOverrideThatOutlivesTheDaemon_IsNotRecorded()
    {
        var (coordinator, daemon, _, _) = Make();

        var hold = HoldNextSetSettings(daemon);
        var ephemeral = coordinator.ApplyEphemeralAsync(SettingsFor("Per-app snapshot", locked: true));

        SwitchDaemon(coordinator, daemon);
        hold.SetResult(true);

        // Superseded, not merely "false": the session it was for had ended.
        Assert.Equal(SettingsApplyStatus.Superseded, (await ephemeral).Status);
        Assert.False(coordinator.HasEphemeralOverride);
    }

    /// <summary>
    /// The control. Every assertion above is "nothing happened", which a coordinator that had stopped
    /// working entirely would also satisfy. After the switch, B's own edits must still apply and save.
    /// </summary>
    [Fact]
    public async Task ButAfterTheSwitch_TheNewDaemonsEditsStillLand()
    {
        var path = new PathHolder();
        var (coordinator, daemon, store, _) = Make(path);

        var hold = HoldNextSetSettings(daemon);
        var apply = coordinator.ApplyAndSaveAsync(SettingsFor("A's edit", locked: true));
        SwitchDaemon(coordinator, daemon);
        path.Value = OtherPath;
        Identify(coordinator, daemon, path);      // B answered, and said where it lives
        hold.SetResult(true);
        await apply;

        daemon.Applied.Clear();
        var outcome = await coordinator.ApplyAndSaveAsync(SettingsFor("B's edit", locked: true));

        Assert.Equal(SettingsApplyStatus.AppliedAndSaved, outcome.Status);
        Assert.Equal("B's edit", Tablet(store.OnDiskAt(OtherPath)));
        Assert.Single(daemon.Applied);
    }
}
