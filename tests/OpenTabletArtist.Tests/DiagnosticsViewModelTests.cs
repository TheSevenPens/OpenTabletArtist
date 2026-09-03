using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DiagnosticsViewModelTests
{
    private sealed class FakeDebugSession : IDaemonDebugSession
    {
        private readonly List<Action<JObject>> _subs = new();

        /// <summary>Number of live DeviceReport subscriptions (tracks the leak edge in #39).
        /// Counts actual subscriptions, so a no-op `-=` on an unsubscribed handler doesn't skew it.</summary>
        public int SubscriberCount => _subs.Count;

        public event Action<JObject>? DeviceReport
        {
            add { if (value != null) _subs.Add(value); }
            remove { if (value != null) _subs.Remove(value); }
        }

        public List<bool> DebugCalls { get; } = new();

        /// <summary>When true, enabling (SetTabletDebugAsync(true)) throws.</summary>
        public bool FailEnable { get; set; }

        public Task SetTabletDebugAsync(bool enabled)
        {
            DebugCalls.Add(enabled);
            if (enabled && FailEnable) throw new InvalidOperationException("enable failed");
            return Task.CompletedTask;
        }

        public void Raise(JObject data) { foreach (var h in _subs.ToArray()) h(data); }
    }

    [Fact]
    public async Task Toggle_WhenConnected_StartsDebugging()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null);

        Assert.True(vm.IsDebugging);
        Assert.Equal(new[] { true }, fake.DebugCalls);
    }

    [Fact]
    public async Task Toggle_WhenNotConnected_DoesNothing()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake); // IsConnected defaults false

        await vm.ToggleDebuggingCommand.ExecuteAsync(null);

        Assert.False(vm.IsDebugging);
        Assert.Empty(fake.DebugCalls);
    }

    [Fact]
    public async Task Toggle_Twice_StopsDebuggingAndDisablesStream()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // start
        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // stop

        Assert.False(vm.IsDebugging);
        Assert.Equal(new[] { true, false }, fake.DebugCalls);
    }

    [Fact]
    public async Task StopDebuggingAsync_WhenIdle_IsNoOp()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.StopDebuggingAsync();

        Assert.False(vm.IsDebugging);
        Assert.Empty(fake.DebugCalls);
    }

    [Fact]
    public async Task Start_SubscribesExactlyOnce_StopUnsubscribes()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // start
        Assert.Equal(1, fake.SubscriberCount);

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // stop
        Assert.Equal(0, fake.SubscriberCount);
    }

    [Fact]
    public async Task FailedEnable_LeavesNoSubscriptionAndStaysStopped()
    {
        var fake = new FakeDebugSession { FailEnable = true };
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // enable throws

        Assert.False(vm.IsDebugging);
        Assert.Equal(0, fake.SubscriberCount); // no leaked handler (the #39 fix)
    }

    [Fact]
    public async Task FailedEnable_ThenSuccessfulStart_DoesNotDoubleSubscribe()
    {
        var fake = new FakeDebugSession { FailEnable = true };
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // fails, must not leak
        fake.FailEnable = false;
        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // succeeds

        Assert.True(vm.IsDebugging);
        Assert.Equal(1, fake.SubscriberCount); // exactly one, not two
    }

    [Fact]
    public async Task Dispose_WhileDebugging_DisablesStream()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true };
        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // start

        vm.Dispose();

        Assert.Contains(false, fake.DebugCalls);
    }

    [Fact]
    public void IsConnected_SyncsFromConnectionState()
    {
        var conn = new FakeConnectionState { IsConnected = true };
        var vm = new DiagnosticsViewModel(new FakeDebugSession(), conn);

        Assert.True(vm.IsConnected); // initial value taken from the connection

        conn.IsConnected = false;    // change propagates via PropertyChanged
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public void Dispose_UnsubscribesFromConnectionState()
    {
        var conn = new FakeConnectionState { IsConnected = true };
        var vm = new DiagnosticsViewModel(new FakeDebugSession(), conn);

        vm.Dispose();
        conn.IsConnected = false; // must not throw or update after dispose

        Assert.True(vm.IsConnected); // stayed at the pre-dispose value
    }

    // ── The page's tablet/rate line (#diagnostics-blank-name). Both properties are plain strings that
    //    start "", and an empty string is not a failed binding, so a XAML FallbackValue never covered
    //    them. These pin the projections that do.

    [Fact]
    public void TabletLabel_BeforeAnyReport_SaysNoTablet()
    {
        var vm = new DiagnosticsViewModel(new FakeDebugSession());

        Assert.Equal("", vm.DebugTabletName);      // the raw value really is empty ...
        Assert.Equal("No tablet", vm.DebugTabletLabel); // ... and the row still says something
    }

    [Fact]
    public void TabletLabel_FollowsTheNameOnceAReportNamesOne()
    {
        var vm = new DiagnosticsViewModel(new FakeDebugSession()) { DebugTabletName = "Wacom PTH-660" };

        Assert.Equal("Wacom PTH-660", vm.DebugTabletLabel);
    }

    [Fact]
    public async Task TabletLabel_ReturnsToNoTablet_WhenDebuggingRestarts()
    {
        var fake = new FakeDebugSession();
        var vm = new DiagnosticsViewModel(fake) { IsConnected = true, DebugTabletName = "Wacom PTH-660" };

        await vm.ToggleDebuggingCommand.ExecuteAsync(null); // StartDebugging clears the name

        Assert.Equal("No tablet", vm.DebugTabletLabel);
    }

    [Fact]
    public void ReportRate_IsHidden_UntilThereIsARate()
    {
        var vm = new DiagnosticsViewModel(new FakeDebugSession()) { IsDebugging = true };

        Assert.False(vm.ShowDebugReportRate);   // "" — would have drawn a separator against nothing

        vm.DebugReportRate = "133 Hz";
        Assert.True(vm.ShowDebugReportRate);
    }

    [Fact]
    public void ReportRate_IsHidden_WhenNotDebugging()
    {
        // The rate is not cleared on stop, so it must be gated on IsDebugging as well as on being set.
        var vm = new DiagnosticsViewModel(new FakeDebugSession()) { DebugReportRate = "133 Hz" };

        Assert.False(vm.ShowDebugReportRate);
    }
}
