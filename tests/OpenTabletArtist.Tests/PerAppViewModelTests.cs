using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PerAppViewModelTests
{
    private sealed class FakeWatcher : IForegroundAppWatcher
    {
#pragma warning disable CS0067
        public event Action<AppIdentity>? Changed;
#pragma warning restore CS0067
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeDebounce : IDebounceScheduler
    {
        public void Schedule(Action action) { }
        public void Cancel() { }
    }

    private sealed class FakeApplier : IPerAppApplier
    {
        public Task ApplyDefaultAsync() => Task.CompletedTask;
        public Task<bool> ApplySnapshotAsync(string name) => Task.FromResult(true);
    }

    private static string TempDirWith(params string[] snapshotNames)
    {
        var d = Path.Combine(Path.GetTempPath(), $"otaperapp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        foreach (var n in snapshotNames) File.WriteAllText(Path.Combine(d, n + ".json"), "{}");
        return d;
    }

    private static PerAppViewModel NewVm(PerAppProfileStore store, FakeDeviceData device)
    {
        var switcher = new PerAppSwitcher(new FakeWatcher(), store, new FakeApplier(),
            new FakeDebounce(), ownExeName: "OpenTabletArtist.exe");
        return new PerAppViewModel(switcher, store, device, new FakeDialogService(), new FakeConnectionState());
    }

    [Fact]
    public async Task UnmappedTarget_Profile_PersistsAsDefault_CurrentSettingsClearsIt()
    {
        var dir = TempDirWith("Painting", "Gaming");
        try
        {
            string? backing = null;
            var store = new PerAppProfileStore(() => backing, v => backing = v);
            var device = new FakeDeviceData { PresetDirectory = dir };
            var vm = NewVm(store, device);

            await vm.LoadAsync();

            vm.UnmappedTarget = "Gaming";
            Assert.Equal("Gaming", store.Config.DefaultSnapshot);

            vm.UnmappedTarget = PerAppViewModel.CurrentSettingsOption;
            Assert.Null(store.Config.DefaultSnapshot);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LoadAsync_RestoresUnmappedTargetFromStoredDefault()
    {
        var dir = TempDirWith("Painting", "Gaming");
        try
        {
            string? backing = null;
            var store = new PerAppProfileStore(() => backing, v => backing = v);
            store.SetDefaultSnapshot("Gaming");
            var device = new FakeDeviceData { PresetDirectory = dir };
            var vm = NewVm(store, device);

            await vm.LoadAsync();

            Assert.Equal("Gaming", vm.UnmappedTarget);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
