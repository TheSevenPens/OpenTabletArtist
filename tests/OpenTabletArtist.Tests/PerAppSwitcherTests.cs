using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PerAppSwitcherTests
{
    private sealed class FakeWatcher : IForegroundAppWatcher
    {
        public event Action<AppIdentity>? Changed;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public void Raise(string exeName) => Changed?.Invoke(new AppIdentity("", exeName));
    }

    private sealed class FakePen : IPenStateProvider
    {
        public bool IsDown { get; private set; }
        public event Action<bool>? PenStateChanged;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public void Set(bool down) { IsDown = down; PenStateChanged?.Invoke(down); }
    }

    private sealed class FakeDebounce : IDebounceScheduler
    {
        private Action? _pending;
        public void Schedule(Action action) => _pending = action;
        public void Cancel() => _pending = null;
        public void Fire() { var a = _pending; _pending = null; a?.Invoke(); }
    }

    private sealed class FakeApplier : IPerAppApplier
    {
        public List<string> Calls { get; } = new();          // "default" or the snapshot name
        public bool SnapshotSucceeds { get; set; } = true;
        public Task ApplyDefaultAsync() { Calls.Add("default"); return Task.CompletedTask; }
        public Task<bool> ApplySnapshotAsync(string name) { Calls.Add(name); return Task.FromResult(SnapshotSucceeds); }
    }

    private sealed class Harness
    {
        public readonly FakeWatcher Watcher = new();
        public readonly FakePen Pen = new();
        public readonly FakeDebounce Debounce = new();
        public readonly FakeApplier Applier = new();
        public readonly PerAppProfileStore Store;
        public readonly PerAppSwitcher Switcher;

        public Harness(bool defer = true)
        {
            string? backing = null;
            Store = new PerAppProfileStore(() => backing, v => backing = v);
            Switcher = new PerAppSwitcher(Watcher, Pen, Store, Applier, Debounce, ownExeName: "OpenTabletArtist.exe")
            { DeferUntilPenUp = defer };
            Switcher.Start();
        }
    }

    [Fact]
    public void MappedApp_AppliesItsSnapshot()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));

        h.Watcher.Raise("krita.exe");
        h.Debounce.Fire();

        Assert.Equal(new[] { "Painting" }, h.Applier.Calls);
    }

    [Fact]
    public void UnmappedApp_AppliesDefault()
    {
        var h = new Harness();
        h.Watcher.Raise("random.exe");
        h.Debounce.Fire();
        Assert.Equal(new[] { "default" }, h.Applier.Calls);
    }

    [Fact]
    public void SameTarget_DedupedToOneApply()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Store.Upsert(new PerAppMapping("", "photoshop.exe", "Painting")); // same snapshot

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        h.Watcher.Raise("photoshop.exe"); h.Debounce.Fire(); // resolves to same target → no re-apply

        Assert.Equal(new[] { "Painting" }, h.Applier.Calls);
    }

    [Fact]
    public void OwnWindow_IsIgnored()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();

        h.Watcher.Raise("OpenTabletArtist.exe"); // focusing ourselves must not switch
        h.Debounce.Fire();

        Assert.Equal(new[] { "Painting" }, h.Applier.Calls); // unchanged
    }

    [Fact]
    public void DeferUntilPenUp_HoldsSwitchWhilePenDown_ThenAppliesOnPenUp()
    {
        var h = new Harness(defer: true);
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));

        h.Pen.Set(true);                 // pen down (mid-stroke)
        h.Watcher.Raise("krita.exe");
        h.Debounce.Fire();               // debounce elapses mid-stroke → held, not applied
        Assert.Empty(h.Applier.Calls);

        h.Pen.Set(false);                // pen lifted → pending switch applies
        Assert.Equal(new[] { "Painting" }, h.Applier.Calls);
    }

    [Fact]
    public void DanglingSnapshot_FallsBackToDefault_AndWarns()
    {
        var h = new Harness();
        h.Applier.SnapshotSucceeds = false; // snapshot missing / fails to load
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Gone"));
        string? warned = null;
        h.Switcher.DanglingSnapshot += n => warned = n;

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();

        Assert.Equal(new[] { "Gone", "default" }, h.Applier.Calls);
        Assert.Equal("Gone", warned);
    }

    [Fact]
    public async Task Stop_RestoresDefault_WhenASnapshotWasActive()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        h.Applier.Calls.Clear();

        await h.Switcher.StopAsync();

        Assert.Equal(new[] { "default" }, h.Applier.Calls);
    }
}
