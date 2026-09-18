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

    private sealed class FakeDebounce : IDebounceScheduler
    {
        private Action? _pending;
        public void Schedule(Action action) => _pending = action;
        public void Cancel() => _pending = null;
        public void Fire() { var a = _pending; _pending = null; a?.Invoke(); }
    }

    /// <summary>
    /// An applier whose completion the test controls. Immediately-completed fakes cannot exercise an
    /// ordering bug: every apply finishes before the next request arrives, so two switches never overlap
    /// and a stale one never has the chance to publish over a newer one (#737).
    ///
    /// By default it still completes synchronously, so the existing policy tests read as before. Set
    /// <see cref="Gate"/> to hold applies open and release them in whatever order the test needs.
    /// </summary>
    private sealed class FakeApplier : IPerAppApplier
    {
        public List<string> Calls { get; } = new();          // "default" or the snapshot name

        /// <summary>Result for the next <see cref="ApplySnapshotAsync"/>. Per-snapshot entries in
        /// <see cref="Results"/> win over this.</summary>
        public PerAppApplyResult SnapshotResult { get; set; } = PerAppApplyResult.Applied;

        /// <summary>Per-snapshot outcomes, for tests where one target fails and another doesn't.</summary>
        public Dictionary<string, PerAppApplyResult> Results { get; } = new(StringComparer.Ordinal);

        public bool DefaultSucceeds { get; set; } = true;

        /// <summary>When set, every apply waits on this before returning. The test decides when — and in
        /// which order — they complete.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Per-snapshot gates, so A and B can be released independently.</summary>
        public Dictionary<string, TaskCompletionSource> Gates { get; } = new(StringComparer.Ordinal);

        public async Task<bool> ApplyDefaultAsync()
        {
            Calls.Add("default");
            await WaitFor("default");
            return DefaultSucceeds;
        }

        public async Task<PerAppApplyResult> ApplySnapshotAsync(string name)
        {
            Calls.Add(name);
            await WaitFor(name);
            return Results.TryGetValue(name, out var r) ? r : SnapshotResult;
        }

        private async Task WaitFor(string key)
        {
            if (Gates.TryGetValue(key, out var own)) { await own.Task; return; }
            if (Gate != null) await Gate.Task;
        }
    }

    private sealed class Harness
    {
        public readonly FakeWatcher Watcher = new();
        public readonly FakeDebounce Debounce = new();
        public readonly FakeApplier Applier = new();
        public readonly PerAppProfileStore Store;
        public readonly PerAppSwitcher Switcher;

        public Harness()
        {
            string? backing = null;
            Store = new PerAppProfileStore(() => backing, v => backing = v);
            Switcher = new PerAppSwitcher(Watcher, Store, Applier, Debounce, ownExeName: "OpenTabletArtist.exe");
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
    public void DanglingSnapshot_FallsBackToDefault_AndWarns()
    {
        var h = new Harness();
        h.Applier.SnapshotResult = PerAppApplyResult.SnapshotMissing;
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

    // --- Ordering and confirmed success (#737) ---

    /// <summary>
    /// The reproduced defect. Dedupe returned early when the newly focused app resolved to the target
    /// already applied — without cancelling a DIFFERENT target already sitting in the debounce. So
    /// returning to the app you were just in switched you away from it.
    /// </summary>
    [Fact]
    public void ReturningToTheCurrentApp_CancelsAQueuedSwitchAway()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Store.Upsert(new PerAppMapping("", "blender.exe", "Sculpting"));

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();   // A applied
        h.Applier.Calls.Clear();

        h.Watcher.Raise("blender.exe");                    // B queued
        h.Watcher.Raise("krita.exe");                      // back to A before the window elapses
        h.Debounce.Fire();                                 // whatever is still queued fires

        Assert.Empty(h.Applier.Calls);                     // B was dropped, A was never re-applied
        Assert.Equal("Painting", h.Switcher.ActiveProfile);
    }

    /// <summary>Two applies in flight must not interleave, and the one that started first must not
    /// publish its label after the newer one. Both are held open, then released oldest-first — the order
    /// that used to produce a stale label.</summary>
    [Fact]
    public async Task WhenAnOlderApplyFinishesLast_ItDoesNotPublishOverTheNewerOne()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Store.Upsert(new PerAppMapping("", "blender.exe", "Sculpting"));
        var gateA = new TaskCompletionSource();
        var gateB = new TaskCompletionSource();
        h.Applier.Gates["Painting"] = gateA;
        h.Applier.Gates["Sculpting"] = gateB;

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();     // A starts, blocks
        h.Watcher.Raise("blender.exe"); h.Debounce.Fire();   // B requested while A is in flight

        gateA.SetResult();                                   // let the older one finish first
        gateB.SetResult();
        await h.Switcher.WaitForIdleAsync();

        Assert.Equal("Sculpting", h.Switcher.ActiveProfile); // the newest target, not the older one
    }

    /// <summary>
    /// A failed apply used to be recorded as a success, so the dedupe swallowed every retry: the tablet
    /// kept the previous app's profile, the UI named the new one, and focusing that app again did nothing.
    /// </summary>
    [Fact]
    public async Task AFailedApply_CommitsNothing_SoTheNextFocusChangeRetries()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Applier.SnapshotResult = PerAppApplyResult.ApplyFailed;

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        await h.Switcher.WaitForIdleAsync();

        Assert.Null(h.Switcher.ActiveProfile);               // nothing claimed
        Assert.Equal(new[] { "Painting" }, h.Applier.Calls);

        // Focusing it again must try again rather than dedupe against a switch that never happened.
        h.Applier.SnapshotResult = PerAppApplyResult.Applied;
        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        await h.Switcher.WaitForIdleAsync();

        Assert.Equal("Painting", h.Switcher.ActiveProfile);
        Assert.Equal(new[] { "Painting", "Painting" }, h.Applier.Calls);
    }

    /// <summary>A failed apply is not a dangling snapshot: the profile is fine, the daemon just didn't
    /// take it. Warning about a deleted profile would send the user looking for a problem that isn't there.</summary>
    [Fact]
    public async Task AFailedApply_DoesNotWarnAboutADanglingSnapshot_OrFallBackToDefault()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Applier.SnapshotResult = PerAppApplyResult.ApplyFailed;
        string? warned = null;
        h.Switcher.DanglingSnapshot += n => warned = n;

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        await h.Switcher.WaitForIdleAsync();

        Assert.Null(warned);
        Assert.Equal(new[] { "Painting" }, h.Applier.Calls);  // no "default" fallback
    }

    /// <summary>A dangling snapshot whose fallback to the default also fails must not claim to be on the
    /// default — the tablet is still on the previous app's profile.</summary>
    [Fact]
    public async Task ADanglingSnapshotWhoseFallbackFails_DoesNotClaimTheDefault()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();
        await h.Switcher.WaitForIdleAsync();

        h.Store.Upsert(new PerAppMapping("", "blender.exe", "Gone"));
        h.Applier.Results["Gone"] = PerAppApplyResult.SnapshotMissing;
        h.Applier.DefaultSucceeds = false;

        h.Watcher.Raise("blender.exe"); h.Debounce.Fire();
        await h.Switcher.WaitForIdleAsync();

        Assert.Equal("Painting", h.Switcher.ActiveProfile);   // unchanged; nothing was confirmed
    }

    /// <summary>Stopping while an apply is in flight must wait for it, or that apply lands after the
    /// restore and leaves the tablet on a per-app profile with nothing watching it.</summary>
    [Fact]
    public async Task Stop_WhileApplying_WaitsAndStillEndsOnTheDefault()
    {
        var h = new Harness();
        h.Store.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        var gate = new TaskCompletionSource();
        h.Applier.Gates["Painting"] = gate;

        h.Watcher.Raise("krita.exe"); h.Debounce.Fire();   // apply starts, blocks
        var stopping = h.Switcher.StopAsync();
        Assert.False(stopping.IsCompleted, "Stop returned while an apply was still running.");

        gate.SetResult();
        await stopping;

        Assert.Null(h.Switcher.ActiveProfile);
        Assert.Equal("default", h.Applier.Calls[^1]);      // the restore ran last
    }
}
