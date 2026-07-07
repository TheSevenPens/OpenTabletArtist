using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class DisplayMappingApplierTests
{
    private const int Precision = 3;

    private static Profile ProfileWithAbsolute(string tablet = "Test Tablet") =>
        new()
        {
            Tablet = tablet,
            AbsoluteModeSettings = new AbsoluteModeSettings
            {
                Display = new AreaSettings(),
                Tablet = new AreaSettings(),
            },
        };

    private static DisplayInfo Display(int number, int x, int y, int w, int h, bool primary = false) =>
        new(number, $"Monitor {number}", w, h, x, y, primary);

    // A standard extended layout: primary at the desktop origin, a second monitor to its right.
    private static DisplayInfo[] TwoMonitors() => new[]
    {
        Display(1, 0, 0, 1920, 1080, primary: true),
        Display(2, 1920, 0, 2560, 1440),
    };

    [Fact]
    public void ApplyToProfile_SetsDisplayAreaToWholeMonitor_Centered()
    {
        var displays = TwoMonitors();
        var p = ProfileWithAbsolute();
        var ok = DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), displays[1], displays);

        Assert.True(ok);
        var disp = p.AbsoluteModeSettings.Display;
        Assert.Equal(2560f, disp.Width, Precision);
        Assert.Equal(1440f, disp.Height, Precision);
        // Primary at the origin ⇒ virtual-screen space equals raw coords: monitor centre.
        Assert.Equal(1920f + 2560f / 2f, disp.X, Precision);
        Assert.Equal(0f + 1440f / 2f, disp.Y, Precision);
    }

    // Regression for the duplicated-display bug: the primary (2) is at x=0 with another monitor (1) to
    // its LEFT at x=-2880 (a layout duplication can produce). The stored area must be in OTD's 0-based
    // virtual-desktop space (origin at the desktop's min corner) so it lands on monitor 2 — not the raw
    // monitor centre, which sat inside monitor 1 (the left-shift the user reported).
    [Fact]
    public void ApplyToProfile_PrimaryNotTopLeft_PlacesAreaInZeroBasedDesktopSpace()
    {
        var displays = new[]
        {
            Display(1, -2880, 0, 2880, 1800),            // left of the primary
            Display(2, 0, 0, 1920, 1080, primary: true), // primary at x=0
        };
        var p = ProfileWithAbsolute();
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), displays[1], displays);

        var disp = p.AbsoluteModeSettings.Display;
        // minX=-2880 ⇒ X = display.X - minX + width/2 = 0 - (-2880) + 1920/2 = 3840 (centre of monitor 2,
        // whose 0-based span is 2880..4800). Monitor 1 occupies 0..2880.
        Assert.Equal(3840f, disp.X, Precision);
        Assert.Equal(540f, disp.Y, Precision);
        Assert.NotEqual(960f, disp.X, Precision);        // the old raw-centre value — sat inside monitor 1
        // And it still round-trips through CurrentlyMapped.
        Assert.Equal(2, DisplayMappingApplier.CurrentlyMapped(p, displays)!.Number);
    }

    [Fact]
    public void ApplyToProfile_FitsTabletAreaToDisplayAspect_AspectLocked()
    {
        var p = ProfileWithAbsolute();
        var d = Display(1, 0, 0, 1920, 1080, primary: true);
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), d, new[] { d });

        var tab = p.AbsoluteModeSettings.Tablet;
        // 16:9 display wider than the 152x95 tablet → full width, reduced height, centered.
        Assert.Equal(152f, tab.Width, Precision);
        Assert.Equal(152f * 1080f / 1920f, tab.Height, Precision);
        Assert.Equal(76f, tab.X, Precision);
        Assert.Equal(47.5f, tab.Y, Precision);
        Assert.True(p.AbsoluteModeSettings.LockAspectRatio);
    }

    [Fact]
    public void ApplyToProfile_FallsBackToProfileTabletSize_WhenNoDigitizer()
    {
        var p = ProfileWithAbsolute();
        p.AbsoluteModeSettings.Tablet.Width = 100f;
        p.AbsoluteModeSettings.Tablet.Height = 100f;

        var d = Display(1, 0, 0, 1000, 500, primary: true);
        var ok = DisplayMappingApplier.ApplyToProfile(p, digitizer: null, d, new[] { d });

        Assert.True(ok);
        // 2:1 display vs 1:1 fallback tablet → full width 100, half height.
        Assert.Equal(100f, p.AbsoluteModeSettings.Tablet.Width, Precision);
        Assert.Equal(50f, p.AbsoluteModeSettings.Tablet.Height, Precision);
    }

    [Fact]
    public void ApplyToProfile_ReturnsFalse_WhenNoAbsoluteSettings()
    {
        var p = new Profile { Tablet = "X", AbsoluteModeSettings = null! };
        var d = Display(1, 0, 0, 1920, 1080, primary: true);
        Assert.False(DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), d, new[] { d }));
    }

    [Fact]
    public void CurrentlyMapped_RoundTripsApply()
    {
        var displays = new[]
        {
            Display(1, 0, 0, 1920, 1080, primary: true),
            Display(2, 1920, 0, 2560, 1440),
        };
        var p = ProfileWithAbsolute();
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), displays[1], displays);

        var mapped = DisplayMappingApplier.CurrentlyMapped(p, displays);
        Assert.NotNull(mapped);
        Assert.Equal(2, mapped!.Number);
    }

    [Fact]
    public void CurrentlyMapped_NullWhenNotAFullMonitorMatch()
    {
        var displays = new[] { Display(1, 0, 0, 1920, 1080) };
        var p = ProfileWithAbsolute();
        // A sub-area, not a whole monitor.
        p.AbsoluteModeSettings.Display.Width = 800;
        p.AbsoluteModeSettings.Display.Height = 600;
        p.AbsoluteModeSettings.Display.X = 400;
        p.AbsoluteModeSettings.Display.Y = 300;

        Assert.Null(DisplayMappingApplier.CurrentlyMapped(p, displays));
    }

    [Fact]
    public void PreserveAreaMapping_KeepsCurrentMonitor_ButNotOtherSettings()
    {
        // Snapshot maps the tablet to monitor 2 and turns on aspect lock; current has it on monitor 1.
        var displays = TwoMonitors();
        var snapshot = new Settings { Profiles = new ProfileCollection { ProfileWithAbsolute("T") } };
        DisplayMappingApplier.ApplyToProfile(snapshot.Profiles[0], (100f, 100f), displays[1], displays);
        snapshot.Profiles[0].AbsoluteModeSettings.LockAspectRatio = true;

        var current = new Settings { Profiles = new ProfileCollection { ProfileWithAbsolute("T") } };
        DisplayMappingApplier.ApplyToProfile(current.Profiles[0], (100f, 100f), displays[0], displays);

        DisplayMappingApplier.PreserveAreaMapping(snapshot, current);

        // Monitor now follows current (1), not the snapshot's baked-in monitor (2)…
        Assert.Equal(1, DisplayMappingApplier.CurrentlyMapped(snapshot.Profiles[0], displays)!.Number);
        // …while a non-mapping setting from the snapshot is left untouched.
        Assert.True(snapshot.Profiles[0].AbsoluteModeSettings.LockAspectRatio);
    }

    [Fact]
    public void ClassifyMapping_Clean_ForWholeMonitorMapping()
    {
        var displays = TwoMonitors();
        var p = ProfileWithAbsolute();
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), displays[1], displays);
        Assert.Equal(DisplayMappingValidity.Clean, DisplayMappingApplier.ClassifyMapping(p, displays));
    }

    [Fact]
    public void ClassifyMapping_Custom_ForOnScreenSubRegion()
    {
        var displays = new[] { Display(1, 0, 0, 1920, 1080, primary: true) };
        var p = ProfileWithAbsolute();
        var d = p.AbsoluteModeSettings.Display;
        d.Width = 800; d.Height = 600; d.X = 400; d.Y = 300; // fully inside the monitor, but not the whole one
        Assert.Equal(DisplayMappingValidity.Custom, DisplayMappingApplier.ClassifyMapping(p, displays));
    }

    [Fact]
    public void ClassifyMapping_OffScreen_WhenAreaExtendsBeyondDisplays()
    {
        var displays = new[] { Display(1, 0, 0, 1920, 1080, primary: true) };
        var p = ProfileWithAbsolute();
        var d = p.AbsoluteModeSettings.Display;
        // A large area centred so it spills well past the monitor's right and bottom edges.
        d.Width = 2560; d.Height = 1440; d.X = 1500; d.Y = 800;
        Assert.Equal(DisplayMappingValidity.OffScreen, DisplayMappingApplier.ClassifyMapping(p, displays));
    }

    [Fact]
    public void ClassifyMapping_None_ForDegenerateAreaOrNoDisplays()
    {
        var displays = new[] { Display(1, 0, 0, 1920, 1080, primary: true) };
        Assert.Equal(DisplayMappingValidity.None,
            DisplayMappingApplier.ClassifyMapping(ProfileWithAbsolute(), displays)); // default area is zero-size

        var mapped = ProfileWithAbsolute();
        DisplayMappingApplier.ApplyToProfile(mapped, (152f, 95f), displays[0], displays);
        Assert.Equal(DisplayMappingValidity.None,
            DisplayMappingApplier.ClassifyMapping(mapped, System.Array.Empty<DisplayInfo>()));
    }

    [Fact]
    public void PreserveAreaMapping_IgnoresUnmatchedTablets()
    {
        var displays = TwoMonitors();
        var snapshot = new Settings { Profiles = new ProfileCollection { ProfileWithAbsolute("A") } };
        DisplayMappingApplier.ApplyToProfile(snapshot.Profiles[0], (100f, 100f), displays[1], displays);
        var current = new Settings { Profiles = new ProfileCollection { ProfileWithAbsolute("B") } }; // different tablet

        DisplayMappingApplier.PreserveAreaMapping(snapshot, current); // no match → snapshot untouched

        Assert.Equal(2, DisplayMappingApplier.CurrentlyMapped(snapshot.Profiles[0], displays)!.Number);
    }
}
