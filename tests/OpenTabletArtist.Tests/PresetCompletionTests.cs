using System.Linq;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// A preset only knows the tablets that existed when it was saved. Loading it onto a driver with another
/// tablet plugged in made the daemon generate that tablet's profile, which read back as a rejected apply and
/// paused editing for a load nobody else had touched.
/// </summary>
public class PresetCompletionTests
{
    private static Profile Profile(string tablet) => new() { Tablet = tablet, OutputMode = TestOtd.Output() };

    private static Settings Of(params Profile[] profiles) =>
        new() { Profiles = new ProfileCollection(profiles) };

    [Fact]
    public void ATabletThePresetLacks_KeepsTheProfileItHasNow()
    {
        var preset = Of(Profile("Wacom PTK-870"));
        var mine = Profile("Wacom PTK-670");
        var current = Of(Profile("Wacom PTK-870"), mine);

        var added = PresetCompletion.AddMissingProfiles(preset, current);

        Assert.Equal(1, added);
        Assert.Equal(["Wacom PTK-870", "Wacom PTK-670"], preset.Profiles.Select(p => p.Tablet));
        Assert.Same(mine, preset.Profiles.GetProfile("Wacom PTK-670"));
    }

    /// <summary>The preset wins for every tablet it does hold; filling gaps is all this does.</summary>
    [Fact]
    public void AProfileThePresetHolds_IsNotReplacedByTheCurrentOne()
    {
        var held = Profile("Wacom PTK-870");
        var preset = Of(held);
        var current = Of(Profile("Wacom PTK-870"));

        var added = PresetCompletion.AddMissingProfiles(preset, current);

        Assert.Equal(0, added);
        Assert.Same(held, Assert.Single(preset.Profiles));
    }

    /// <summary>
    /// The preset as saved is never filled in itself: a second pass over a filled-in preset took the first
    /// pass's copies for the preset's own and put a tablet back as it was (#1060).
    /// </summary>
    [Fact]
    public void Complete_FillsACopy_AndLeavesThePresetAsSaved()
    {
        var preset = Of(Profile("Wacom PTK-870"));
        var mine = Profile("Wacom PTK-670");

        var completed = PresetCompletion.Complete(preset, Of(mine));

        Assert.Equal(["Wacom PTK-870", "Wacom PTK-670"], completed.Profiles.Select(p => p.Tablet));
        Assert.Equal(["Wacom PTK-870"], preset.Profiles.Select(p => p.Tablet));
        Assert.NotSame(preset, completed);
    }

    [Fact]
    public void WithNothingCurrent_ThePresetIsLeftAsItIs()
    {
        var preset = Of(Profile("Wacom PTK-870"));

        Assert.Equal(0, PresetCompletion.AddMissingProfiles(preset, null));
        Assert.Single(preset.Profiles);
    }

    [Fact]
    public void APresetWithNoProfileList_GetsOne()
    {
        var preset = new Settings { Profiles = null! };

        var added = PresetCompletion.AddMissingProfiles(preset, Of(Profile("Wacom PTK-670")));

        Assert.Equal(1, added);
        Assert.Single(preset.Profiles);
    }
}
