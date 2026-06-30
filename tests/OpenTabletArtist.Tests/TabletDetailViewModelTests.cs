using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TabletDetailViewModelTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";

    private static Settings SettingsWith(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    private static Settings AbsoluteSettingsWith(string tablet)
    {
        var settings = SettingsWith(tablet);
        // Mirror how the VM builds an output-mode store: the (object,…) ctor sets Path to the type
        // name, so the actual mode path is assigned afterward (FromPath needs the plugin loaded).
        settings.Profiles.First().OutputMode = new PluginSettingStore(WinInkAbsolute) { Path = WinInkAbsolute };
        return settings;
    }

    // Regression for #124: the dialog persists edits by mutating _profile then pushing _settings.
    // After an in-dialog Refresh, _settings must move to the reloaded settings the new _profile
    // lives in — otherwise edits push the stale pre-refresh settings object.
    [Fact]
    public async Task Refresh_ThenPersist_PushesReloadedSettings_NotStaleOne()
    {
        var original = SettingsWith("T");
        var reloaded = SettingsWith("T");
        Settings? pushed = null;

        var vm = new TabletDetailViewModel(
            original.Profiles.First(),
            original,
            applyAction: s => { pushed = s; return Task.CompletedTask; },
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((reloaded, reloaded.Profiles.First())));

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.FixOutputModeCommand.ExecuteAsync(null); // any persist path goes through _settings

        Assert.Same(reloaded, pushed);
        Assert.Null(vm.RefreshWarning);
    }

    // Cursor review nit on #125: if the tablet is gone when Refresh runs (profile == null), the
    // dialog keeps the last-known settings, warns, and keeps persisting through them (no NRE/no-op
    // that silently writes elsewhere).
    [Fact]
    public async Task Refresh_ProfileGone_Warns_AndKeepsPersistingThroughOriginalSettings()
    {
        var original = SettingsWith("T");
        Settings? pushed = null;

        var vm = new TabletDetailViewModel(
            original.Profiles.First(),
            original,
            applyAction: s => { pushed = s; return Task.CompletedTask; },
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((null, null)));

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrEmpty(vm.RefreshWarning));

        await vm.FixOutputModeCommand.ExecuteAsync(null);
        Assert.Same(original, pushed);
    }

    // #179 follow-up: a changed display selection must read as "pending" until Apply, but the dialog
    // must not open already-pending before the user touches anything.
    [Fact]
    public void MappingChangePending_FalseOnOpen_TrueAfterChangingSelection()
    {
        var settings = SettingsWith("T");
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(), settings,
            applyAction: _ => Task.CompletedTask);

        Assert.False(vm.MappingChangePending); // as opened, nothing changed

        // Selecting a display that isn't the applied mapping marks the change pending.
        vm.SelectedDisplayNumber = 999;
        Assert.True(vm.MappingChangePending);
    }

    [Fact]
    public void MappingChangePending_False_WhenNoApplyAction()
    {
        var settings = SettingsWith("T");
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings); // read-only (no apply)

        vm.SelectedDisplayNumber = 999;

        Assert.False(vm.MappingChangePending); // can't apply, so never "pending"
    }

    // #177: calibration needs an Absolute mode AND a live tablet. With absolute mode but the tablet
    // not (yet) connected, the calibration UI shows but the action is disabled, with a connect hint.
    [Fact]
    public void CanRunCalibration_RequiresAbsoluteMode_AndDetectedTablet()
    {
        var settings = AbsoluteSettingsWith("T");
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(), settings,
            applyAction: _ => Task.CompletedTask,
            isDetected: () => false,
            onCalibrate: _ => Task.CompletedTask); // a host that can run calibration

        Assert.True(vm.CanCalibrate);                // absolute mode → section visible
        Assert.False(vm.CanRunCalibration);          // ...but not connected → button disabled
        Assert.True(vm.ShowConnectToCalibrateHint);
    }

    // #177: a tablet plugged in after the dialog opened must update the banner and enable calibration
    // live, without reopening — the View calls RefreshDetectionStatus() on the session's DataLoaded.
    [Fact]
    public void RefreshDetectionStatus_ReflectsLateConnect_LiveAndNotifies()
    {
        bool detected = false;
        var settings = AbsoluteSettingsWith("T");
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(), settings,
            applyAction: _ => Task.CompletedTask,
            isDetected: () => detected,
            onCalibrate: _ => Task.CompletedTask); // a host that can run calibration

        Assert.False(vm.IsTabletDetected);
        Assert.False(vm.CanRunCalibration);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Tablet connects; the live re-check picks it up.
        detected = true;
        vm.RefreshDetectionStatus();

        Assert.True(vm.IsTabletDetected);
        Assert.Equal("Connected", vm.DetectionText);
        Assert.True(vm.CanRunCalibration);
        Assert.False(vm.ShowConnectToCalibrateHint);
        // The dependent gates re-notify so the button/hint update without reopening the dialog.
        Assert.Contains(nameof(vm.CanRunCalibration), changed);
        Assert.Contains(nameof(vm.ShowConnectToCalibrateHint), changed);
    }

    // #177: detection alone isn't enough — a connected tablet in a non-Absolute mode still can't be
    // calibrated (calibration only corrects an Absolute mapping).
    [Fact]
    public void CanRunCalibration_False_WhenDetected_ButNotAbsoluteMode()
    {
        var settings = SettingsWith("T"); // no Absolute output mode set
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(), settings,
            applyAction: _ => Task.CompletedTask,
            isDetected: () => true);

        Assert.True(vm.IsTabletDetected);
        Assert.False(vm.CanCalibrate);
        Assert.False(vm.CanRunCalibration);
        Assert.False(vm.ShowConnectToCalibrateHint); // not absolute → the "connect" hint doesn't apply
    }
}
