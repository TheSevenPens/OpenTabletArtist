using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

public class ExplicitSettingsTests
{
    private static Settings Document(bool pressureDisabled = false) => new()
    {
        Profiles = new ProfileCollection { new Profile { Tablet = "T",
            BindingSettings = new BindingSettings { DisablePressure = pressureDisabled } } }
    };

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

    private static async Task<(AppSession App, FakeDaemonTransport Daemon, MemorySettingsFileStore Store)>
        Open()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        daemon.GetSettingsHandler = async () => { await Task.Yield(); return daemon.Settings; };
        var store = new MemorySettingsFileStore { Saved = Document() };
        var app = new AppSession(FakeSession.Over(daemon, store), new Lifecycle());
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        return (app, daemon, store);
    }

    [AvaloniaFact]
    public async Task BackgroundRefreshDoesNotHideAFailedSaveOrRetryIt()
    {
        var (app, _, store) = await Open();
        using var lifetime = app;
        await app.ApplySettingsAsync(Document(true));
        store.SaveSucceeds = false;
        Assert.False(await app.SaveSettingsAsync());
        await app.ReloadAsync();
        Assert.Equal(SettingsSaveState.Failed, app.SaveState);
        Assert.Equal(1, store.Attempts);
        store.SaveSucceeds = true;
        Assert.True(await app.SaveSettingsAsync());
        Assert.Equal(SettingsSaveState.Saved, app.SaveState);
    }

    [AvaloniaFact]
    public async Task AppliedUnsavedThenExplicitSavedStateIsReportedOnUiThread()
    {
        var (app, _, store) = await Open();
        using var lifetime = app;
        var offThread = new List<string>();
        app.PropertyChanged += (_, e) =>
        {
            if (!Dispatcher.UIThread.CheckAccess()) offThread.Add(e.PropertyName ?? "");
        };
        Assert.True((await app.ApplySettingsAsync(Document(true))).IsLive);
        Assert.Equal(SettingsSaveState.Unsaved, app.SaveState);
        Assert.Equal(0, store.Attempts);
        Assert.True(await app.SaveSettingsAsync());
        Assert.Equal(SettingsSaveState.Saved, app.SaveState);
        Assert.False(app.HasUnsavedChanges);
        Assert.Empty(offThread);
    }

    [AvaloniaFact]
    public async Task ExternalEditPausesAllSettingsUntilReloadWithoutOverwritingIt()
    {
        var (app, daemon, store) = await Open();
        using var lifetime = app;
        daemon.Settings = Document(true);
        await app.ReloadAsync();
        Assert.True(app.SettingsPaused);
        Assert.False(app.CanEditSettings);
        Assert.False(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure);
        Assert.False((await app.ApplySettingsAsync(Document())).IsLive);
        await app.ReloadSettingsAsync();
        Assert.True(app.CanEditSettings);
        Assert.True(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);
    }

    [AvaloniaFact]
    public async Task ReconnectReplacesWorkspaceAndNotifiesAboutLostUnsavedState()
    {
        var (app, daemon, store) = await Open();
        using var lifetime = app;
        await app.ApplySettingsAsync(Document(true));
        daemon.RaiseDisconnected();
        daemon.Settings = Document();
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings?.Profiles[0].BindingSettings.DisablePressure == false);
        Assert.True(app.IsConnected);
        Assert.True(app.CanEditSettings);
        Assert.Contains("unsaved", app.DiscardedChangeNotice);
        Assert.Single(daemon.Applied);
        Assert.Equal(0, store.Attempts);
    }

    [AvaloniaFact]
    public async Task StopCanBeCancelledByUnsavedChangesPrompt()
    {
        var (app, _, _) = await Open();
        using var lifetime = app;
        await app.ApplySettingsAsync(Document(true));
        var asked = false;
        app.ResolveUnsavedChanges = () => { asked = true; return Task.FromResult(false); };
        await app.StopDaemonCommand.ExecuteAsync(null);
        Assert.True(asked);
        Assert.True(app.IsConnected);
    }

    [AvaloniaFact]
    public async Task SaveFlushesSliderInputAndReloadDropsItWithoutAWrite()
    {
        var (app, daemon, store) = await Open();
        using var lifetime = app;
        var settings = app.CurrentSettings!;
        using var editor = new TabletDetailViewModel(settings.Profiles[0], settings,
            applyAction: s => app.ApplyProfileAsync(s.Profiles[0]));
        editor.PressureSmoothing = 0.25;
        Assert.True(editor.HasPendingEdits);
        Assert.Empty(daemon.Applied);
        await editor.FlushPendingEditsAsync();
        Assert.True(await app.SaveSettingsAsync());
        Assert.Equal(0.25, PressureCurveProfile.Read(store.Saved, "T")!.Value.Dynamics.PressureSmoothing);
        var writes = daemon.Applied.Count;
        editor.PressureSmoothing = 0.5;
        editor.ResetPendingEdits();
        await app.ReloadSettingsAsync();
        editor.ReconcileExternalChange(app.CurrentSettings, app.CurrentSettings!.Profiles[0]);
        await editor.FlushPendingEditsAsync();
        Assert.Equal(0.25, editor.PressureSmoothing);
        Assert.Equal(writes, daemon.Applied.Count);
        Assert.False(editor.HasPendingEdits);
    }

    [AvaloniaFact]
    public async Task RestartRelaunchesTheConnectedExecutableInsteadOfANewSelection()
    {
        var daemon = new FakeDaemonTransport { Settings = Document() };
        var lifecycle = new Lifecycle
        {
            StopAction = () => daemon.RaiseDisconnected(),
            LaunchAction = () => daemon.Reconnect(),
        };
        using var app = new AppSession(FakeSession.Over(daemon), lifecycle);
        daemon.Reconnect();
        await PumpUntil(() => app.CurrentSettings is not null);
        lifecycle.Expected = "selected-for-next-start.exe";
        await app.RestartDaemonCommand.ExecuteAsync(null);
        Assert.Equal("daemon.exe", lifecycle.Launched);
        Assert.True(app.IsConnected);
    }

    /// <summary>
    /// A dialog holding the settings open stops a background refresh adopting under it (#922).
    /// </summary>
    /// <remarks>
    /// Calibration captures the whole document when it opens and submits a profile from it when the
    /// artist finishes. It contributes nothing to the editor-input predicate, because it is not an
    /// editor — so an outside change arriving while it was open was adopted, and its eventual submit
    /// put the captured values back over the top, reverting settings nobody had touched in calibration.
    /// </remarks>
    [AvaloniaFact]
    public async Task WhileADialogHoldsTheSettings_ABackgroundRefreshDoesNotAdopt()
    {
        var (app, daemon, _) = await Open();
        using var lifetime = app;
        app.HasPendingEditorInput = () => false;   // no half-moved slider anywhere

        using var editing = app.ReserveEditing();
        Assert.True(editing.StillCurrent);

        daemon.Settings = Document(true);
        await app.ReloadAsync();

        Assert.True(app.SettingsPaused,
            "a refresh adopted while a dialog was holding the settings open");
        Assert.False(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure);
    }

    /// <summary>
    /// And a dialog whose document was replaced anyway cannot submit what it captured (#922).
    /// </summary>
    /// <remarks>
    /// The hold stops automatic adoption, not an explicit Reload or a reconnect. Refusing loses that
    /// dialog's work, which is the lesser harm: it is one dialog's worth and the artist is present to
    /// repeat it. Applying it silently reverts whatever they changed in between, with nothing to say it
    /// happened.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADialogWhoseDocumentWasReplaced_CannotSubmitWhatItCaptured()
    {
        var (app, daemon, store) = await Open();
        using var lifetime = app;

        using var editing = app.ReserveEditing();
        var captured = app.CurrentSettings!;

        // The artist reloads explicitly while the dialog is still open.
        daemon.Settings = Document(true);
        await app.ReloadSettingsAsync();
        Assert.False(editing.StillCurrent, "the document was replaced under this scope");

        var outcome = await editing.ApplyProfileAsync(captured.Profiles[0]);

        Assert.Equal(SettingsApplyStatus.ChangedElsewhere, outcome.Status);
        Assert.Empty(daemon.Applied);
        Assert.Equal(0, store.Attempts);
        Assert.True(app.CurrentSettings!.Profiles[0].BindingSettings.DisablePressure,
            "the dialog's captured values were written back over the reloaded document");
    }

    private sealed class Lifecycle : IDaemonLifecycleService
    {
        public string Expected { get; set; } = "daemon.exe";
        public string? Launched { get; private set; }
        public Action? StopAction { get; set; }
        public Action? LaunchAction { get; set; }
        public string? ExpectedExePath() => Expected;
        public bool IsAppManaged(string? path) => true;
        public bool HasBundledDaemon() => false;
        public string? FindExe() => "daemon.exe";
        public bool IsRunning() => true;
        public string? Launch(string? executablePath = null)
        { Launched = executablePath; LaunchAction?.Invoke(); return null; }
        public bool Stop(int processId)
        {
            if (StopAction is null) throw new InvalidOperationException("Stop was not approved.");
            StopAction();
            return true;
        }
        public void StopAll() => throw new InvalidOperationException("Stop was not approved.");
        public string? PathOf(int processId) => "daemon.exe";
        public string? SingleRunningDaemonPath() => "daemon.exe";
    }
}
