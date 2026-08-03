using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileSanitizerTests
{
    private static Settings With(params Profile[] profiles) =>
        new() { Profiles = new ProfileCollection(profiles) };

    [Fact]
    public void FillsNullAbsoluteModeSettings()
    {
        var p = new Profile { Tablet = "T", AbsoluteModeSettings = null! };
        Assert.Equal(1, ProfileSanitizer.EnsureValidAbsoluteAreas(With(p)));
        Assert.NotNull(p.AbsoluteModeSettings);
        Assert.NotNull(p.AbsoluteModeSettings.Tablet);
        Assert.NotNull(p.AbsoluteModeSettings.Display);
    }

    [Fact]
    public void FillsNullTabletAndDisplay_SoTheOtdUxPredicateWontThrow()
    {
        // A `new AbsoluteModeSettings()` leaves Tablet/Display null — exactly the forced-tablet malformation.
        var p = new Profile { Tablet = "T", AbsoluteModeSettings = new AbsoluteModeSettings() };
        Assert.Equal(1, ProfileSanitizer.EnsureValidAbsoluteAreas(With(p)));
        Assert.NotNull(p.AbsoluteModeSettings.Tablet);
        Assert.NotNull(p.AbsoluteModeSettings.Display);

        // The precise expression OpenTabletDriver's UX runs on Save (MainForm.cs) must no longer NRE.
        var settings = With(p);
        var ex = Record.Exception(() =>
            settings.Profiles.Any(x => x.AbsoluteModeSettings.Tablet.Width + x.AbsoluteModeSettings.Tablet.Height == 0));
        Assert.Null(ex);
    }

    [Fact]
    public void LeavesValidProfilesUnchanged()
    {
        var p = new Profile
        {
            Tablet = "T",
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Tablet = new AreaSettings { Width = 100, Height = 50 },
                Display = new AreaSettings { Width = 1920, Height = 1080 },
            },
        };
        Assert.Equal(0, ProfileSanitizer.EnsureValidAbsoluteAreas(With(p)));
        Assert.Equal(100, p.AbsoluteModeSettings.Tablet.Width); // untouched
        Assert.Equal(1920, p.AbsoluteModeSettings.Display.Width);
    }

    [Fact]
    public void NullSettings_IsANoOp()
    {
        Assert.Equal(0, ProfileSanitizer.EnsureValidAbsoluteAreas(null));
    }
}
