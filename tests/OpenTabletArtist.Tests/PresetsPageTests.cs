using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.Tests;

// The presets page's list: the last-loaded marker, the name order, and the question before Update.
public class PresetsPageTests : IDisposable
{
    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"otapresetpage_{Guid.NewGuid():N}");
    private readonly FakeSettingsCoordinator _coordinator = new() { CurrentSettings = new Settings() };
    private readonly FakeDialogService _dialogs = new();
    private readonly ProfileSwitchService _switch;

    public PresetsPageTests()
    {
        Directory.CreateDirectory(_dir);
        _switch = new ProfileSwitchService(_coordinator, new PresetStore(NullOtdLog.Instance), () => _dir);
        AppSettings.Remove(PresetsViewModel.SortByNameKey);
    }

    public void Dispose()
    {
        AppSettings.Remove(PresetsViewModel.SortByNameKey);
        Directory.Delete(_dir, true);
    }

    // Each preset written a second after the one before, so newest first has an unambiguous answer.
    private void Write(params string[] oldestFirst)
    {
        var store = new PresetStore(NullOtdLog.Instance);
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        foreach (var name in oldestFirst)
        {
            var path = Path.Combine(_dir, name + ".json");
            store.Save(new Settings(), path);
            File.SetLastWriteTime(path, at);
            at = at.AddSeconds(1);
        }
    }

    private async Task<PresetsViewModel> Page()
    {
        var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), _coordinator, new FakeDeviceData(),
            _dialogs, new NoHotkeys(), _switch)
        { PresetDirectory = _dir };
        await vm.LoadAsync();
        return vm;
    }

    private static string[] Names(PresetsViewModel vm) => vm.Presets.Select(p => p.Name).ToArray();

    [Fact]
    public async Task ThePresetJustLoaded_IsMarked_AndOnlyThatOne()
    {
        Write("Inking", "Sketching");
        var vm = await Page();
        Assert.DoesNotContain(vm.Presets, p => p.IsLastLoaded);

        await vm.LoadPresetCommand.ExecuteAsync("Inking");

        Assert.Equal(new[] { "Inking" }, vm.Presets.Where(p => p.IsLastLoaded).Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task ALoadFromElsewhere_TheTrayOrAHotkey_MovesTheMarkerToo()
    {
        Write("Inking", "Sketching");
        var vm = await Page();
        await vm.LoadPresetCommand.ExecuteAsync("Inking");

        await _switch.SwitchToAsync("Sketching");   // what the tray and the hotkeys call

        Assert.Equal(new[] { "Sketching" }, vm.Presets.Where(p => p.IsLastLoaded).Select(p => p.Name).ToArray());
    }

    /// <summary>The marker is a name, so it follows the file when the preset is renamed. Left behind, it named
    /// nothing, and a later preset given the old name was marked as the one loaded (#1060).</summary>
    [Fact]
    public async Task RenamingTheMarkedPreset_MovesTheMarker_AndFreesTheOldName()
    {
        Write("Inking", "Sketching");
        var dialogs = new FakeDialogService { InputResult = "Painting" };
        var vm = new PresetsViewModel(new PresetStore(NullOtdLog.Instance), _coordinator, new FakeDeviceData(),
            dialogs, new NoHotkeys(), _switch)
        { PresetDirectory = _dir };
        await vm.LoadAsync();
        await vm.LoadPresetCommand.ExecuteAsync("Inking");

        await vm.RenamePresetCommand.ExecuteAsync("Inking");

        Assert.Equal(new[] { "Painting" }, vm.Presets.Where(p => p.IsLastLoaded).Select(p => p.Name).ToArray());

        // Something else now takes the old name; it was never loaded.
        Write("Inking");
        await vm.LoadAsync();
        Assert.False(vm.Presets.Single(p => p.Name == "Inking").IsLastLoaded);
    }

    [Fact]
    public async Task TheMarker_SurvivesARescan()
    {
        Write("Inking", "Sketching");
        var vm = await Page();
        await vm.LoadPresetCommand.ExecuteAsync("Inking");

        await vm.LoadAsync();

        Assert.True(vm.Presets.Single(p => p.Name == "Inking").IsLastLoaded);
    }

    [Fact]
    public async Task NewestFirst_IsTheDefault_AndSortByName_ReordersAndIsRemembered()
    {
        Write("beta", "Alpha", "Gamma");
        var vm = await Page();
        Assert.Equal(new[] { "Gamma", "Alpha", "beta" }, Names(vm));

        vm.SortByName = true;

        Assert.Equal(new[] { "Alpha", "beta", "Gamma" }, Names(vm));
        Assert.Equal(new[] { "Alpha", "beta", "Gamma" }, Names(await Page()));   // next time the page opens
    }

    [Fact]
    public async Task Update_AsksFirst_AndANo_LeavesThePresetAsItWas()
    {
        Write("Inking");
        var path = Path.Combine(_dir, "Inking.json");
        var before = File.ReadAllText(path);
        _coordinator.CurrentSettings = new Settings { LockUsableAreaTablet = true };
        var vm = await Page();

        _dialogs.ConfirmResult = false;
        await vm.UpdatePresetCommand.ExecuteAsync("Inking");
        Assert.Equal(before, File.ReadAllText(path));

        _dialogs.ConfirmResult = true;
        await vm.UpdatePresetCommand.ExecuteAsync("Inking");
        Assert.NotEqual(before, File.ReadAllText(path));
    }
}
