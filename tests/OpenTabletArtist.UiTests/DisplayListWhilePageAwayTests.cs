using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The Display Mapping tab's monitor list when the displays change while the tablet page is not on screen.
/// </summary>
/// <remarks>
/// <para>
/// The main window keeps one <see cref="TabletDetailViewModel"/> per tablet for the whole session, and
/// that view model reads the monitors once, in its constructor. After that only the view's
/// <c>Screens.Changed</c> subscription refreshes them, and that subscription exists only while the view is
/// attached. So switching Windows from Extend to Duplicate while another page is showing left the page
/// listing a monitor that was gone, until OpenTabletArtist was restarted.
/// </para>
/// <para>
/// <c>Screens.Changed</c> cannot be raised from a headless test, so this drives the part that is ours: what
/// the page does when it comes back. The live path, with the page showing, was checked by hand against
/// real Extend/Duplicate switches and needs no test of its own here.
/// </para>
/// </remarks>
public class DisplayListWhilePageAwayTests
{
    private const float WidthMm = 269, HeightMm = 168;

    private sealed class SwitchableDisplays : IDisplayEnumerator
    {
        public IReadOnlyList<DisplayInfo> Current { get; set; } = [];
        public IReadOnlyList<DisplayInfo> Enumerate() => Current;
    }

    private static DisplayInfo Display(int number) => new(
        Number: number, Name: $"Display {number}", Width: 1920, Height: 1080,
        X: (number - 1) * 1920, Y: 0, IsPrimary: number == 1, RefreshHz: 60, Port: "HDMI", Gpu: "Test GPU");

    private static Settings Mapped(string tablet)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { OutputMode = Tests.TestOtd.Output(), Tablet = tablet } } };
        settings.Profiles.First().AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Display = new AreaSettings(),
            Tablet = new AreaSettings { Width = WidthMm, Height = HeightMm, X = WidthMm / 2, Y = HeightMm / 2 },
        };
        return settings;
    }

    private static void Settle() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void AMonitorThatWentAwayWhileThePageWasAwayIsNotStillListed()
    {
        var windows = new SwitchableDisplays { Current = [Display(1), Display(2)] };
        DisplayEnumerator.Use(windows);
        try
        {
            var settings = Mapped("T");
            var vm = new TabletDetailViewModel(
                settings.Profiles.First(), settings,
                applyAction: _ => Task.FromResult(SettingsApplyOutcome.Live),
                refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles.First())),
                tabletDigitizer: (WidthMm, HeightMm));

            var view = new TabletDetailView { DataContext = vm };
            var window = new Window { Content = view, Width = 1100, Height = 900 };
            window.Show();
            Settle();

            // The control: the page starts out listing both monitors, so the assertion below can only
            // pass by the list having changed.
            Assert.Equal(2, vm.Displays.Count);

            // Navigate away: the page leaves the window, as it does when another nav item is chosen.
            window.Content = null;
            Settle();

            // Windows goes from Extend to Duplicate while it is away: one desktop monitor remains.
            windows.Current = [Display(1)];

            // Navigate back.
            window.Content = view;
            Settle();

            Assert.Equal(new[] { 1 }, vm.Displays.Select(d => d.Number).ToArray());
        }
        finally
        {
            DisplayEnumerator.Use(null);
        }
    }
}
