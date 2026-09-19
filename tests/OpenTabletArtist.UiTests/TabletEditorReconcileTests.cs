using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
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
        using var session = new AppSession(daemon, new StubLifecycle(), new NoopStore())
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
        public string? GetProcessPath(int processId) => null;
        public string? GetSingleRunningDaemonPath() => null;
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
        var session = new AppSession(daemon, new StubLifecycle(), new NoopStore())
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
        daemon.GetSettingsHandler = () => ++reads == 1 ? first.Task : second.Task;

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
