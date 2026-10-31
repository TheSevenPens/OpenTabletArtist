using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The Daemon page as it is drawn (#1043, #1042): a plain status list, a button for OpenTabletDriver's own
/// window that is always there, and a banner that leads the page when the app refused a daemon.
/// </summary>
///
/// <remarks>
/// Watched across a change rather than read once. The page used to decide a card's visibility from a
/// property nothing told it had moved (#936), and a getter asked after the fact cannot show that.
/// </remarks>
public class DaemonPageTests
{
    private const double Width = 1100, Height = 900;

    /// <summary>The button exists whatever the daemon is doing. It used to appear only when the connected
    /// daemon had a window beside it, which left the page giving no sign the window existed.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]   // nothing running
    [InlineData(true, false)]    // attached
    [InlineData(false, true)]    // a daemon the app declined
    public void TheOpenTabletDriverButtonIsAlwaysThere(bool connected, bool refused)
    {
        var connection = new FakeConnectionState { IsConnected = connected, IsDaemonRefused = refused };
        using var shown = Show(connection);
        var (view, window) = (shown.View, shown.Window);

        var button = ButtonNamed(view, "Open OpenTabletDriver");
        Assert.NotNull(button);
        Assert.True(button!.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
    }

    /// <summary>The banner and its button follow the refusal in both directions.</summary>
    [AvaloniaFact]
    public void TheRefusalBannerAppearsAndGoesWithTheRefusal()
    {
        var connection = new FakeConnectionState();
        using var shown = Show(connection);
        var (view, window) = (shown.View, shown.Window);

        Assert.False(Banner(view)?.IsEffectivelyVisible ?? false, "nothing is refused yet");
        Assert.False(ButtonNamed(view, "Stop this daemon")?.IsEffectivelyVisible ?? false);

        connection.DaemonRefusalReason = "OpenTabletDriver 0.6.7 is running, but this version needs 0.6.8.";
        connection.IsDaemonRefused = true;
        Settle(window);

        Assert.True(Banner(view)?.IsEffectivelyVisible ?? false, "the refusal has to reach the screen");
        Assert.True(ButtonNamed(view, "Stop this daemon")?.IsEffectivelyVisible ?? false);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text?.Contains("0.6.7 is running") == true);

        connection.IsDaemonRefused = false;
        Settle(window);

        Assert.False(Banner(view)?.IsEffectivelyVisible ?? false, "and leave it when the daemon goes");
    }

    /// <summary>The banner leads the page: it is above the status, not somewhere beside it.</summary>
    [AvaloniaFact]
    public void TheBannerSitsAboveTheStatus()
    {
        var connection = new FakeConnectionState { IsDaemonRefused = true, DaemonRefusalReason = "wrong version" };
        using var shown = Show(connection);
        var (view, window) = (shown.View, shown.Window);

        var banner = Banner(view)!;
        var status = ButtonNamed(view, "Open OpenTabletDriver")!;

        Assert.True(banner.TranslatePoint(default, view)!.Value.Y < status.TranslatePoint(default, view)!.Value.Y);
    }

    /// <summary>The daemon's location is a long path, and it has to stay inside the section it is listed in.
    /// It used to run on in one line past the section's edge and was cut off there, with the room to its
    /// right empty: a horizontal panel measures its children without a width limit, so the text never
    /// wrapped. The whole path is there, wrapped; the tooltip and the button still carry it too.</summary>
    [AvaloniaFact]
    public void ALongDaemonLocationWrapsInsideItsSectionInsteadOfRunningOffTheEdge()
    {
        const string path = @"C:\Users\seven\Documents\GitHub\OpenTabletArtist\OpenTabletArtist\bin\Debug\net10.0\Daemon\OpenTabletDriver.Daemon.exe";
        var connection = new FakeConnectionState { IsConnected = true, DaemonSourcePath = path };
        using var shown = Show(connection);
        var view = shown.View;

        var section = view.GetVisualDescendants().OfType<Section>().First();
        var text = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("pathText"));

        Assert.Equal(path, text.Text);
        var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), view)!.Value.X;
        var sectionRight = section.TranslatePoint(new Point(section.Bounds.Width, 0), view)!.Value.X;
        Assert.True(right <= sectionRight + 0.5, $"the path ends at {right}, past its section's edge at {sectionRight}");
        Assert.True(text.Bounds.Height > text.FontSize * 2, "a path this long has to wrap onto more than one line");
    }

    private static Alert? Banner(DaemonView view) => view.GetVisualDescendants().OfType<Alert>().FirstOrDefault();

    private static Button? ButtonNamed(DaemonView view, string content) =>
        view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == content);

    /// <summary>The page in a window, closed with the test: nothing may outlive a test on the shared
    /// dispatcher.</summary>
    private sealed class ShownPage(DaemonView view, Window window) : System.IDisposable
    {
        public DaemonView View { get; } = view;
        public Window Window { get; } = window;
        public void Dispose() => Window.Close();
    }

    private static ShownPage Show(FakeConnectionState connection)
    {
        var page = new DaemonViewModel(new DaemonStatusViewModel(connection));
        var view = new DaemonView { DataContext = page };
        var window = new Window { Content = view, Width = Width, Height = Height };
        window.Show();
        Settle(window);
        return new ShownPage(view, window);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));
        Dispatcher.UIThread.RunJobs();
    }
}
