using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

// The page shows what the view model knows: the "Last loaded" chip on one row, and the sort choice.
public class PresetsViewTests
{
    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }

    [AvaloniaFact]
    public async Task TheLoadedPreset_CarriesTheChip_AndTheSortBoxIsBound()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"otapresetview_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        AppSettings.Remove(PresetsViewModel.SortByNameKey);
        try
        {
            var store = new PresetStore(NullOtdLog.Instance);
            store.Save(new Settings(), Path.Combine(dir, "Inking.json"));
            store.Save(new Settings(), Path.Combine(dir, "Sketching.json"));
            var coordinator = new FakeSettingsCoordinator { CurrentSettings = new Settings() };
            var vm = new PresetsViewModel(store, coordinator, new FakeDeviceData(), new FakeDialogService(), new NoHotkeys(),
                new ProfileSwitchService(coordinator, store, () => dir))
            { PresetDirectory = dir };
            await vm.LoadAsync();
            var view = new PresetsView { DataContext = vm };
            var window = HarnessTests.Show(view);

            await vm.LoadPresetCommand.ExecuteAsync("Inking");
            Dispatcher.UIThread.RunJobs();

            var chips = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("LoadedChip") && b.IsEffectivelyVisible).ToList();
            var chip = Assert.Single(chips);
            Assert.Equal("Inking", Assert.IsType<PresetInfo>(chip.DataContext).Name);

            var sort = view.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Sort by name"));
            sort.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.SortByName);
            window.Close();
        }
        finally
        {
            AppSettings.Remove(PresetsViewModel.SortByNameKey);
            Directory.Delete(dir, true);
        }
    }
}
