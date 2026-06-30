using OpenTabletDriver.Desktop.Profiles;
using OtdArtist.Domain;
using Xunit;

namespace OtdArtist.Tests;

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

    [Fact]
    public void ApplyToProfile_SetsDisplayAreaToWholeMonitor_Centered()
    {
        var p = ProfileWithAbsolute();
        var ok = DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), Display(2, 1920, 0, 2560, 1440));

        Assert.True(ok);
        var disp = p.AbsoluteModeSettings.Display;
        Assert.Equal(2560f, disp.Width, Precision);
        Assert.Equal(1440f, disp.Height, Precision);
        Assert.Equal(1920f + 2560f / 2f, disp.X, Precision); // monitor centre X
        Assert.Equal(0f + 1440f / 2f, disp.Y, Precision);    // monitor centre Y
    }

    [Fact]
    public void ApplyToProfile_FitsTabletAreaToDisplayAspect_AspectLocked()
    {
        var p = ProfileWithAbsolute();
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), Display(1, 0, 0, 1920, 1080));

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

        var ok = DisplayMappingApplier.ApplyToProfile(p, digitizer: null, Display(1, 0, 0, 1000, 500));

        Assert.True(ok);
        // 2:1 display vs 1:1 fallback tablet → full width 100, half height.
        Assert.Equal(100f, p.AbsoluteModeSettings.Tablet.Width, Precision);
        Assert.Equal(50f, p.AbsoluteModeSettings.Tablet.Height, Precision);
    }

    [Fact]
    public void ApplyToProfile_ReturnsFalse_WhenNoAbsoluteSettings()
    {
        var p = new Profile { Tablet = "X", AbsoluteModeSettings = null! };
        Assert.False(DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), Display(1, 0, 0, 1920, 1080)));
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
        DisplayMappingApplier.ApplyToProfile(p, (152f, 95f), displays[1]);

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
}
