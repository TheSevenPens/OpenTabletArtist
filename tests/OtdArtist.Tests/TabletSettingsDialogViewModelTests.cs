using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdArtist.Views;
using Xunit;

namespace OtdArtist.Tests;

public class TabletSettingsDialogViewModelTests
{
    private static Settings SettingsWith(string tablet) =>
        new() { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };

    // Regression for #124: the dialog persists edits by mutating _profile then pushing _settings.
    // After an in-dialog Refresh, _settings must move to the reloaded settings the new _profile
    // lives in — otherwise edits push the stale pre-refresh settings object.
    [Fact]
    public async Task Refresh_ThenPersist_PushesReloadedSettings_NotStaleOne()
    {
        var original = SettingsWith("T");
        var reloaded = SettingsWith("T");
        Settings? pushed = null;

        var vm = new TabletSettingsDialogViewModel(
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

        var vm = new TabletSettingsDialogViewModel(
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
        var vm = new TabletSettingsDialogViewModel(
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
        var vm = new TabletSettingsDialogViewModel(settings.Profiles.First(), settings); // read-only (no apply)

        vm.SelectedDisplayNumber = 999;

        Assert.False(vm.MappingChangePending); // can't apply, so never "pending"
    }
}
