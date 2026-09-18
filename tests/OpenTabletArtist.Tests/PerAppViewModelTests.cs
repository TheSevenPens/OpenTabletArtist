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
        public Task<bool> ApplyDefaultAsync() => Task.FromResult(true);
        public Task<PerAppApplyResult> ApplySnapshotAsync(string name) =>
            Task.FromResult(PerAppApplyResult.Applied);
    }

    private static string TempDirWith(params string[] snapshotNames)
    {
        var d = Path.Combine(Path.GetTempPath(), $"otaperapp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        foreach (var n in snapshotNames) File.WriteAllText(Path.Combine(d, n + ".json"), "{}");
        return d;
    }

    private static PerAppViewModel NewVm(PerAppProfileStore store, FakeDeviceData device,
        FakeConnectionState? connection = null)
    {
        var switcher = new PerAppSwitcher(new FakeWatcher(), store, new FakeApplier(),
            new FakeDebounce(), ownExeName: "OpenTabletArtist.exe");
        return new PerAppViewModel(switcher, store, device, new FakeDialogService(),
            connection ?? new FakeConnectionState());
    }

    // --- Whose daemon it is decides whether this feature may run at all (#742) ---

    private static PerAppViewModel VmFor(DaemonOwnership ownership)
    {
        string? backing = null;
        var connection = new FakeConnectionState { IsConnected = true, Ownership = ownership };
        return NewVm(new PerAppProfileStore(() => backing, v => backing = v), new FakeDeviceData(), connection);
    }

    [Fact]
    public void OnOurOwnDaemon_TheFeatureIsAvailable()
    {
        var vm = VmFor(DaemonOwnership.Owned);

        Assert.True(vm.CanUse);
        Assert.False(vm.ShowDaemonBlockedNotice);
    }

    /// <summary>
    /// The regression: <c>CanUse</c> was <c>!IsForeignDaemon</c>, which is also true when OTA cannot tell
    /// whose daemon it is. This feature rewrites settings on every focus change, so it enabled itself in
    /// exactly the case OTA knew least about.
    /// </summary>
    [Theory]
    [InlineData(DaemonOwnership.External)]
    [InlineData(DaemonOwnership.Unknown)]
    public void OnAnyOtherDaemon_TheFeatureIsUnavailable_AndSaysWhy(DaemonOwnership ownership)
    {
        var vm = VmFor(ownership);

        Assert.False(vm.CanUse);
        Assert.True(vm.ShowDaemonBlockedNotice);
        Assert.NotEmpty(vm.DaemonBlockedText);
    }

    /// <summary>The two blocked cases need different advice — one daemon can be stopped, the other is
    /// probably elevated and wants looking at, not stopping.</summary>
    [Fact]
    public void TheBlockedExplanationDistinguishesTheirDaemonFromAnUnreadableOne()
    {
        var theirs = VmFor(DaemonOwnership.External).DaemonBlockedText;
        var unreadable = VmFor(DaemonOwnership.Unknown).DaemonBlockedText;

        Assert.NotEqual(theirs, unreadable);
        Assert.Contains("didn't start", theirs, StringComparison.Ordinal);
        Assert.Contains("can't tell", unreadable, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenDisconnected_TheBlockedNoticeIsNotShown()
    {
        // Nothing to explain about a daemon that isn't there; the page has its own disconnected state.
        string? backing = null;
        var connection = new FakeConnectionState { IsConnected = false, Ownership = DaemonOwnership.Unknown };
        var vm = NewVm(new PerAppProfileStore(() => backing, v => backing = v), new FakeDeviceData(), connection);

        Assert.False(vm.ShowDaemonBlockedNotice);
    }

    [Fact]
    public void OwnershipChanging_UpdatesAvailability()
    {
        string? backing = null;
        var connection = new FakeConnectionState { IsConnected = true, Ownership = DaemonOwnership.External };
        var vm = NewVm(new PerAppProfileStore(() => backing, v => backing = v), new FakeDeviceData(), connection);
        Assert.False(vm.CanUse);

        connection.Ownership = DaemonOwnership.Owned;

        Assert.True(vm.CanUse);
        Assert.False(vm.ShowDaemonBlockedNotice);
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
