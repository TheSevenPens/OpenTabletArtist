using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.ViewModels;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The Display Mapping tab's rotation dropdown, which replaced four radio buttons (#932).
/// </summary>
///
/// <remarks>
/// <para>
/// The radios were bound to an <c>AsyncRelayCommand</c>, and so were disabled for as long as an apply
/// ran. The dropdown is a two-way property instead, and that gate had to be rebuilt by hand: without it
/// a second choice made while the first apply is still running is compared against the pre-apply
/// rotation and silently dropped, because <c>ApplySettingsChange</c> mutates the profile before
/// awaiting while <c>TabletRotation</c> only moves once the apply lands.
/// </para>
/// <para>
/// So these cover the whole round trip rather than the happy path: one selection is one apply, a
/// refresh is not an apply, an apply in flight closes the control, and a refused apply leaves what it
/// actually leaves rather than what would be convenient.
/// </para>
/// </remarks>
// Constructs TabletDetailViewModel, whose constructor calls DisplayEnumerator.Enumerate() (#729).
[Collection(DisplayEnumeratorCollection.Name)]
public class RotationSelectionTests
{
    private static Settings Mapped(string tablet)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
        settings.Profiles.First().AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Tablet = new AreaSettings { Width = 269, Height = 168, X = 134.5f, Y = 84 },
        };
        return settings;
    }

    private static TabletDetailViewModel Vm(Settings settings, Func<Settings, Task<SettingsApplyOutcome>> apply) =>
        new(settings.Profiles.First(), settings,
            applyAction: apply,
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles.First())),
            tabletDigitizer: (269f, 168f));

    private static RotationOption Option(TabletDetailViewModel vm, int degrees) =>
        vm.RotationOptions.Single(o => o.Degrees == degrees);

    private static float StoredRotation(Settings settings) =>
        settings.Profiles.First().AbsoluteModeSettings!.Tablet!.Rotation;

    /// <summary>Picking an angle applies it, once.</summary>
    [Fact]
    public async Task OneSelection_IsOneApply()
    {
        var settings = Mapped("T");
        var applies = 0;
        var vm = Vm(settings, _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });

        vm.SelectedRotation = Option(vm, 90);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, applies);
        Assert.Equal(90, StoredRotation(settings));
        Assert.Equal(90, vm.SelectedRotation!.Degrees);
    }

    /// <summary>
    /// A rotation arriving from a refresh is not a selection, and must not apply anything.
    /// </summary>
    /// <remarks>
    /// This is the loop the two-way binding makes possible: the poll behind <c>RefreshTabletArea</c>
    /// writes <c>TabletRotation</c>, which notifies <c>SelectedRotation</c>, which the dropdown answers
    /// by writing back through the setter. What stops it there is the setter finding the value already
    /// stored — so this pins that, rather than the suppression flag it could have been.
    /// </remarks>
    [Fact]
    public async Task ARotationThatArrivesFromTheDaemon_DoesNotApplyAnything()
    {
        var settings = Mapped("T");
        settings.Profiles.First().AbsoluteModeSettings!.Tablet!.Rotation = 180;
        var applies = 0;
        var vm = Vm(settings, _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });

        await vm.RefreshCommand.ExecuteAsync(null);
        // What the dropdown does on that notification: write the value it now shows straight back.
        vm.SelectedRotation = vm.SelectedRotation;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(180, vm.SelectedRotation!.Degrees);
        Assert.Equal(0, applies);
    }

    /// <summary>
    /// While an apply is running the dropdown is unavailable, and a value written anyway is ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the defect Codex found, and the reason the flag exists at all. Without it the second
    /// write below is measured against the pre-apply rotation — 0 against 0 — and dropped as a no-op,
    /// after which the refresh snaps the dropdown to 90° and the artist's last choice was never sent.
    /// Closing the control for the duration is what the radio buttons did, so this restores rather than
    /// invents.
    /// </para>
    /// <para>
    /// The second selection here is 180°, deliberately. Selecting 0° again is refused by the
    /// <em>same-value</em> guard too, so that version of this test passed with the busy guard deleted —
    /// two guards covering for each other. 180° is a value only the busy guard can stop, and without it
    /// a second apply runs concurrently with the first, both writing the same profile.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WhileAnApplyIsRunning_TheControlIsClosedAndALaterWriteIsIgnored()
    {
        var settings = Mapped("T");
        var gate = new TaskCompletionSource<SettingsApplyOutcome>();
        var applies = 0;
        var vm = Vm(settings, _ => { applies++; return gate.Task; });

        vm.SelectedRotation = Option(vm, 90);
        Assert.True(vm.RotationApplyRunning, "the dropdown should be closed while the apply runs");

        vm.SelectedRotation = Option(vm, 180);   // what a disabled ComboBox cannot send
        Assert.Equal(1, applies);

        gate.SetResult(SettingsApplyOutcome.Live);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(vm.RotationApplyRunning);
        Assert.Equal(90, vm.SelectedRotation!.Degrees);
        Assert.Equal(1, applies);
    }

    /// <summary>The control reopens even when the apply throws.</summary>
    /// <remarks>
    /// A flag set outside a <c>try</c> is a flag that can be left on. Rotation would then be stuck for
    /// the life of the page, with nothing on screen saying why.
    /// </remarks>
    [Fact]
    public async Task WhenTheApplyThrows_TheControlReopens()
    {
        var settings = Mapped("T");
        var vm = Vm(settings, _ => Task.FromException<SettingsApplyOutcome>(new InvalidOperationException("boom")));

        vm.SelectedRotation = Option(vm, 90);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(vm.RotationApplyRunning);
    }

    /// <summary>
    /// A refused apply leaves the rotation the artist asked for on screen, not the one the daemon has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This describes the view model on its own, not a promise the app makes.
    /// <c>ApplySettingsChange</c> mutates the local profile before it awaits, and the refresh that
    /// follows a refusal rereads that same local profile — so with a delegate that refuses without
    /// publishing authoritative settings, as here, there is no rollback to observe. A live session can
    /// replace that profile when it reconciles, and this fixture has no session, no paused panel and no
    /// footer.
    /// </para>
    /// <para>
    /// Written down because the obvious assumption — "the next refresh will correct it" — is false of
    /// this path, and a change that quietly starts relying on it should fail here. What the app owes the
    /// artist is narrower and lives elsewhere: a refusal must not look like a success, editing follows
    /// the session, and reloading adopts the OTD daemon's values. A deliberate rollback later is free to
    /// change this assertion.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(SettingsApplyStatus.Disconnected)]
    [InlineData(SettingsApplyStatus.ChangedElsewhere)]
    public async Task WhenTheApplyIsRefused_TheDropdownKeepsShowingWhatWasAskedFor(SettingsApplyStatus status)
    {
        var settings = Mapped("T");
        var vm = Vm(settings, _ => Task.FromResult(new SettingsApplyOutcome(status)));

        vm.SelectedRotation = Option(vm, 90);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(90, vm.SelectedRotation!.Degrees);
        Assert.False(vm.RotationApplyRunning);
    }

    /// <summary>A negative angle is the same angle, and selects it.</summary>
    /// <remarks>
    /// The refresh normalises into 0–359 before the dropdown sees it, so -90 is 270 and reads as a
    /// choice the artist made rather than as an angle the app does not offer.
    /// </remarks>
    [Fact]
    public async Task ANegativeAngle_SelectsItsEquivalent()
    {
        var settings = Mapped("T");
        settings.Profiles.First().AbsoluteModeSettings!.Tablet!.Rotation = -90;
        var vm = Vm(settings, _ => Task.FromResult(SettingsApplyOutcome.Live));

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(270, vm.TabletRotation);
        Assert.Equal(270, vm.SelectedRotation!.Degrees);
    }

    /// <summary>An angle that is none of the four leaves the box empty rather than rounding to one.</summary>
    /// <remarks>
    /// OTD's own UX can store any angle. Showing the nearest would say the artist had chosen it.
    /// </remarks>
    [Fact]
    public async Task AnAngleThatIsNotOneOfTheFour_SelectsNothing()
    {
        var settings = Mapped("T");
        settings.Profiles.First().AbsoluteModeSettings!.Tablet!.Rotation = 45;
        var applies = 0;
        var vm = Vm(settings, _ => { applies++; return Task.FromResult(SettingsApplyOutcome.Live); });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(45, vm.TabletRotation);
        Assert.Null(vm.SelectedRotation);
        Assert.Equal(0, applies);
    }
}
