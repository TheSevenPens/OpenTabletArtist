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
}
