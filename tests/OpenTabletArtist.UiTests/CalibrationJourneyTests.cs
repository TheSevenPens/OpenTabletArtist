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
/// <para>
/// Most of them drive the view model, with <see cref="Overlay"/> standing in for the window by doing
/// what it does when the overlay closes — stop the pen stream, release the hold. That stand-in is
/// accurate about the order of those two things and says nothing about the window's own start, which is
/// where the next fault turned out to be; the last journey here builds the real
/// <c>CalibrationOverlayWindow</c> for that reason.
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

        /// <summary>How many times reporting was switched on.</summary>
        /// <remarks>
        /// The observable consequence of the overlay starting its pen stream. A closed overlay that
        /// leaves this raised has left the driver reporting for nobody.
        /// </remarks>
        public int Enabled { get; private set; }

        public Task SetTabletDebugAsync(bool enabled)
        {
            if (enabled) Enabled++;
            return Task.CompletedTask;
        }
    }

    private sealed record Overlay(
        AppSession App,
        FakeDaemonTransport Daemon,
        CalibrationViewModel Vm,
        AppSession.ISettingsEditingScope Scope) : IDisposable
    {
        /// <summary>Whether the overlay has asked to be closed.</summary>
        public bool Closed { get; private set; }

        /// <summary>Settings writes the daemon had taken by the time it closed.</summary>
        public int WritesAtClose { get; private set; }

        /// <summary>
        /// Does what the window does when the overlay closes: stop the pen stream, release the hold.
        /// </summary>
        /// <remarks>
        /// A boolean alone says the overlay asked to close and nothing about whether closing meant
        /// anything. What matters afterwards is that no further settings write happens, and that is only
        /// a real question once the stop and the release have actually taken effect.
        /// </remarks>
        public Overlay Watch()
        {
            Vm.CloseRequested += () =>
            {
                if (Closed) return;
                Closed = true;
                WritesAtClose = Daemon.Applied.Count;
                _ = Vm.StopAsync();
                Scope.Dispose();
            };
            return this;
        }

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
        return new Overlay(app, daemon, new CalibrationViewModel(ctx), editing).Watch();
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

    /// <summary>
    /// Waits for everything the overlay had queued, by queueing one more thing behind it.
    /// </summary>
    /// <remarks>
    /// A settling period was the obvious way to assert that nothing further happens, and a weaker one:
    /// an interval that passed without an event is not the same as the work having finished. Codex's
    /// alternative is better and needs nothing added to the overlay — a non-closing command appended
    /// after the close waits behind whatever is still in the queue and is then skipped by the gate, so
    /// awaiting it is an observable boundary rather than a guess at one.
    /// </remarks>
    private static async Task Drained(Overlay overlay)
    {
        await overlay.Vm.RedoCommand.ExecuteAsync(null);
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
        Assert.Contains("Calibration tab", overlay.Vm.Instruction);
        Assert.Equal("Close (Esc)", overlay.Vm.CancelLabel);
    }

    /// <summary>
    /// A tap landing while Cancel waits cannot apply a calibration after the overlay closes (#925).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sequencing put the overlay's operations in order and left closing as merely another operation, so
    /// work admitted before the close still ran after it. Codex's reproduction: hold Cancel's restore
    /// inside the driver, let the artist land the last tap while it waits, then release. Cancel restores
    /// the original and the overlay closes — and then the queued Finish applies the calibration just
    /// captured, over settings nobody is looking at any more.
    /// </para>
    /// <para>
    /// A close request now stops the pen stream and every non-closing command at once, and work already
    /// queued honours that when its turn comes. Closing is a decision, not a position in a queue.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ATapThatLandsWhileCancelWaits_CannotApplyAfterTheOverlayCloses()
    {
        using var overlay = await Open();

        // Three of the four taps are in when the artist gives up and presses Cancel.
        for (var i = 0; i < overlay.Vm.Targets.Count - 1; i++) Tap(overlay.Vm, i);

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay.Daemon.SetSettingsHandler = _ =>
        {
            overlay.Daemon.SetSettingsHandler = null;
            reached.TrySetResult();
            return held.Task;
        };

        var cancelling = overlay.Vm.CancelCommand.ExecuteAsync(null);
        await PumpUntil(() => reached.Task.IsCompleted);

        // The pen is still on the tablet, and the last target completes while the restore is out.
        var captured = overlay.Vm.CapturedCount;
        Tap(overlay.Vm, overlay.Vm.Targets.Count - 1);
        Assert.Equal(captured, overlay.Vm.CapturedCount);

        held.SetResult(true);
        await cancelling;
        await PumpUntil(() => overlay.Closed);
        await Drained(overlay);

        Assert.Equal(overlay.WritesAtClose, overlay.Daemon.Applied.Count);
        Assert.False(CalibrationEnabled(overlay.Daemon.Settings),
            "a calibration was applied after the overlay closed");
    }

    /// <summary>
    /// Keep does not close over a Redo that is still running (#925).
    /// </summary>
    /// <remarks>
    /// Keep raised the close directly instead of taking its turn, so it could be pressed while Redo's
    /// bypass write was still out: the overlay closed, the bypass then landed, and the artist was left
    /// with calibration <em>disabled</em> by the Redo they had abandoned — having just pressed the
    /// button that keeps it. It goes through the same boundary as everything else now, and is refused
    /// while another of the overlay's own operations is in flight.
    /// </remarks>
    [AvaloniaFact]
    public async Task KeepDoesNotCloseOverAnUnfinishedRedo()
    {
        using var overlay = await Open();

        TapAll(overlay.Vm);
        await PumpUntil(() => overlay.Vm.IsConfirming);
        Assert.True(CalibrationEnabled(overlay.Daemon.Settings));

        // Redo's bypass is still out when the artist changes their mind and presses Keep.
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay.Daemon.SetSettingsHandler = _ =>
        {
            overlay.Daemon.SetSettingsHandler = null;
            reached.TrySetResult();
            return held.Task;
        };

        var redoing = overlay.Vm.RedoCommand.ExecuteAsync(null);
        await PumpUntil(() => reached.Task.IsCompleted);

        overlay.Vm.ApplyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(overlay.Closed, "Keep closed the overlay over an operation still in flight");

        held.SetResult(true);
        await redoing;

        Assert.Equal(CalibrationViewModel.Phase.Capturing, overlay.Vm.CurrentPhase);
        Assert.False(overlay.Closed);
    }

    /// <summary>
    /// A command pressed while Cancel is waiting does not run once it has closed (#925).
    /// </summary>
    /// <remarks>
    /// The other half of the same contract, and the half a per-step guard kept hiding: it is not enough
    /// to stop taking new work at the moment of the close, because work already in the queue reaches the
    /// front afterwards. Redo pressed while the restore is out would otherwise bypass the calibration
    /// that was just restored and leave the closed overlay's document disabled.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACommandQueuedBeforeTheCloseDoesNotRunAfterIt()
    {
        using var overlay = await Open();

        TapAll(overlay.Vm);
        await PumpUntil(() => overlay.Vm.IsConfirming);

        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay.Daemon.SetSettingsHandler = _ =>
        {
            overlay.Daemon.SetSettingsHandler = null;
            reached.TrySetResult();
            return held.Task;
        };

        var cancelling = overlay.Vm.CancelCommand.ExecuteAsync(null);
        await PumpUntil(() => reached.Task.IsCompleted);

        // Pressed while the restore is still out, so it queues behind the close.
        var redoing = overlay.Vm.RedoCommand.ExecuteAsync(null);

        held.SetResult(true);
        await cancelling;
        await redoing;
        await PumpUntil(() => overlay.Closed);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(overlay.WritesAtClose, overlay.Daemon.Applied.Count);
        Assert.NotEqual(CalibrationViewModel.Phase.Capturing, overlay.Vm.CurrentPhase);
    }

    /// <summary>
    /// Closing the window during its own startup leaves the driver reporting to nobody (#926).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate governs entry to a step, not what a step does after its own awaits — and startup has
    /// one: it bypasses the existing calibration, waits for the driver, and then turns the pen stream
    /// on. Close the window in that interval and its <c>OnClosed</c> stops the stream first; the
    /// continuation then starts it again, on an overlay nobody can see. The input source keeps a desired
    /// state across its own awaits, so the earlier stop does not win.
    /// </para>
    /// <para>
    /// This is the one journey that builds a real <c>CalibrationOverlayWindow</c>, because the fault is
    /// in the wiring between the window's lifetime and the view model's — which is exactly what the
    /// stand-in elsewhere in this file substitutes for.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingTheWindowDuringStartup_DoesNotLeaveTheDriverReporting()
    {
        var daemon = new FakeDaemonTransport { Settings = WithCalibration() };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document() };
        using var app = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        app.HasPendingEditorInput = () => false;

        var pen = new NoPenInput();
        var editing = app.ReserveEditing();
        var settings = app.CurrentSettings!;
        var ctx = new CalibrationViewModel.Context(
            "T", Digi, Input, Output, Display, settings,
            s => editing.ApplyProfileAsync(s.Profiles.First(p => p.Tablet == "T")), pen);
        var vm = new CalibrationViewModel(ctx);

        // Startup's bypass is held open, so the window is still starting when it is closed.
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daemon.SetSettingsHandler = _ =>
        {
            daemon.SetSettingsHandler = null;
            reached.TrySetResult();
            return held.Task;
        };

        var window = new Views.CalibrationOverlayWindow(vm, Display);
        window.Show();
        await PumpUntil(() => reached.Task.IsCompleted);

        window.Close();
        await PumpUntil(() => !window.IsVisible);
        editing.Dispose();          // what DialogService's using does when the dialog returns

        held.SetResult(true);
        await vm.StartAsync();      // queued behind the startup that was in flight

        Assert.Equal(0, pen.Enabled);
    }

    /// <summary>A document whose tablet already carries an enabled calibration, so startup must bypass.</summary>
    private static Settings WithCalibration()
    {
        var settings = Document();
        CalibrationProfile.Write(settings, "T",
            new CalibrationProfile.CalibrationData(Matrix3x2.Identity, Enabled: true, Fingerprint: "f"));
        return settings;
    }
}
