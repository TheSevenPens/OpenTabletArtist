using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Calibration from end to end: the real overlay, over a real session, with slow answers (#924).
/// </summary>
///
/// <remarks>
/// <para>
/// Every other test of this dialog drives the view model against a callback that answers instantly.
/// That is enough for the capture arithmetic and nothing else: the faults Codex found across three
/// rounds all lived in the intervals a fast answer removes — a command arriving while the dialog's own
/// previous write was still out, a helper's refusal overwritten by its caller, a failure the dialog
/// described as something it was not.
/// </para>
/// <para>
/// So these hold the daemon's answers open and press the buttons in the order an artist would. No
/// OpenTabletDriver process is involved; the daemon is <see cref="FakeDaemonTransport"/> and the file
/// store is memory.
/// </para>
/// </remarks>
public class CalibrationJourneyTests
{
    private static readonly TabletDigitizerSpec Digi = new(100, 100, 1000, 1000);
    private static readonly MappingArea Input = new(50, 50, 100, 100);
    private static readonly MappingArea Output = new(960, 540, 1920, 1080);
    private static readonly DisplayInfo Display =
        new(Number: 1, Name: "", Width: 1920, Height: 1080, X: 0, Y: 0, IsPrimary: true);

    private static Settings Document() => new()
    {
        Profiles = new ProfileCollection { new Profile { Tablet = "T" } }
    };

    private sealed class NoPenInput : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private sealed record Overlay(
        AppSession App,
        FakeDaemonTransport Daemon,
        CalibrationViewModel Vm,
        AppSession.ISettingsEditingScope Scope) : IDisposable
    {
        public void Dispose() { Scope.Dispose(); App.Dispose(); }
    }

    /// <summary>Opens the overlay the way <c>DialogService</c> does: one hold, one captured document.</summary>
    private static async Task<Overlay> Open()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document() };
        var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        app.HasPendingEditorInput = () => false;

        var editing = app.ReserveEditing();
        var settings = app.CurrentSettings!;
        var ctx = new CalibrationViewModel.Context(
            "T", Digi, Input, Output, Display, settings,
            s => editing.ApplyProfileAsync(s.Profiles.First(p => p.Tablet == "T")), new NoPenInput());
        return new Overlay(app, daemon, new CalibrationViewModel(ctx), editing);
    }

    private static async Task PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "UI did not settle.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A full hold on one target, which is what commits a point (#457).</summary>
    private static void Tap(CalibrationViewModel vm, int target)
    {
        var t = vm.Targets[target];
        var desktop = new Vector2((float)(Display.X + t.X * Display.Width),
            (float)(Display.Y + t.Y * Display.Height));
        var raw = AbsolutePositionMapper.MapFromDesktop(desktop, Digi, Input, Output)!.Value;
        for (var k = 0; k < CalibrationViewModel.HoldSamplesTarget; k++)
            vm.OnSample(new PenSample(0, 0, raw.X, raw.Y, 0.5, 0, 0, 0, IsDown: true));
    }

    private static void TapAll(CalibrationViewModel vm)
    {
        for (var i = 0; i < vm.Targets.Count; i++) Tap(vm, i);
    }

    private static bool CalibrationEnabled(Settings? settings) =>
        CalibrationProfile.ReadProfile(settings?.Profiles.FirstOrDefault(p => p.Tablet == "T"))
            is { Enabled: true };

    /// <summary>
    /// Redo does not talk an interrupted overlay back into capturing (#924).
    /// </summary>
    /// <remarks>
    /// The helper reported the refusal and set the phase; its caller then set Capturing over the top and
    /// replaced the explanation with "Rest the pen on the top-left target". Nothing had been written, and
    /// nothing the artist did next could be. I had fixed the same line in Undo and Start and missed this
    /// one, which is exactly the kind of thing a per-caller rule loses.
    /// </remarks>
    [AvaloniaFact]
    public async Task RedoDoesNotRestartACalibrationThatCannotBeSubmitted()
    {
        using var overlay = await Open();

        TapAll(overlay.Vm);
        await overlay.Vm.RedoCommand.ExecuteAsync(null);       // settles the preview first
        Assert.Equal(CalibrationViewModel.Phase.Capturing, overlay.Vm.CurrentPhase);

        TapAll(overlay.Vm);
        await PumpUntil(() => overlay.Vm.IsConfirming);

        // Another hand replaces the document while the overlay is up.
        Assert.True((await overlay.App.ApplySettingsAsync(Document())).IsLive);
        Assert.False(overlay.Scope.StillCurrent);

        var writes = overlay.Daemon.Applied.Count;
        await overlay.Vm.RedoCommand.ExecuteAsync(null);

        Assert.Equal(CalibrationViewModel.Phase.Interrupted, overlay.Vm.CurrentPhase);
        Assert.Contains("not applied", overlay.Vm.Instruction);
        Assert.Equal(writes, overlay.Daemon.Applied.Count);
    }

    /// <summary>
    /// Cancel during the overlay's own unfinished preview still restores what was there (#924).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hold counts a write from the moment it is submitted, so between submitting a preview and its
    /// answer coming back the overlay looked stale <em>to itself</em>. Cancel was refused on that basis
    /// and closed anyway, leaving the preview live with nothing anywhere to say so — no competing writer,
    /// no reconnect, just the artist pressing Cancel while the driver was still thinking.
    /// </para>
    /// <para>
    /// The overlay's commands run one at a time now, so Cancel waits for the preview it is undoing.
    /// That is also what makes a refusal mean what the message says: after waiting, the only thing left
    /// that can refuse is another hand.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task CancelDuringItsOwnPreview_StillRestoresWhatWasThere()
    {
        using var overlay = await Open();

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay.Daemon.SetSettingsHandler = _ =>
        {
            overlay.Daemon.SetSettingsHandler = null;   // only the preview waits
            reached.TrySetResult();
            return held.Task;
        };

        TapAll(overlay.Vm);
        await PumpUntil(() => reached.Task.IsCompleted);

        var closed = false;
        overlay.Vm.CloseRequested += () => closed = true;
        var cancelling = overlay.Vm.CancelCommand.ExecuteAsync(null);

        held.SetResult(true);
        await cancelling;
        await PumpUntil(() => closed);

        Assert.False(CalibrationEnabled(overlay.Daemon.Settings),
            "Cancel left its own preview live on the driver");
        Assert.False(CalibrationEnabled(overlay.App.CurrentSettings));
    }

    /// <summary>
    /// An unconfirmed write is not described as a write that never happened (#924).
    /// </summary>
    /// <remarks>
    /// Every non-live outcome said "something else changed these settings, so this calibration was not
    /// applied". A readback that fails after the driver accepted the write is a different fact: the
    /// calibration may well be in effect, and the artist was being told it certainly was not. The library
    /// keeps that distinction; the overlay was throwing it away.
    /// </remarks>
    [AvaloniaFact]
    public async Task AWriteThatCouldNotBeConfirmed_IsNotCalledAWriteThatDidNotHappen()
    {
        using var overlay = await Open();

        // The driver takes the write and then stops answering reads: the apply is real, the confirmation
        // is not available.
        overlay.Daemon.SetSettingsHandler = settings =>
        {
            overlay.Daemon.GetSettingsHandler =
                () => Task.FromException<Settings?>(new InvalidOperationException("no readback"));
            return Task.FromResult(true);
        };

        TapAll(overlay.Vm);
        await PumpUntil(() => overlay.Vm.CurrentPhase != CalibrationViewModel.Phase.Capturing);

        Assert.Equal(CalibrationViewModel.Phase.Interrupted, overlay.Vm.CurrentPhase);
        Assert.DoesNotContain("not applied", overlay.Vm.Instruction);
        Assert.Contains("could not be confirmed", overlay.Vm.Instruction);
        Assert.Equal("Close (Esc)", overlay.Vm.CancelLabel);
    }
}
