using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The driver card appears and disappears as the connected daemon changes (#936).
/// </summary>
///
/// <remarks>
/// <para>
/// <c>ShowDriverCard</c> is computed from <c>CanOpenOtdUx</c>, which reads the connected daemon's path.
/// Nothing told the page that path had changed, so the card was decided once and then kept whatever it
/// had decided: connect to a daemon that ships its own window and the button stays hidden; disconnect
/// from one and the empty card stays on screen.
/// </para>
/// <para>
/// <c>DriverCardVisibilityTests</c> could not catch this and still cannot: it asks the getters with the
/// path already in place, and the getters were always right. What was missing was the notification, so
/// the test has to watch the rendered card across a change rather than read a property once.
/// </para>
/// </remarks>
public class DriverCardFollowsTheDaemonTests
{
    /// <summary>A folder holding a daemon, and optionally the settings window beside it.</summary>
    private sealed class TempInstall : IDisposable
    {
        private readonly string _root;

        public TempInstall(bool withUx)
        {
            _root = Path.Combine(Path.GetTempPath(), $"ota-cardfollow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            Daemon = Add(DaemonExePaths.DaemonExeName);
            if (withUx) Add(DaemonExePaths.UxExeName);
        }

        public string Daemon { get; }

        private string Add(string fileName)
        {
            var path = Path.Combine(_root, fileName);
            File.WriteAllText(path, "");
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { /* a temp folder that outlives the test is not a failure */ }
        }
    }

    /// <summary>
    /// The card follows the daemon in both directions.
    /// </summary>
    /// <remarks>
    /// Both transitions, because they failed for the same reason and would be fixed by the same line —
    /// so testing only the appearing half would leave the disappearing half resting on an assumption.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]   // no daemon, then one that ships its own window: the card has to appear
    [InlineData(false)]  // that daemon, then nothing: the card has to go
    public void WhenTheConnectedDaemonChanges_TheCardFollows(bool appearing)
    {
        using var install = new TempInstall(withUx: true);

        var connection = new FakeConnectionState
        {
            IsConnected = true,
            DaemonSourcePath = appearing ? "" : install.Daemon,
        };
        var page = new DaemonViewModel(new DaemonStatusViewModel(connection));
        var view = new DaemonView { DataContext = page };
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        window.Show();
        window.Measure(new Size(1100, 900));
        window.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(!appearing, OtdWindowButton(view)?.IsEffectivelyVisible ?? false);

        connection.DaemonSourcePath = appearing ? install.Daemon : "";
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1100, 900));
        window.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(appearing, OtdWindowButton(view)?.IsEffectivelyVisible ?? false);
    }

    /// <summary>The button the card exists to hold, found by its content rather than its position.</summary>
    private static Button? OtdWindowButton(DaemonView view) =>
        view.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("own window") == true);
}
