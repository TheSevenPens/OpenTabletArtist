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

    /// <summary>
    /// A held change says so in the editor at once, without waiting for a reload (#906).
    /// </summary>
    /// <remarks>
    /// The banner used to be raised only from reconciliation, which needs a reload that actually
    /// disagrees. For a check that could not be made there may be nothing to disagree with — the daemon
    /// may hold exactly what we think and the read simply failed — so an artist whose edit was being held
    /// had nothing on the page telling them so.
    /// </remarks>
    [AvaloniaFact]
    public async Task AChangeHeldBecauseTheDaemonCouldNotBeAsked_SaysSoAtOnceAndOffersToTryAgain()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.CouldNotCheck));

        vm.DisablePressure = true;
        await Settle();

        Assert.True(vm.HasExternalChange, "the artist should be told, not left guessing");
        Assert.Contains("Couldn't check", vm.ExternalChangeText);
        Assert.True(vm.CanRetryHeldChange);

        // Nothing was seen, so there is nothing to overwrite and nothing to reload.
        Assert.False(vm.CanOverwriteHeldChange, "an unanswered question is not a conflict to overwrite");
        Assert.False(vm.CanReloadExternalChange, "no snapshot has arrived, so Reload would do nothing");

        vm.Dispose();
    }

    /// <summary>Trying again is the same apply, and the same check with it (#906).</summary>
    /// <remarks>
    /// Retry must not be a way past the check. It re-runs the ordinary apply, so if the daemon has
    /// recovered the change lands, and if it has not the change is held again.
    /// </remarks>
    [AvaloniaFact]
    public async Task TryingAgainAfterTheDaemonRecovers_AppliesTheHeldChange()
    {
        var settings = SettingsWithForeignFilter();
        var sent = new List<Settings>();
        var answer = SettingsApplyOutcome.CouldNotCheck;
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: draft =>
            {
                if (answer.Status != SettingsApplyStatus.AppliedAndSaved) return Task.FromResult(answer);
                var revision = Clone(draft);
                sent.Add(revision);
                return Task.FromResult(new SettingsApplyOutcome(
                    SettingsApplyStatus.AppliedAndSaved, null,
                    new PreparedSettings(revision, new SettingsStamp(1, 1))));
            });

        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.CanRetryHeldChange);

        answer = SettingsApplyOutcome.Saved;           // the daemon starts answering again
        vm.RetryHeldChangeCommand.Execute(null);
        await Settle();

        Assert.NotEmpty(sent);
        Assert.True(Pressure(sent[^1]), "the held change should have been the thing retried");
        Assert.False(vm.HasExternalChange, "and nothing is held any more");

        vm.Dispose();
    }

    /// <summary>
    /// Keeping the artist's change sends the conflict back as the authorisation (#906).
    /// </summary>
    /// <remarks>
    /// The button exists so consent is something the artist gives about a particular conflict. Sending
    /// the change without the conflict would be an ordinary apply, which the session would hold again —
    /// and sending it with one the session never issued would be this editor inventing permission.
    /// </remarks>
    [AvaloniaFact]
    public async Task KeepingTheArtistsChange_AuthorisesItWithTheConflictTheyWereShown()
    {
        var settings = SettingsWithForeignFilter();
        var conflict = ConflictSeenByTheSession();
        SettingsConflict? authorisedWith = null;

        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(
                new SettingsApplyOutcome(SettingsApplyStatus.ChangedElsewhere, Conflict: conflict)),
            overwriteAction: (draft, seen) =>
            {
                authorisedWith = seen;
                var revision = Clone(draft);
                return Task.FromResult(new SettingsApplyOutcome(
                    SettingsApplyStatus.AppliedAndSaved, null,
                    new PreparedSettings(revision, new SettingsStamp(1, 1))));
            });

        vm.DisablePressure = true;
        await Settle();

        Assert.True(vm.HasExternalChange);
        Assert.Contains("changed outside OpenTabletArtist", vm.ExternalChangeText);
        Assert.True(vm.CanOverwriteHeldChange);
        Assert.False(vm.CanRetryHeldChange, "a seen conflict is not resolved by asking again");

        vm.OverwriteHeldChangeCommand.Execute(null);
        await Settle();

        Assert.Same(conflict, authorisedWith);
        Assert.False(vm.HasExternalChange, "the banner should go once the change lands");

        vm.Dispose();
    }

    /// <summary>A conflict with nothing to overwrite it with offers no such button.</summary>
    /// <remarks>
    /// The editor must not show an action it cannot carry out. Without an overwrite action wired in —
    /// which is the case in every test harness that does not supply one — the offer is withheld rather
    /// than presented and silently ignored.
    /// </remarks>
    [AvaloniaFact]
    public async Task WithNoWayToOverwrite_TheOfferIsNotMade()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(new SettingsApplyOutcome(
                SettingsApplyStatus.ChangedElsewhere, Conflict: ConflictSeenByTheSession())));

        vm.DisablePressure = true;
        await Settle();

        Assert.True(vm.HasExternalChange);
        Assert.False(vm.CanOverwriteHeldChange);

        vm.Dispose();
    }

    /// <summary>
    /// A conflict as the session would report one.
    /// </summary>
    /// <remarks>
    /// Constructed rather than obtained from a real coordinator, because what these tests are about is
    /// the editor carrying the token back unchanged. Whether the token means anything is the session's
    /// business and is covered where that decision lives.
    /// </remarks>
    private static SettingsConflict ConflictSeenByTheSession() =>
        new(issuer: Guid.NewGuid(), channel: 1, daemonState: "{\"whatever\":\"the daemon held\"}");

    /// <summary>
    /// Retrying a held change does not overwrite an external edit a reload merely learned about (#910).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison a held draft is weighed against used to be rebased onto every reload. So: reads
    /// fail and a change is held; reads recover and somebody else edits the daemon; an automatic reload
    /// learns their edit; and the held draft — still built against the older state — is resubmitted,
    /// found "unchanged" against the newly advanced baseline, and written straight over them.
    /// </para>
    /// <para>
    /// Learning somebody's settings is not consent to replace them. The draft is now compared against
    /// what it was held against until the artist resolves it, so the retry meets the conflict it should.
    /// </para>
    /// <para>
    /// Driven through a real session because the defect lived in the space between the coordinator's
    /// baseline and the editor's draft; a fake apply action cannot have that space.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task RetryingAHeldChange_DoesNotOverwriteAnEditTheReloadJustLearnedAbout()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        // The daemon stops answering reads, so the artist's edit is held with nothing established.
        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");

        vm.DisablePressure = true;
        await Settle();
        Assert.False(Pressure(daemon.Settings!), "nothing should have been sent");

        // Reads recover, and somebody else changes the daemon in the meantime.
        daemon.GetSettingsHandler = null;
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        // An ordinary reload learns their edit. It does not resolve anything on the artist's behalf.
        await session.ReloadAsync();
        await Settle();

        vm.RetryHeldChangeCommand.Execute(null);
        await Settle();

        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "the retry overwrote an external edit that nobody had agreed to replace");

        vm.Dispose();
    }

    /// <summary>
    /// Nor does simply carrying on editing, which is the same submission by another route (#910).
    /// </summary>
    /// <remarks>
    /// Hiding the Retry button would have left this open. An artist whose change is held does not stop
    /// touching the page, and every edit submits.
    /// </remarks>
    [AvaloniaFact]
    public async Task EditingOnAfterAChangeIsHeld_DoesNotOverwriteWhatTheReloadLearned()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");
        vm.DisablePressure = true;
        await Settle();

        daemon.GetSettingsHandler = null;
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        await session.ReloadAsync();
        await Settle();

        var readsBeforeTheEdit = daemon.GetSettingsCalls;

        // Not the button: just carrying on editing.
        // A plain profile setting, not Windows Ink: OnDisableWindowsInkChanged returns early off
        // Windows, so an edit made through it submits nothing there and this would pass by doing
        // nothing at all. Windows is the supported platform, but a test that only tests on one of
        // the three legs CI runs is not saying so — it is just quiet.
        vm.DisablePressure = false;
        await Settle();

        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "editing on overwrote an external edit that nobody had agreed to replace");

        // And the edit really was submitted and really was held, rather than never leaving the editor:
        // the check reads the daemon before it decides, so a read means a submission reached it.
        Assert.True(daemon.GetSettingsCalls > readsBeforeTheEdit,
            "no submission reached the daemon, so this proves nothing about holding one");

        vm.Dispose();
    }

    /// <summary>
    /// And once the artist takes the daemon's version, editing lands again (#910).
    /// </summary>
    /// <remarks>
    /// The pin has to be released by something, or holding it would turn into refusing the artist's work
    /// for ever. Taking their version is the release, because it is the artist saying what a reload
    /// cannot say for them.
    /// </remarks>
    [AvaloniaFact]
    public async Task AfterTakingTheirVersion_EditingLandsAgain()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        daemon.GetSettingsHandler = () => throw new InvalidOperationException("no answer");
        vm.DisablePressure = true;
        await Settle();

        daemon.GetSettingsHandler = null;
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        await session.ReloadAsync();
        await Settle();

        // The artist takes theirs, which is the banner's Reload.
        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        vm.DisablePressure = true;
        await PumpUntil(() => Pressure(daemon.Settings!), "the redone edit to reach the daemon");

        Assert.True(Pressure(daemon.Settings!));
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "and it did not undo what the artist had just accepted");

        vm.Dispose();
    }

    /// <summary>
    /// A refused acceptance leaves the draft where it is (#910).
    /// </summary>
    /// <remarks>
    /// The session can correctly refuse to release a hold — the snapshot this editor is offering has
    /// been overtaken — and the editor was throwing the draft away anyway, because it asked and then
    /// discarded the answer. The comment beside that call even said a refusal leaves the hold standing.
    /// It did not.
    /// </remarks>
    [AvaloniaFact]
    public async Task WhenAcceptanceIsRefused_TheDraftIsKept()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere),
            acceptCurrentAction: _ => false);   // the session says that snapshot is no longer current

        vm.DisablePressure = true;
        await Settle();

        var theirs = SettingsWithForeignFilter();
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(theirs, theirs.Profiles[0], new SettingsStamp(1, 1));

        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        Assert.True(vm.DisablePressure, "the editor discarded the draft despite acceptance being refused");
        Assert.False(vm.DisableTilt, "and it should not have adopted the snapshot it was refused");
        Assert.True(vm.HasExternalChange, "the artist needs to be told the offer moved on");
        Assert.Contains("changed again", vm.ExternalChangeText);

        vm.Dispose();
    }

    /// <summary>And an accepted one still resolves, so refusing is not simply a wall.</summary>
    [AvaloniaFact]
    public async Task WhenAcceptanceIsGranted_TheSnapshotIsAdopted()
    {
        var settings = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere),
            acceptCurrentAction: _ => true);

        vm.DisablePressure = true;
        await Settle();

        var theirs = SettingsWithForeignFilter();
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        vm.ReconcileExternalChange(theirs, theirs.Profiles[0], new SettingsStamp(1, 1));

        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        Assert.True(vm.DisableTilt);
        Assert.False(vm.DisablePressure);
        Assert.False(vm.HasExternalChange);

        vm.Dispose();
    }

    /// <summary>
    /// A reconnect under a held draft stops submission until the artist decides — it does not quietly
    /// re-enable it (#906).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence the library's refusal creates, and the one my first two attempts at handling it both
    /// got wrong. Clearing the hold let the next edit go out as an ordinary apply, which met a baseline
    /// the reconnect's reload had brought level with the daemon and wrote over the external change:
    /// protection dropped rather than resolved. Giving the draft up lost the artist's work silently, for
    /// a reason #905 does not supply — that was a different daemon, this is the same one one connection
    /// later.
    /// </para>
    /// <para>
    /// Driven through a real session, because the defect is in what the editor and the session do to each
    /// other across a reconnect, and a stubbed outcome cannot have a reconnect. The second edit is made
    /// <b>before</b> any reconciliation, which is what my earlier version quietly skipped past.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AfterAReconnectUnderAHeldDraft_TheNextEditIsNotWrittenEither()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        // Somebody else suppresses tilt, and the artist's pressure edit is held against what this
        // session had read before that.
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.HasExternalChange, "the change should be held to begin with");

        // The connection is replaced and the session reloads, so its baseline is now the daemon's own
        // settings. The editor still holds its draft, and its hold names the connection that has gone.
        daemon.Reconnect();
        await session.ReloadAsync();
        await Settle();

        // The artist edits again. The library refuses the hold; nothing may be written.
        vm.DisablePressure = false;
        await Settle();
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "the first edit after the reconnect overwrote the external change");

        // And again, before any reconciliation has offered them anything. This is the edit that used to
        // go through: the hold had been cleared, so the submission was an ordinary apply.
        var writesBefore = daemon.Applied.Count;
        vm.DisablePressure = true;
        await Settle();

        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "editing on after the reconnect overwrote an external change nobody agreed to replace");
        Assert.Equal(writesBefore, daemon.Applied.Count);

        // Said explicitly, because "the daemon was not written to" is also what a test that submitted
        // nothing at all would observe. This banner is set on exactly one path — the editor taking a
        // refused hold — so it distinguishes a refusal that happened from an edit that never left.
        Assert.Contains("connection to OpenTabletDriver was replaced", vm.ExternalChangeText);
        Assert.True(vm.HasExternalChange, "and the artist is still owed a decision");
        Assert.False(vm.CanRetryHeldChange, "trying again would be the ordinary apply that must not run");
        Assert.False(vm.CanOverwriteHeldChange, "the conflict it held belonged to the old connection");

        vm.Dispose();
    }

    /// <summary>
    /// And the artist is not stuck: taking the current settings resolves it and editing works again
    /// (#906).
    /// </summary>
    /// <remarks>
    /// The other half, without which stopping submission would just be a more explicit way of losing the
    /// artist's work. Reload is the decision: it adopts a snapshot the session vouched for, which is the
    /// thing a reconnect took away.
    /// </remarks>
    [AvaloniaFact]
    public async Task AfterAReconnect_TakingTheCurrentSettingsLetsTheArtistWorkAgain()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        vm.DisablePressure = true;
        await Settle();

        daemon.Reconnect();
        await session.ReloadAsync();
        await Settle();

        vm.DisablePressure = false;
        await Settle();
        Assert.True(vm.HasExternalChange);

        // A reload offers the current settings, and the artist takes them.
        await session.ReloadAsync();
        await Settle();
        Assert.True(vm.CanReloadExternalChange, "there should be something to take");
        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        Assert.False(vm.HasExternalChange, "taking the current settings should have resolved it");
        Assert.True(vm.DisableTilt, "and the editor should be showing them");

        // Editing works again, and what it writes keeps what the artist just accepted.
        vm.DisablePressure = true;
        await PumpUntil(() => Pressure(daemon.Settings!), "the redone edit to reach the daemon");

        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "the redone edit undid the external change the artist had just accepted");

        vm.Dispose();
    }

    /// <summary>
    /// An overwrite refused because the world moved again leaves the draft held, and still held against
    /// what it was first held against (#906).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A refusal is the session declining to act on a decision about a state that is no longer there. It
    /// reports the conflict it found but issues no hold, because it made no comparison on the draft's
    /// behalf — so an editor that took the result at face value would come away holding nothing, and the
    /// next edit would be weighed against whatever a reload had since adopted and written straight over
    /// the external change.
    /// </para>
    /// <para>
    /// The editor keeps the hold it had unless it is given a new one. Nothing about a refusal resolves
    /// anything.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AnOverwriteRefusedBecauseTheWorldMovedAgain_LeavesTheDraftHeld()
    {
        var (daemon, session, vm) = await RealEditor();
        using var _s = session;

        // Somebody else edits, and the artist's change is held against what this session had read.
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.CanOverwriteHeldChange, "the artist should be offered the choice");

        // They edit again before the artist decides, so the thing the artist was shown is gone.
        var theirsAgain = Clone(daemon.Settings!);
        theirsAgain.Profiles[0].BindingSettings.TipActivationThreshold = 42;
        daemon.Settings = theirsAgain;

        vm.OverwriteHeldChangeCommand.Execute(null);
        await Settle();

        Assert.True(vm.HasExternalChange, "a refused overwrite resolves nothing");
        Assert.Equal(42, daemon.Settings!.Profiles[0].BindingSettings.TipActivationThreshold);

        // A reload learns what is actually there, and the artist carries on editing.
        await session.ReloadAsync();
        await Settle();
        vm.DisablePressure = false;
        await Settle();

        Assert.Equal(42, daemon.Settings!.Profiles[0].BindingSettings.TipActivationThreshold);
        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "editing on after a refusal overwrote an edit nobody had agreed to replace");

        vm.Dispose();
    }

    /// <summary>
    /// The forwarder hands each editor the pair it was given, whatever has been published since (#910).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The behavioural half of the snapshot/stamp fix. The caller reads one publication; this shows what
    /// the editors then receive is that publication and not a later one — which is what makes an
    /// acceptance quoting the stamp an agreement about the settings actually on screen.
    /// </para>
    /// <para>
    /// The newer publication is created before the forward and left available throughout, so a forwarder
    /// that went back for a second opinion about the stamp would pick it up. No thread and no timing:
    /// the two publications simply both exist, and only one of them may arrive.
    /// </para>
    /// <para>
    /// The stamp is observed where it actually matters — through the acceptance the editor makes when
    /// the artist takes what it is showing. Reading it off a property would test a property; this tests
    /// the thing the stamp is for.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ForwardingAPublication_GivesEditorsThatPublicationsOwnStamp()
    {
        var older = SettingsWithForeignFilter();
        older.Profiles[0].BindingSettings.DisableTilt = true;
        var olderStamp = new SettingsStamp(1, 7);

        // Already published by the time the forward happens, and never handed over.
        var newer = SettingsWithForeignFilter();
        newer.Profiles[0].BindingSettings.DisableTilt = false;
        var newerStamp = new SettingsStamp(1, 8);
        Assert.NotEqual(olderStamp, newerStamp);

        SettingsStamp? accepted = null;
        var mine = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(mine.Profiles[0], mine,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere),
            acceptCurrentAction: stamp =>
            {
                accepted = stamp;
                return true;
            });

        // A held draft, so reconciliation offers rather than adopts and the artist has a decision to make.
        vm.DisablePressure = true;
        await Settle();
        Assert.True(vm.HasExternalChange);

        EditorReconciliation.Forward(
            new PreparedSettings(older, olderStamp),
            new Dictionary<string, TabletDetailViewModel> { ["T"] = vm });

        vm.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        Assert.Equal(olderStamp, accepted);
        Assert.True(vm.DisableTilt, "the settings that arrived were not the ones that were forwarded");

        vm.Dispose();
    }

    /// <summary>A publication of nothing leaves every editor alone.</summary>
    /// <remarks>
    /// Before anything has loaded there is no snapshot to reconcile against, and handing editors a null
    /// one would be telling them the daemon holds nothing.
    /// </remarks>
    [AvaloniaFact]
    public async Task ForwardingNothing_LeavesEditorsAlone()
    {
        var mine = SettingsWithForeignFilter();
        var vm = new TabletDetailViewModel(mine.Profiles[0], mine,
            applyAction: _ => Task.FromResult(SettingsApplyOutcome.ChangedElsewhere));

        vm.DisablePressure = true;
        await Settle();

        EditorReconciliation.Forward(
            null, new Dictionary<string, TabletDetailViewModel> { ["T"] = vm });

        Assert.True(vm.DisablePressure, "the editor's own draft was disturbed by an empty publication");

        vm.Dispose();
    }

    private sealed record TwoEditors(
        FakeDaemonTransport Daemon,
        AppSession Session,
        TabletDetailViewModel First,
        TabletDetailViewModel Second);

    /// <summary>Two tablets, so the shell caches two editors over one session (#906).</summary>
    private static Settings TwoTablets()
    {
        var settings = new Settings { Profiles = new ProfileCollection() };
        foreach (var name in new[] { "T", "U" })
        {
            var profile = new Profile { Tablet = name };
            profile.BindingSettings.WheelBindings.Add(new WheelBindingSettings());
            settings.Profiles.Add(profile);
        }

        return settings;
    }

    /// <summary>
    /// Two real editors over one real session, built the way the shell builds them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Real on both sides on purpose. The hole these tests are about lived in what the editors and the
    /// session shared, so a fake on either side of that boundary is a fake of the thing under test: one
    /// editor with a stubbed session cannot collide with anybody, and a stubbed editor cannot hold a
    /// draft of its own.
    /// </para>
    /// <para>
    /// Scope, precisely: this constructs the two editors through <see cref="DialogService"/> directly. It
    /// does not drive <c>MainViewModel</c>'s cache or navigation, so what the tests below establish is
    /// that two editors sharing a session cannot resolve each other's drafts — not that the shell's cache
    /// produces two of them. That it does is the premise, and it is covered where the cache lives.
    /// </para>
    /// </remarks>
    private static async Task<TwoEditors> TwoRealEditors()
    {
        var daemon = new FakeDaemonTransport
        {
            Settings = TwoTablets(),
            AppInfo = new AppInfo { AppDataDirectory = "x", SettingsFile = "settings.json", PluginDirectory = "" },
        };
        var session = new AppSession(FakeSession.Over(daemon, new NoopStore()), new StubLifecycle())
        {
            Ownership = DaemonOwnership.Owned,
        };
        await session.ReloadAsync();

        var dialogs = new DialogService(session);
        var first = dialogs.CreateTabletDetail("T", () => Task.CompletedTask);
        var second = dialogs.CreateTabletDetail("U", () => Task.CompletedTask);
        Assert.NotNull(first);
        Assert.NotNull(second);

        session.DataLoaded += () =>
        {
            var current = session.CurrentSettings;
            var stamp = session.CurrentStamp;
            first!.ReconcileExternalChange(
                current, current?.Profiles.FirstOrDefault(p => p.Tablet == "T"), stamp);
            second!.ReconcileExternalChange(
                current, current?.Profiles.FirstOrDefault(p => p.Tablet == "U"), stamp);
        };

        return new TwoEditors(daemon, session, first!, second!);
    }

    /// <summary>
    /// One artist resolving their own held change does not resolve somebody else's (#906).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Editors are cached, so two of them are live at once, and either can be holding a draft. The
    /// expectation a held draft is compared against used to be one field on the session, shared by both.
    /// Whichever artist resolved first cleared it — and the other's draft, still built on the state it
    /// was held against, was then weighed against the daemon's current settings, found to agree with
    /// them, and written. One person's decision about their own tablet silently authorised a write over
    /// an edit nobody had shown the other.
    /// </para>
    /// <para>
    /// The expectation now travels with the draft, so there is no shared thing for a decision to clear.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task OneEditorTakingTheDaemonsVersion_DoesNotResolveTheOthersHeldChange()
    {
        var (daemon, session, first, second) = await TwoRealEditors();
        using var _s = session;

        // Somebody else edits the daemon. Both artists edit on top of a state that has moved.
        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        first.DisablePressure = true;
        second.DisablePressure = true;
        await Settle();

        Assert.True(first.HasExternalChange, "the first editor's change should be held");
        Assert.True(second.HasExternalChange, "and so should the second's");

        // A reload gives both banners something to show, and the first artist takes the daemon's version.
        await session.ReloadAsync();
        await Settle();
        first.ReloadExternalChangeCommand.Execute(null);
        await Settle();

        // The second artist, who has decided nothing, carries on editing. Its own tablet's tilt, which
        // is a plain profile setting and so submits on every platform — and is a different profile from
        // the one the external edit touched, so it cannot mask the assertion below.
        var readsBeforeTheSecondEdit = daemon.GetSettingsCalls;
        second.DisableTilt = !second.DisableTilt;
        await Settle();

        Assert.True(daemon.Settings!.Profiles[0].BindingSettings.DisableTilt,
            "the second editor overwrote an edit that only the first artist had agreed to replace");
        Assert.True(daemon.GetSettingsCalls > readsBeforeTheSecondEdit,
            "the second editor submitted nothing, so this proves nothing about holding it");
        Assert.True(second.HasExternalChange, "and its own change is still held, because it still is");

        first.Dispose();
        second.Dispose();
    }

    /// <summary>
    /// Nor does resolving it by writing over it, which cleared the same shared field (#906).
    /// </summary>
    /// <remarks>
    /// The other release. A write that landed cleared the session's one expectation just as acceptance
    /// did, so keeping your change had the same reach into somebody else's draft as taking theirs — and
    /// this is the route an artist is more likely to take, since it is the one that keeps their work.
    /// </remarks>
    [AvaloniaFact]
    public async Task OneEditorKeepingItsChange_DoesNotResolveTheOthersHeldChange()
    {
        var (daemon, session, first, second) = await TwoRealEditors();
        using var _s = session;

        var theirs = Clone(daemon.Settings!);
        theirs.Profiles[0].BindingSettings.DisableTilt = true;
        daemon.Settings = theirs;

        first.DisablePressure = true;
        second.DisablePressure = true;
        await Settle();

        Assert.True(first.CanOverwriteHeldChange, "the first artist should be offered the choice");

        // The first artist keeps their change, which is a write that lands.
        first.OverwriteHeldChangeCommand.Execute(null);
        await PumpUntil(() => !first.HasExternalChange, "the first editor's change to go through");

        var afterTheirWrite = Clone(daemon.Settings!);

        // The second artist, who decided nothing, carries on editing.
        var readsBeforeTheSecondEdit = daemon.GetSettingsCalls;
        second.DisableTilt = !second.DisableTilt;
        await Settle();

        Assert.Equal(
            Newtonsoft.Json.JsonConvert.SerializeObject(afterTheirWrite),
            Newtonsoft.Json.JsonConvert.SerializeObject(daemon.Settings));
        Assert.True(daemon.GetSettingsCalls > readsBeforeTheSecondEdit,
            "the second editor submitted nothing, so this proves nothing about holding it");
        Assert.True(second.HasExternalChange, "the second editor's change should still be held");

        first.Dispose();
        second.Dispose();
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
            // With the stamp, as the shell passes it: an acceptance has to name the snapshot it took,
            // and a harness that omitted it would make every acceptance stale (#910).
            vm!.ReconcileExternalChange(
                current, current?.Profiles.FirstOrDefault(p => p.Tablet == "T"), session.CurrentStamp);
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
