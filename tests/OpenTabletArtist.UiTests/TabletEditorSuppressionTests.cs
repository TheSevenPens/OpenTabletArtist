using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The tablet editor's load-under-suppression invariant (#751, step 2).
///
/// <c>RefreshFromProfile</c> is the single load hub, and it assigns the same observable properties the
/// user edits. Every one of those assignments would otherwise run the property-changed handler that
/// persists the value — so the load is bracketed by <c>_skip*</c> / <c>_suppress*</c> flags. Combined
/// with <c>ApplySettingsChange</c>, which reloads <em>after</em> awaiting the apply, an unsuppressed load
/// is not merely a redundant write: it is a persist→apply→reload→persist loop.
///
/// That invariant is what makes splitting this class dangerous, and #751 records it as untested. These
/// are the tests it was waiting for. They live in the UI suite because the persist path runs through
/// <c>Dispatcher.UIThread.InvokeAsync</c> and a debounce, neither of which exists in the logic suite.
///
/// Each one has a positive control. "No apply happened" is a claim any broken editor would also satisfy.
///
/// <b>Which of these actually discriminate.</b> Deleting the <c>_skipCurvePersist</c> guard fails exactly
/// two: <see cref="OpeningTheEditor_DoesNotWriteAnything"/> and
/// <see cref="AdoptingAnExternalChange_DoesNotWriteItBack"/>. The two "not a loop" tests still pass, and
/// the reason is worth knowing before anyone relies on them: after the user's own edit the reload writes
/// the value back <em>unchanged</em>, and the generated <c>[ObservableProperty]</c> setter compares before
/// assigning — so no handler runs and no loop forms, flag or no flag.
///
/// So the suppression flags earn their keep on loads that <b>change</b> what is displayed: opening the
/// editor on a stored profile, and adopting someone else's edit. That is the shape a feature-editor split
/// has to preserve. The loop tests stay because "one edit, one write" is its own invariant, but they are
/// not evidence about suppression.
/// </summary>
public class TabletEditorSuppressionTests
{
    /// <summary>Longer than the editor's persist debounces (400 ms for dynamics, and the hover one), so a
    /// persist that was going to happen has happened by the time anything is asserted.</summary>
    private static readonly TimeSpan PastDebounce = TimeSpan.FromMilliseconds(900);

    private static Settings SettingsFor(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    /// <summary>
    /// Runs the dispatcher for <paramref name="window"/> of wall-clock time. The persist path schedules
    /// onto the UI thread through a debounce timer, so the test has to let the loop turn rather than
    /// simply sleeping — a bare await would never run the queued work.
    /// </summary>
    private static async Task Settle(TimeSpan window)
    {
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static (TabletDetailViewModel vm, List<Settings> applies) Editor(
        Settings settings, Func<Task<(Settings?, Profile?)>>? refreshAction = null)
    {
        var applies = new List<Settings>();
        var vm = new TabletDetailViewModel(
            settings.Profiles[0], settings,
            applyAction: s => { applies.Add(s); return Task.FromResult(SettingsApplyOutcome.Saved); },
            refreshAction: refreshAction);
        return (vm, applies);
    }

    // --- The load itself must not write ------------------------------------------------------

    /// <summary>
    /// Constructing the editor loads the profile, which assigns every dynamics and hover property. With
    /// values that differ from the defaults, an unsuppressed load would schedule a persist for each —
    /// writing settings the user never touched, on merely opening the tab.
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningTheEditor_DoesNotWriteAnything()
    {
        var settings = SettingsFor("T");
        // Values deliberately unlike the defaults the load would otherwise settle on.
        PressureCurveProfile.Write(settings, "T",
            new PenDynamicsSettings(PressureCurveSettings.Default, 0.4, 0.3, SmoothAfterCurve: true), enable: true);
        HoverProfile.Write(settings, "T", 7, enable: true, nearProximityOnly: true);

        var (vm, applies) = Editor(settings);
        await Settle(PastDebounce);

        Assert.Empty(applies);
        vm.Dispose();
    }

    /// <summary>
    /// The control for the test above. Without this, "nothing was written" would also be satisfied by an
    /// editor that can no longer write at all — which is the failure a suppression bug would most easily
    /// be mistaken for.
    /// </summary>
    [AvaloniaFact]
    public async Task ButEditingStillWrites()
    {
        var (vm, applies) = Editor(SettingsFor("T"));
        await Settle(PastDebounce);
        Assert.Empty(applies);      // the load, as above

        vm.PressureSmoothing = 0.42;
        await Settle(PastDebounce);

        Assert.NotEmpty(applies);
        vm.Dispose();
    }

    // --- The apply → reload transaction must not loop -----------------------------------------

    /// <summary>
    /// The invariant that matters most, and the reason the editor is hard to split.
    ///
    /// <c>ApplySettingsChange</c> mutates the profile, awaits the apply, and then reloads. The reload
    /// re-assigns the properties that were just edited — so if suppression failed, the reload would
    /// schedule another persist, which applies, which reloads. One edit, unbounded writes.
    ///
    /// Asserting "exactly one" rather than "at least one" is the whole point: a loop passes any weaker
    /// assertion.
    ///
    /// Note this one does not discriminate on the suppression flag — see the class comment. It guards the
    /// invariant, not the mechanism.
    /// </summary>
    [AvaloniaFact]
    public async Task OneEdit_ProducesOneWrite_NotALoop()
    {
        var (vm, applies) = Editor(SettingsFor("T"));
        await Settle(PastDebounce);
        applies.Clear();

        vm.PressureSmoothing = 0.42;
        await Settle(PastDebounce);

        var afterFirstSettle = applies.Count;
        Assert.Equal(1, afterFirstSettle);

        // Let the reload that followed the apply have every chance to schedule another one.
        await Settle(PastDebounce);
        Assert.Equal(afterFirstSettle, applies.Count);

        vm.Dispose();
    }

    /// <summary>Hover has its own flag and its own debounce, and the same loop is available to it.</summary>
    [AvaloniaFact]
    public async Task OneHoverEdit_ProducesOneWrite_NotALoop()
    {
        var (vm, applies) = Editor(SettingsFor("T"));
        await Settle(PastDebounce);
        applies.Clear();

        vm.HoverLimitEnabled = true;
        await Settle(PastDebounce);

        var afterFirstSettle = applies.Count;
        Assert.True(afterFirstSettle >= 1, "enabling the hover limit should have been persisted");

        await Settle(PastDebounce);
        Assert.Equal(afterFirstSettle, applies.Count);

        vm.Dispose();
    }

    // --- Adopting an external change is a load, not an edit -----------------------------------

    /// <summary>
    /// A reload driven by someone else's change — OTD's own UX writing the shared settings.json, or the
    /// 30-second poll — must not be echoed back as a write. Doing so would turn a passive observer into a
    /// participant, and two editors would push changes at each other indefinitely.
    /// </summary>
    [AvaloniaFact]
    public async Task AdoptingAnExternalChange_DoesNotWriteItBack()
    {
        var settings = SettingsFor("T");
        var (vm, applies) = Editor(settings,
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles[0])));
        await Settle(PastDebounce);
        applies.Clear();

        // What the poll does: the same settings, changed underneath, re-read through the refresh path.
        PressureCurveProfile.Write(settings, "T",
            new PenDynamicsSettings(PressureCurveSettings.Default, 0.55, 0.25, SmoothAfterCurve: true), enable: true);
        await vm.RefreshCommand.ExecuteAsync(null);
        await Settle(PastDebounce);

        Assert.Empty(applies);
        vm.Dispose();
    }
}
