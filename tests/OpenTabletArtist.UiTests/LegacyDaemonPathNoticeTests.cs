using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The upgrade notice on Home, through the button an artist actually presses (#947).
/// </summary>
///
/// <remarks>
/// <para>
/// The evaluator tests prove the row is produced, and a service-level test would prove the preference is
/// read — but neither covers the join that matters: that the row's action reaches the key it means, and
/// that the write lands on disk rather than in a cached object. Those are separate failures and both are
/// silent.
/// </para>
/// <para>
/// No connection is started. <c>AppSession</c> is built over the fake transport and never asked to
/// connect, which is enough for the dashboard to exist and the health list to evaluate.
/// </para>
/// </remarks>
public class LegacyDaemonPathNoticeTests
{
    private const string OldDaemon = @"D:\portable-otd\OpenTabletDriver.Daemon.exe";
    private const string LegacyPathKey = "daemon.userPath";

    /// <summary>
    /// Pressing "Got it" answers the row, on disk, without touching the artist's stored location.
    /// </summary>
    /// <remarks>
    /// The second and third assertions are the point. A fix that silenced the row by clearing the old
    /// path would satisfy the first; one that only updated an in-memory cache would satisfy the first
    /// two and lose the answer on the next launch.
    /// </remarks>
    [AvaloniaFact]
    public async Task PressingGotIt_AnswersTheRowOnDisk_AndKeepsTheOldPath()
    {
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "");
        AppSettings.Set(LegacyPathKey, OldDaemon);

        var (view, health, session) = Home();
        using var lifetime = session;

        var button = NoticeButton(view);
        Assert.NotNull(button);

        button!.Command!.Execute(button.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();

        Assert.DoesNotContain(health.Issues, i => i.Id == "daemon.ignoredPath");

        // Read back off disk, by value.
        //
        // This asserted only that the key was PRESENT, which the setup above had already written as an
        // empty string — so skipping the production write entirely still passed, because the row had
        // gone from the in-memory state (#947). The same shape as a probe that cannot fail, in the test
        // whose whole job was to prove the write reached the file.
        var saved = JObject.Parse(File.ReadAllText(Path.Combine(AppPaths.LocalAppData, "settings.json")));

        Assert.Equal("true", saved[HealthService.LegacyPathNoticeAcknowledgedKey]?.ToString());

        // And the location they chose is still there for a downgrade to find — also from the file,
        // rather than from the cache the writer holds.
        Assert.Equal(OldDaemon, saved[LegacyPathKey]?.ToString());
    }

    /// <summary>Someone who never used the picker is never shown it.</summary>
    /// <remarks>
    /// The control. Without it the test above would pass just as well against a dashboard that offered
    /// this row to everybody.
    /// </remarks>
    [AvaloniaFact]
    public void WithNoStoredPath_TheRowIsNotOnHome()
    {
        AppSettings.Set(HealthService.LegacyPathNoticeAcknowledgedKey, "");
        AppSettings.Set(LegacyPathKey, "");

        var (view, health, session) = Home();
        using var lifetime = session;

        Assert.DoesNotContain(health.Issues, i => i.Id == "daemon.ignoredPath");
        Assert.Null(NoticeButton(view));
    }

    /// <summary>The dashboard, rendered, with nothing connected.</summary>
    private static (DashboardView View, HealthService Health, AppSession Session) Home()
    {
        var daemon = new FakeDaemonTransport();
        var store = new MemorySettingsFileStore();
        var session = new AppSession(FakeSession.Over(daemon, store), new FakeLifecycle());
        var health = new HealthService(session, new FakeDeviceData(), new DriverConflictMonitor());

        var page = new DashboardViewModel(
            session,
            new DaemonStatusViewModel(session),
            new FakeDialogService(),
            navigateToTablet: (_, _) => { },
            health,
            new TabletsOverviewViewModel());

        var view = new DashboardView { DataContext = page };
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        window.Show();
        window.Measure(new Size(1100, 900));
        window.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();
        return (view, health, session);
    }

    /// <summary>The row's action, found by the words on it rather than by position.</summary>
    private static Button? NoticeButton(DashboardView view) =>
        view.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "Got it");
}
