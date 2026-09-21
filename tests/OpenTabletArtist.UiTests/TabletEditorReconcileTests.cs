using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OtdInterop.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// What the editor shows after its own apply, now that the session no longer edits the object it is
/// given.
///
/// <para>
/// The session works on a private copy and reports the revision it actually sent. So the editor's draft
/// can differ from what the daemon received — a third-party filter the app disables, a missing area it
/// repairs — and the editor has to take up that revision rather than go on displaying its draft.
/// </para>
/// <para>
/// This is not cosmetic. External-change detection compares the editor's profile against what the daemon
/// reports and treats a difference as somebody else editing. An editor still holding a draft that policy
/// has since changed would make its <em>own</em> apply look like an external edit, and with an unsaved
/// change present that raises a banner telling the user their settings were changed outside the app.
/// </para>
/// <para>
/// In the UI suite because the editor's persist paths run through the dispatcher and a debounce.
/// </para>
/// </summary>
public class TabletEditorReconcileTests
{
    private const string ThirdPartyFilter = "OpenTabletDriver.Filters.Noise.NoiseReduction";

    /// <summary>
    /// An edit held because the daemon changed elsewhere is not thrown away by the next reload (#905).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #491 made an apply capable of deliberately sending nothing: the daemon holds somebody else's edit,
    /// so this one is held rather than written over it. That only means anything if the held edit then
    /// survives. It did not. `HasUnsavedEdit` was hardcoded false — correctly, until #491, because every
    /// edit applied immediately and nothing could be outstanding — so the next focus or poll reload
    /// adopted the daemon's version straight over the top, and the artist lost the change without anyone
    /// choosing to lose it.
    /// </para>
    /// <para>
    /// The reload here is the automatic one, not the banner's Reload. That is the whole point: taking the
    /// daemon's version is a decision the artist is entitled to make, and this is what happens when
    /// nobody made it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AnEditHeldBecauseSettingsChangedElsewhere_SurvivesTheNextAutomaticReload()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere));

        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.DisablePressure, "the held edit should still be on screen");

        // What the daemon holds, arriving on an ordinary reload. It does not have the artist's change,
        // and it differs from this editor in its own right.
        var fresh = SettingsWithForeignFilter();
        fresh.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(fresh, fresh.Profiles[0]);

        Assert.True(vm.DisablePressure, "the reload replaced an edit nobody agreed to give up");

        vm.Dispose();
    }

    /// <summary>
    /// And once the artist takes the daemon's version, the editor stops holding anything.
    /// </summary>
    /// <remarks>
    /// The other half: a flag that protects a held edit for ever would make every later reload a no-op,
    /// which is a slower way of showing the artist something untrue.
    /// </remarks>
    [AvaloniaFact]
    public async Task OnceTheDaemonsVersionIsTakenUp_NothingIsHeldAnyMore()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere));

        vm.DisablePressure = true;
        await Settle();

        // The artist takes theirs, which is what the banner's Reload does.
        var theirs = SettingsWithForeignFilter();
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(theirs, theirs.Profiles[0]);
        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        Assert.False(vm.DisablePressure, "their version should be showing now");
        Assert.True(vm.DisableTilt);

        // A later reload is free to reconcile again, because nothing is outstanding.
        var later = SettingsWithForeignFilter();
        later.Profiles[0].BindingSettings.DisableTilt = false;
        vm.ReconcileExternalChange(later, later.Profiles[0]);

        Assert.False(vm.DisableTilt, "the editor is holding nothing, so a reload should land");

        vm.Dispose();
    }

    /// <summary>
    /// A curve edit held by the daemon is protected like any other (#905).
    /// </summary>
    /// <remarks>
    /// The curve and hover tabs apply through their own methods rather than through the shared one, and
    /// the held-state assignment lived only in the shared path. So a smoothing edit the session declined
    /// to send was left unprotected and the next reload took it, while the identical situation one tab
    /// over was safe. Every apply path now reports its outcome to the same place.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACurveEditHeldBecauseSettingsChangedElsewhere_SurvivesTheNextReload()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere));

        vm.PressureSmoothing = 0.42;
        await Settle();
        Assert.Equal(0.42, vm.PressureSmoothing, 3);

        var fresh = SettingsWithForeignFilter();
        fresh.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(fresh, fresh.Profiles[0]);

        Assert.Equal(0.42, vm.PressureSmoothing, 3);

        vm.Dispose();
    }

    /// <summary>
    /// A later apply that fails for its own reasons does not release an earlier held edit (#905).
    /// </summary>
    /// <remarks>
    /// The assignment released protection for every outcome that was not itself held, including
    /// disconnected and rejected. None of those is evidence that the held edit reached the daemon, so a
    /// tilt edit that could not be sent discarded a pressure edit that was being kept safe. Protection
    /// now ends only when something shows the draft actually landed.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnUnrelatedFailedApply_DoesNotReleaseAnEarlierHeldEdit()
    {
        var settings = SettingsWithForeignFilter();
        var outcome = SettingsApplyOutcome.ChangedElsewhere;
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(outcome));

        vm.DisablePressure = true;                       // held
        await Settle();

        outcome = SettingsApplyOutcome.Disconnected;     // the next edit cannot be sent at all
        vm.DisableTilt = true;
        await Settle();

        var fresh = SettingsWithForeignFilter();
        vm.ReconcileExternalChange(fresh, fresh.Profiles[0]);

        Assert.True(vm.DisablePressure, "a failure elsewhere gave away an edit it knew nothing about");

        vm.Dispose();
    }

    /// <summary>
    /// And an apply that does reach the daemon ends the hold, whichever tab it came from (#905).
    /// </summary>
    /// <remarks>
    /// The other direction: protection that never ends would make every later reload a no-op, which is a
    /// slower way of showing the artist something untrue. The curve path is used here because it is one
    /// of the two that used to bypass this entirely.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACurveApplyThatReachesTheDaemon_EndsAnEarlierHold()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var outcome = SettingsApplyOutcome.ChangedElsewhere;
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: draft =>
            {
                if (outcome.Status != SettingsApplyStatus.AppliedAndSaved) return Task.FromResult(outcome);
                var revision = Clone(draft);
                sent.Add(revision);
                return Task.FromResult(new SettingsApplyOutcome(
                    SettingsApplyStatus.AppliedAndSaved, null,
                    new PreparedSettings(revision, new SettingsStamp(1, 1))));
            });

        vm.DisablePressure = true;                       // held
        await Settle();

        outcome = SettingsApplyOutcome.Saved;            // a curve edit that does land
        vm.PressureSmoothing = 0.3;
        await Settle();
        Assert.NotEmpty(sent);

        var later = SettingsWithForeignFilter();
        later.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(later, later.Profiles[0]);

        Assert.True(vm.DisableTilt, "nothing is held any more, so the reload should land");

        vm.Dispose();
    }

    /// <summary>An edit held because the daemon could not be asked is protected on the same terms.</summary>
    [AvaloniaFact]
    public async Task AnEditHeldBecauseTheDaemonCouldNotBeAsked_AlsoSurvivesTheNextReload()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.CouldNotCheck));

        vm.DisablePressure = true;
        await Settle();

        var fresh = SettingsWithForeignFilter();
        fresh.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(fresh, fresh.Profiles[0]);

        Assert.True(vm.DisablePressure);

        vm.Dispose();
    }

    /// <summary>
    /// A result about an older draft cannot release a newer held change (#905).
    /// </summary>
    /// <remarks>
    /// The staleness guard sat inside the adoption step, which runs after the held-state decision. So a
    /// curve apply that succeeded — about an edit the artist had already moved on from — announced
    /// "nothing is held any more" about a pressure edit it knew nothing about, and the next reload took
    /// it. The guard now runs in front of everything the result is allowed to touch.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnOlderApplySucceeding_DoesNotReleaseANewerHeldChange()
    {
        var settings = SettingsWithForeignFilter();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowCurve = true;

        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: async draft =>
            {
                if (slowCurve)
                {
                    // The curve apply is still out there when the next edit is made.
                    await release.Task;
                    var revision = Clone(draft);
                    return new SettingsApplyOutcome(
                        SettingsApplyStatus.AppliedAndSaved, null,
                        new PreparedSettings(revision, new SettingsStamp(1, 1)));
                }

                return SettingsApplyOutcome.ChangedElsewhere;
            });

        vm.PressureSmoothing = 0.42;                 // starts the slow curve apply
        await Pump(TimeSpan.FromMilliseconds(600));  // past the curve debounce, still held open

        slowCurve = false;
        vm.DisablePressure = true;                   // a newer edit, which the daemon holds
        await Settle();

        release.SetResult(true);                     // the older curve result finally lands
        await Settle();

        var fresh = SettingsWithForeignFilter();
        fresh.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(fresh, fresh.Profiles[0]);

        Assert.True(vm.DisablePressure, "an older result gave away an edit it knew nothing about");

        vm.Dispose();
    }

    /// <summary>
    /// A change held against one daemon is not carried to the one that replaces it (#905).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Editors are cached by tablet name rather than by daemon, so a replacement exposing the same name
    /// reuses this one. A draft held because daemon A disagreed has never been compared with daemon B,
    /// and keeping the hold would make B's editor refuse to show B's own settings on the strength of a
    /// disagreement with somebody else.
    /// </para>
    /// <para>
    /// The draft is given up, which is the answer #787 already gives for an unsaved change when the
    /// daemon changes underneath it. Losing an edit is bad; writing it over settings it was never
    /// compared with is worse.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AChangeHeldAgainstOneDaemon_IsNotHeldAgainstItsReplacement()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere));

        vm.DisablePressure = true;
        await Settle();

        // A different OpenTabletDriver answers, exposing a tablet of the same name.
        vm.DaemonReplaced();

        var theirs = SettingsWithForeignFilter();
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(theirs, theirs.Profiles[0]);

        Assert.True(vm.DisableTilt, "the new daemon's settings should be showing");
        Assert.False(vm.DisablePressure, "a draft from the previous daemon was carried across");
        Assert.False(vm.HasExternalChange, "and its banner went with it");

        vm.Dispose();
    }

    private static Settings SettingsWithForeignFilter(string tablet = "T")
    {
        var profile = new Profile { Tablet = tablet };
        profile.BindingSettings.WheelBindings.Add(new WheelBindingSettings());
        profile.Filters.Add(new PluginSettingStore(ThirdPartyFilter) { Path = ThirdPartyFilter, Enable = true });
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static bool FilterEnabledIn(Settings s) =>
        s.Profiles[0].Filters.First(f => f.Path == ThirdPartyFilter).Enable;

    /// <summary>
    /// Stands in for the session: takes a copy, applies the app's rule to the copy, and reports it.
    /// Deliberately mirrors the real contract rather than calling into it, so this tests the editor.
    /// </summary>
    private static Func<Settings, Task<SettingsApplyOutcome>> SessionThatDisablesTheFilter(
        List<Settings> sent, SettingsApplyStatus status = SettingsApplyStatus.AppliedAndSaved,
        Func<Task>? whileInFlight = null) => async draft =>
        {
            var revision = Clone(draft);
            revision.Profiles[0].Filters.First(f => f.Path == ThirdPartyFilter).Enable = false;
            sent.Add(revision);
            if (whileInFlight != null) await whileInFlight();
            return new SettingsApplyOutcome(status, null, new PreparedSettings(revision, new SettingsStamp(1, 1)));
        };

    private static Settings Clone(Settings s) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(s))!;

    private static Task Settle() => Pump(TimeSpan.FromMilliseconds(900));

    /// <summary>
    /// Pumps until <paramref name="until"/> holds, or fails.
    ///
    /// Waiting a fixed interval is a guess about scheduling; waiting for the thing you are actually
    /// waiting for is not. A test that guesses wrong reports a defect that is not there, or hides one
    /// that is.
    /// </summary>
    private static async Task PumpUntil(Func<bool> until, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!until())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Pump(TimeSpan window)
    {
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task AfterAnApply_TheEditorShowsWhatWasSent_NotItsDraft()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent));

        await vm.RefreshCommand.ExecuteAsync(null);   // no refreshAction; harmless, keeps the VM warm
        vm.DisablePressure = true;                    // an edit that applies immediately
        await Settle();

        Assert.Single(sent);
        Assert.False(FilterEnabledIn(sent[0]));
        // The editor's Filters tab is rebuilt from its own profile, so this is what the user sees.
        var card = vm.Filters.FirstOrDefault(f => f.FullPath == ThirdPartyFilter);
        Assert.NotNull(card);
        Assert.False(card!.Enabled);

        vm.Dispose();
    }

    /// <summary>
    /// The control. If the session reports that nothing reached the daemon, the draft stands — showing
    /// the prepared revision would assert repairs that never happened.
    ///
    /// This is the older bug reversed: policy used to run on the caller's object before the call, so a
    /// refused apply still left the display "repaired".
    /// </summary>
    [AvaloniaFact]
    public async Task IfTheDaemonRefusedIt_TheEditorKeepsItsDraft()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent, SettingsApplyStatus.Disconnected));

        vm.DisablePressure = true;
        await Settle();

        Assert.Single(sent);
        var card = vm.Filters.FirstOrDefault(f => f.FullPath == ThirdPartyFilter);
        Assert.NotNull(card);
        Assert.True(card!.Enabled);   // still enabled: nothing was applied

        vm.Dispose();
    }

    /// <summary>
    /// The case the session's own stamp cannot catch.
    ///
    /// Adopting a result rewrites the bound properties, so if the user has changed something since the
    /// apply began, adopting would wipe it in front of them. The session orders <em>submitted</em>
    /// operations; an edit sitting in the editor's debounce has not been submitted and carries no
    /// operation number at all, which is why the editor counts its own drafts.
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondEditDuringTheFirstApply_IsNotOverwritten()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        TabletDetailViewModel? vm = null;
        var interrupted = false;

        vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent, whileInFlight: () =>
            {
                // Only during the FIRST apply: the user moves a slider. That schedules a persist of its
                // own, so it is a newer draft even though nothing has been submitted for it yet — which
                // is precisely the state the session's own stamp cannot describe.
                if (!interrupted)
                {
                    interrupted = true;
                    // 0.42, not something larger: the slider's ceiling is 0.50 and the reload clamps to
                    // it, so an out-of-range value would look like the edit had been overwritten when it
                    // had merely been clamped.
                    vm!.PressureSmoothing = 0.42;
                }
                return Task.CompletedTask;
            }));

        vm.DisablePressure = true;
        // Long enough for the first apply to complete, short enough that the slider's own 400 ms
        // debounce has not fired. That is the window this is about: a newer edit exists, nothing has
        // been submitted for it, and an older result has just arrived.
        await Pump(TimeSpan.FromMilliseconds(150));

        Assert.True(interrupted);
        Assert.Single(sent);                              // only the first apply has run
        Assert.Equal(0.42, vm.PressureSmoothing, 3);      // the user's newer edit is untouched

        vm.Dispose();
    }

    /// <summary>
    /// The plain case, and the control for the one above: a slider edit persists through its debounce
    /// and the editor still shows the value the user chose afterwards. Adopting what was sent must not
    /// round-trip the user's own edit into something else.
    /// </summary>
    [AvaloniaFact]
    public async Task ASliderEdit_SurvivesItsOwnPersist()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent));

        vm.PressureSmoothing = 0.42;
        await Settle();

        Assert.Single(sent);
        Assert.Equal(0.42, vm.PressureSmoothing, 3);

        vm.Dispose();
    }

    /// <summary>
    /// A slider edit already waiting in its debounce when a different, immediate edit is applied.
    ///
    /// The draft count says "has anything changed since this apply started", which is not the same
    /// question as "does this revision contain everything the user has done". The slider's value was
    /// never written into the settings the immediate apply submitted, so adopting that apply's result
    /// and refreshing puts the stored value back on screen and the pending edit disappears.
    /// </summary>
    [AvaloniaFact]
    public async Task APendingSliderEdit_IsNotOverwrittenByALaterImmediateApply()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent));

        vm.PressureSmoothing = 0.42;   // schedules a persist; not yet in the settings
        vm.DisablePressure = true;     // a different edit that applies immediately
        await Settle();

        Assert.Equal(0.42, vm.PressureSmoothing, 3);

        vm.Dispose();
    }

    /// <summary>
    /// An apply completing after the editor has taken up newer settings from elsewhere.
    ///
    /// A result that was current when the session finished with it is not necessarily current when the
    /// editor gets round to it. Adopting settings from another source is a change of editor state just as
    /// much as a keystroke is, and an older result arriving afterwards must not undo it.
    /// </summary>
    [AvaloniaFact]
    public async Task AnOlderResult_DoesNotUndoANewerExternalAdoption()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = false;

        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent, whileInFlight: async () =>
            {
                if (held) return;
                held = true;
                await release.Task;
            }));

        vm.DisablePressure = true;                 // starts an apply, held open

        // Meanwhile the daemon's settings changed elsewhere and the editor adopts them.
        var external = SettingsWithForeignFilter();
        external.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(external, external.Profiles[0]);
        Assert.True(vm.DisableTilt);

        release.SetResult();                       // the older apply finally lands
        await Settle();

        Assert.True(vm.DisableTilt);               // the newer adoption still stands

        vm.Dispose();
    }


    /// <summary>
    /// The editor must edit a profile that lives inside the settings it submits — checked through the
    /// real factory, because that is where the pairing is decided.
    ///
    /// The session hands out a detached copy on every read. A caller that resolved the profile from its
    /// own read and paired it with settings from a different read would give the editor two unrelated
    /// object graphs: edits go into one, the other is sent, and the change never leaves the app — with
    /// every layer reporting success.
    ///
    /// Constructing the view model directly from a single settings object cannot catch this. The two
    /// arguments have to come from wherever the app really gets them.
    /// </summary>
    [AvaloniaFact]
    public async Task AnEditMadeThroughTheFactory_ReachesTheDaemon()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsWithForeignFilter(),
            AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json", PluginDirectory = "" },
        };
        using var session = new AppSession(FakeSession.Over(daemon, new NoopStore()), new StubLifecycle())
        {
            Ownership = DaemonOwnership.Owned,
        };
        await session.ReloadAsync();

        var vm = new DialogService(session).CreateTabletDetail("T", () => Task.CompletedTask);
        Assert.NotNull(vm);

        daemon.Applied.Clear();
        vm!.DisablePressure = true;
        await Settle();

        Assert.NotEmpty(daemon.Applied);
        Assert.True(daemon.Applied[^1].Profiles.First(p => p.Tablet == "T").BindingSettings.DisablePressure);

        vm.Dispose();
    }

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
        public string? PathOf(int processId) => null;
        public string? SingleRunningDaemonPath() => null;
    }

    private sealed class NoopStore : ISettingsFileStore
    {
        public void Save(Settings settings, string path) { }
        public bool TrySave(Settings settings, string path) => true;
        public bool TryLoad(string path, out Settings? settings) { settings = null; return false; }
    }

    /// <summary>
    /// The reload that follows our own apply must not run over an edit that is still pending.
    ///
    /// The session reloads before returning its outcome, so the load — and the reconciliation it drives
    /// — happens while the apply is still in flight, before the result can be refused. If policy changed
    /// the submitted profile, its fingerprint differs from the editor's, so reconciliation treated it as
    /// an external edit and adopted it, refreshing the pending slider away.
    /// </summary>
    [AvaloniaFact]
    public async Task AReloadDuringOurOwnApply_DoesNotDiscardAPendingEdit()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        TabletDetailViewModel? vm = null;

        vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: SessionThatDisablesTheFilter(sent, whileInFlight: () =>
            {
                // What AppSession does: reload, raise DataLoaded, reconcile the open editors — all before
                // the apply's own outcome comes back.
                var fresh = Clone(sent[^1]);
                vm!.ReconcileExternalChange(fresh, fresh.Profiles[0]);
                return Task.CompletedTask;
            }));

        vm.PressureSmoothing = 0.42;   // pending in its debounce, in nothing submitted
        vm.DisablePressure = true;     // applies immediately, and reloads while in flight
        await Settle();

        Assert.Equal(0.42, vm.PressureSmoothing, 3);

        // Surviving on screen is only half of it: the value has to reach a submission too, or it is
        // displayed and then lost at the next reload.
        var dynamics = OpenTabletArtist.Services.PressureCurveProfile.Read(sent[^1], "T");
        Assert.NotNull(dynamics);
        Assert.Equal(0.42, dynamics!.Value.Dynamics.PressureSmoothing, 3);

        vm.Dispose();
    }

    private static bool Pressure(Settings s) =>
        s.Profiles.First(p => p.Tablet == "T").BindingSettings.DisablePressure;

    // --- Reads that are overtaken (#814) ------------------------------------------------------
    //
    // Both of these come from the review of this change. The ordering they share is the part I kept
    // getting wrong on my own: C's payload has to be inspected AT SUBMISSION, while the correcting
    // reload is still held. Awaiting everything first lets that reload repair the editor, and the defect
    // disappears before any assertion can see it.

    /// <summary>
    /// Reload-and-redo is a way out, not a loop: after taking the daemon's version the redone edit lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With no overwrite action yet, this is the only path an artist has out of a held edit, and the docs
    /// say so. It only works if the reload moves the baseline the next apply compares against — otherwise
    /// the redo meets the same stale comparison and is held again, and the documented way forward is a
    /// circle.
    /// </para>
    /// <para>
    /// Driven through a real session rather than a supplied snapshot, because the baseline lives in the
    /// coordinator: an editor test that hands the view model a fresh profile proves adoption and proves
    /// nothing about what the next apply will be compared with (#905).
    /// </para>
    /// <para>
    /// The last assertion is the one that matters most. Taking their version and redoing the edit must
    /// not quietly undo the rest of what they changed.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AfterTakingTheDaemonsVersion_TheRedoneEditLandsWithoutUndoingTheirs()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        // Somebody else changes the daemon behind this session's back.
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        // The artist's edit is held rather than written over it.
        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.DisablePressure, "the held edit should still be on screen");
        Assert.False(Pressure(daemon.Settings!), "nothing should have been sent");
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt, "and theirs is untouched");

        // An ordinary reload: the session adopts what the daemon holds, which also moves the baseline.
        await session.ReloadAsync();
        await Settle();

        // The artist takes their version, which is what the banner's Reload does.
        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();
        Assert.False(vm.DisablePressure, "their version is showing now");
        Assert.True(vm.DisableTilt, "including the change they made");

        // Redo the edit. This is the step that was a loop if the baseline had not moved.
        vm.DisablePressure = true;
        await PumpUntil(() => Pressure(daemon.Settings!), "the redone edit to reach the daemon");

        Assert.True(Pressure(daemon.Settings!), "the redone edit never landed");
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "the redo undid the change the artist had just accepted");

        vm.Dispose();
    }

    private sealed record ReadHarness(
        FakeDaemonTransport Daemon,
        AppSession Session,
        TabletDetailViewModel Editor);

    /// <summary>A real session and a real editor, wired as the shell wires them.</summary>
    private static async Task<ReadHarness> RealEditor()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = SettingsWithForeignFilter(),
            AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json", PluginDirectory = "" },
        };
        var session = new AppSession(FakeSession.Over(daemon, new NoopStore()), new StubLifecycle())
        {
            Ownership = DaemonOwnership.Owned,
        };
        await session.ReloadAsync();

        var vm = new DialogService(session).CreateTabletDetail("T", () => Task.CompletedTask);
        Assert.NotNull(vm);
        session.DataLoaded += () =>
        {
            var current = session.CurrentSettings;
            vm!.ReconcileExternalChange(current, current?.Profiles.FirstOrDefault(p => p.Tablet == "T"));
        };
        return new ReadHarness(daemon, session, vm!);
    }

    /// <summary>
    /// A response that was captured before a later change and delivered after it must not be adopted.
    ///
    /// The daemon services a read at some moment of its choosing; a response can describe a state that
    /// has since been superseded. Adopting it is not a display glitch — the editor's next edit is built
    /// on that baseline and sends the superseded value back, so the app undoes the user's own change.
    /// </summary>
    [AvaloniaFact]
    public async Task AnOlderReadDeliveredAfterANewerChange_IsNotAdopted()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        var first = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        // Only the two reload reads are choreographed. Since #491 an apply reads the daemon before it
        // writes, to see whether anyone else has, and that read is served immediately -- holding it would
        // stall the very apply this test inspects, and it is not the read the test is about.
        daemon.GetSettingsHandler = () => ++reads switch
        {
            1 => first.Task,
            2 => second.Task,
            _ => Task.FromResult<Settings?>(daemon.Settings is { } now ? Clone(now) : null),
        };

        try
        {
            vm.DisablePressure = true;
            var older = Clone(daemon.Settings!);      // captured while pressure is disabled
            vm.DisablePressure = false;
            Assert.False(Pressure(daemon.Settings!)); // the daemon has moved on

            first.SetResult(older);                   // the stale response finally arrives
            await PumpUntil(() => reads >= 2, "the correcting reload to start");

            // Inspected here, with the correcting reload still held. Letting it land first would repair
            // the editor and hide the defect entirely.
            vm.DisableTilt = true;
            var submitted = Pressure(daemon.Applied[^1]);

            Assert.False(submitted);
        }
        finally
        {
            daemon.GetSettingsHandler = null;
            second.TrySetResult(daemon.Settings is { } s ? Clone(s) : null);
            first.TrySetResult(null);
            await Settle();
            vm.Dispose();
        }
    }

    /// <summary>
    /// A read that STARTS after the session published a change but before the daemon accepted it.
    ///
    /// Publishing happens before the call, so such a read carries a version that looks current while
    /// returning state from before the change. Versioning the local baseline is not enough: only the
    /// daemon's acceptance changes what a read of the daemon can observe.
    /// </summary>
    [AvaloniaFact]
    public async Task AReadStartedWhileAnApplyWasInFlight_IsNotAdopted()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        var accept = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ => accept.Task;

        var first = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<Settings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        try
        {
            vm.DisablePressure = true;                // published; not yet accepted
            var older = Clone(daemon.Settings!);      // still shows pressure enabled

            daemon.GetSettingsHandler = () => ++reads == 1 ? first.Task : second.Task;
            var reload = session.ReloadAsync();       // this read begins AFTER the publish
            await PumpUntil(() => reads >= 1, "the reload's read to start");

            accept.SetResult(true);                   // now the daemon takes the change
            await PumpUntil(() => Pressure(daemon.Settings!), "the daemon to hold the applied value");

            first.SetResult(older);                   // the read that began too early answers
            await PumpUntil(() => reads >= 2, "the correcting reload to start");

            daemon.SetSettingsHandler = null;
            vm.DisableTilt = true;
            var submitted = Pressure(daemon.Applied[^1]);

            // The successful edit stands: the next submission still carries it.
            Assert.True(submitted);

            daemon.GetSettingsHandler = null;
            second.TrySetResult(Clone(daemon.Settings!));
            await reload;
        }
        finally
        {
            daemon.GetSettingsHandler = null;
            daemon.SetSettingsHandler = null;
            accept.TrySetResult(true);
            first.TrySetResult(null);
            second.TrySetResult(daemon.Settings is { } s ? Clone(s) : null);
            await Settle();
            vm.Dispose();
        }
    }
}
