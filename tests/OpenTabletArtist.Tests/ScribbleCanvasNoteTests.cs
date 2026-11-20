using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// What the Scribble canvas says when it cannot paint. It names the cause that is true: no tablet, Mouse-like
/// movement, or neither. Before, one long note about "an Absolute output mode" covered all three, so an
/// unplugged tablet whose movement was already Normal (Absolute) was told to change a setting that was fine.
/// </summary>
public class ScribbleCanvasNoteTests
{
    private const string Absolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
    private const string Relative = "OpenTabletDriver.Desktop.Output.RelativeMode";

    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067 // not exercised by these tests
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    private static ProfileItem Item(string mode, bool detected) =>
        new(new Profile { Tablet = "Wacom PTK-470", OutputMode = new PluginSettingStore(mode) { Path = mode } },
            IsDetected: detected, LastSeen: null);

    private static TestViewModel Scribble(ProfileItem item)
    {
        var data = new FakeDeviceData { Profiles = [item] };
        var vm = new TestViewModel(new NoopDebugSession(), data);
        data.RaiseDataLoaded();
        return vm;
    }

    /// <summary>The reported case: the tablet is unplugged and its movement is Normal (Absolute). The mode is
    /// not what stops the canvas, so the note must not talk about it.</summary>
    [Fact]
    public void NoTablet_SaysToConnectIt_EvenWhenTheRememberedMovementIsAbsolute()
    {
        using var vm = Scribble(Item(Absolute, detected: false));

        Assert.True(vm.DriverCanvasDisabled);
        Assert.Equal(TestViewModel.NoTabletNote, vm.DriverDisabledNote);
    }

    [Fact]
    public void NoTablet_SaysToConnectIt_WhenTheRememberedMovementIsRelative()
    {
        using var vm = Scribble(Item(Relative, detected: false));

        Assert.Equal(TestViewModel.NoTabletNote, vm.DriverDisabledNote);
    }

    /// <summary>The note uses the words on the pen page's movement dropdown, not "output mode".</summary>
    [Fact]
    public void MouseLikeMovement_NamesTheSettingAndWhereItIs()
    {
        using var vm = Scribble(Item(Relative, detected: true));

        Assert.True(vm.DriverCanvasDisabled);
        Assert.Equal(TestViewModel.MouseLikeNote, vm.DriverDisabledNote);
        Assert.Contains("Mouse-like", vm.DriverDisabledNote);
        Assert.Contains("Normal (Absolute)", vm.DriverDisabledNote);
        Assert.Contains("basics", vm.DriverDisabledNote);
        Assert.DoesNotContain("output mode", vm.DriverDisabledNote, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Connected and already Normal, but the tablet's size is not known: a setting would not help, so
    /// the note must not send the artist to one.</summary>
    [Fact]
    public void ConnectedAndAbsolute_ButUnmappable_DoesNotBlameTheMovementSetting()
    {
        using var vm = Scribble(Item(Absolute, detected: true));

        Assert.True(vm.DriverCanvasDisabled);
        Assert.Equal(TestViewModel.CannotMapNote, vm.DriverDisabledNote);
        Assert.DoesNotContain("Mouse-like", vm.DriverDisabledNote);
    }

    /// <summary>The note follows the tablet: plug it in and the "connect" note gives way to the next true one.</summary>
    [Fact]
    public void TheNote_FollowsTheTablet_WhenItIsPluggedIn()
    {
        var data = new FakeDeviceData { Profiles = [Item(Relative, detected: false)] };
        using var vm = new TestViewModel(new NoopDebugSession(), data);
        data.RaiseDataLoaded();
        Assert.Equal(TestViewModel.NoTabletNote, vm.DriverDisabledNote);
        var changed = false;
        vm.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(TestViewModel.DriverDisabledNote);

        data.Profiles = [Item(Relative, detected: true)];
        data.RaiseDataLoaded();

        Assert.True(changed, "the note did not announce that it changed");
        Assert.Equal(TestViewModel.MouseLikeNote, vm.DriverDisabledNote);
    }
}
