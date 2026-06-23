using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OtdWindowsHelper.Services;
using OtdWindowsHelper.ViewModels;
using Xunit;

namespace OtdWindowsHelper.Tests;

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
}
