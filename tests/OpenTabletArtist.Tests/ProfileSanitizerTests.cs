using System.Linq;
using OpenTabletArtist.Domain;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ProfileSanitizerTests
{
    private static Settings With(params Profile[] profiles) =>
        new() { Profiles = new ProfileCollection(profiles) };

    [Fact]
    public void FillsNullAbsoluteModeSettings()
    {
        var p = new Profile { OutputMode = TestOtd.Output(), Tablet = "T", AbsoluteModeSettings = null! };
        Assert.Equal(1, ProfileSanitizer.EnsureValidAbsoluteAreas(With(p)));
        Assert.NotNull(p.AbsoluteModeSettings);
        Assert.NotNull(p.AbsoluteModeSettings.Tablet);
        Assert.NotNull(p.AbsoluteModeSettings.Display);
    }

    [Fact]
    public void FillsNullTabletAndDisplay_SoTheOtdUxPredicateWontThrow()
    {
        // Both areas null — exactly the forced-tablet malformation. They are required members as of OTD 0.6.8, so
        // the compiler no longer lets a new instance leave them out; a settings.json that omits them still can.
        var p = new Profile { OutputMode = TestOtd.Output(), Tablet = "T", AbsoluteModeSettings = new AbsoluteModeSettings { Display = null!, Tablet = null! } };
        Assert.Equal(1, ProfileSanitizer.EnsureValidAbsoluteAreas(With(p)));
        Assert.NotNull(p.AbsoluteModeSettings.Tablet);
        Assert.NotNull(p.AbsoluteModeSettings.Display);

        // The precise expression OpenTabletDriver's UX runs on Save (MainForm.cs) must no longer NRE.
        var settings = With(p);
        var ex = Record.Exception(() =>
            settings.Profiles.Any(x => x.AbsoluteModeSettings!.Tablet.Width + x.AbsoluteModeSettings!.Tablet.Height == 0));
        Assert.Null(ex);
    }

    [Fact]
    public void LeavesValidProfilesUnchanged()
    {
        var p = new Profile
        {
            OutputMode = TestOtd.Output(),
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
