using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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

}
