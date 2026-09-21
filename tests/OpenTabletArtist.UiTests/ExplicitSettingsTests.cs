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
