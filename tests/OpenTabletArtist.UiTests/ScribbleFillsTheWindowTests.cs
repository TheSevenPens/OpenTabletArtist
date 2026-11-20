using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The Scribble canvas takes the height the window leaves for it, instead of sitting at its minimum however tall
/// the window is.
/// </summary>
/// <remarks>
/// The shell used to host every page in a vertical StackPanel, which offers its children unlimited height, so a
/// "fill the rest" child had no rest to fill. The page area is now <see cref="PageHost"/>, a Grid, and a page
/// opts in to filling (<see cref="PageLayout"/>). These tests build the real page area, the real Scribble view
/// and the shell's outer scroll viewer in a window with a nav row above and a footer below, and measure the
/// canvas as the window is resized.
/// </remarks>
public class ScribbleFillsTheWindowTests
{
    private const double NavHeight = 80, FooterHeight = 60, PagePaddingBottom = 18;

    private sealed class NoopDebugSession : IDaemonDebugSession
    {
#pragma warning disable CS0067
        public event Action<JObject>? DeviceReport;
#pragma warning restore CS0067
        public Task SetTabletDebugAsync(bool enabled) => Task.CompletedTask;
    }

    // What the page host binds, minus the parts of the shell that need a daemon.
    // In the shell CurrentPage is the page's view model and a template turns it into the view; the rule about
    // which pages fill reads the view model. Here the view is shown directly, so the view model is passed
    // separately for the rule.
    private sealed class HostState(object view, object viewModel)
    {
        public object CurrentPage { get; } = view;
        public bool PageInputEnabled => true;
        public VerticalAlignment ContentVerticalAlignment { get; } = PageLayout.VerticalAlignmentFor(viewModel);
        public SessionState SettingsSession { get; } = new();
    }

    private sealed class SessionState
    {
        public bool SettingsPaused => false;
        public bool SettingsBusy => false;
        public string SaveStatusText => "";
        public string SettingsPauseCauseText => "";
    }

    private static TestView Scribble()
    {
        var profile = new Profile { OutputMode = TestOtd.Output(), Tablet = "Wacom PTK-670" };
        var data = new FakeDeviceData { Profiles = [new ProfileItem(profile, IsDetected: true, LastSeen: null)] };
        var vm = new TestViewModel(new NoopDebugSession(), data);
        data.RaiseDataLoaded();
        return new TestView { DataContext = vm };
    }

    /// <summary>The shell's shape: a nav row, the scrolling page area, a footer.</summary>
    private static (Window Window, ScrollViewer Scroll, Control Page) Shell(Control page, object viewModel, double windowHeight)
    {
        var host = new PageHost { DataContext = new HostState(page, viewModel) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = host,
        };
        var outer = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var nav = new Border { Height = NavHeight };
        var footer = new Border { Height = FooterHeight };
        Grid.SetRow(nav, 0); Grid.SetRow(scroll, 1); Grid.SetRow(footer, 2);
        outer.Children.Add(nav); outer.Children.Add(scroll); outer.Children.Add(footer);

        var window = new Window { Content = outer, Width = 1200, Height = windowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, scroll, page);
    }

    private static (Window Window, ScrollViewer Scroll, Control Page) ScribbleShell(double windowHeight)
    {
        var view = Scribble();
        return Shell(view, view.DataContext!, windowHeight);
    }

    private static double CanvasHeight(Control page) => page.GetVisualDescendants().OfType<PenTestCanvas>().Single().Bounds.Height;

    /// <summary>The point of the change: every pixel of window height goes to the canvas.</summary>
    [AvaloniaFact]
    public void TheCanvas_GrowsAndShrinksWithTheWindow()
    {
        var heights = new[] { 800.0, 1000.0, 1400.0 }.Select(h =>
        {
            var (window, _, page) = ScribbleShell(h);
            var canvas = CanvasHeight(page);
            window.Close();
            return canvas;
        }).ToArray();

        Assert.Equal(200, heights[1] - heights[0], 0.5);
        Assert.Equal(400, heights[2] - heights[1], 0.5);
    }

    /// <summary>It fills to the bottom of the page area, leaving only the page's own bottom padding, so there is
    /// no dead band under it and nothing to scroll.</summary>
    [AvaloniaFact]
    public void TheCanvas_ReachesTheBottomOfThePage_WithNothingToScroll()
    {
        var (window, scroll, page) = ScribbleShell(1200);

        var canvas = page.GetVisualDescendants().OfType<PenTestCanvas>().Single();
        var bottom = canvas.TranslatePoint(new Point(0, canvas.Bounds.Height), scroll)!.Value.Y;

        Assert.Equal(scroll.Viewport.Height - PagePaddingBottom, bottom, 1.0);
        Assert.Equal(scroll.Viewport.Height, scroll.Extent.Height, 0.5);
        window.Close();
    }

    /// <summary>Too short a window has no room to give: the canvas stops at its minimum and the page scrolls,
    /// rather than the bottom of the page being cut off with no way to reach it.</summary>
    [AvaloniaFact]
    public void InAWindowTooShort_TheCanvasKeepsItsMinimum_AndThePageScrolls()
    {
        var (window, scroll, page) = ScribbleShell(420);

        // The frame carries the 320 minimum; the paint surface inside it is a border's width smaller.
        var frame = page.GetVisualDescendants().OfType<PenTestCanvas>().Single().FindAncestorOfType<OpenTabletArtist.Controls.Canvas>()!;
        Assert.True(frame.Bounds.Height >= 320, $"the canvas frame is {frame.Bounds.Height} tall, under its minimum of 320");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the page does not scroll, so its lower part is out of reach");
        window.Close();
    }

    // --- Nothing else changes -----------------------------------------------------------------------------

    /// <summary>Every other page is as tall as its content, as it was in the stack. Stretching them would put
    /// their backgrounds and star rows in places they have never been.</summary>
    [AvaloniaFact]
    public void AnotherPage_StaysAsTallAsItsContent()
    {
        // An outer box with no height of its own around a 123-tall child: stretched, the outer one would be as
        // tall as the window; as tall as its content, it is 123.
        var content = new Border { Child = new Border { Height = 123 } };
        var (window, _, _) = Shell(content, new object(), 1200);

        Assert.Equal(123, content.Bounds.Height, 0.5);
        Assert.Equal(VerticalAlignment.Top, PageLayout.VerticalAlignmentFor(new object()));
        window.Close();
    }

    [AvaloniaFact]
    public void OnlyScribble_AsksToFill()
    {
        Assert.True(PageLayout.FillsHeight(new TestViewModel(new NoopDebugSession(), new FakeDeviceData())));
        Assert.False(PageLayout.FillsHeight(null));
        Assert.False(PageLayout.FillsHeight("home"));
    }
}
