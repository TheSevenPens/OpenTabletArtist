using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Home as two columns (#1050): your tablets alone on the left, and on the right About, a one-card health
/// summary, your presets and the daemon, always in that order.
/// </summary>
public class HomeLayoutTests
{
    /// <summary>The order is fixed. It is the point of the layout that nothing on the right moves when the
    /// state of health or the daemon changes, so a person always looks in the same place.</summary>
    [AvaloniaTheory]
    [InlineData(HealthSeverity.Broken)]
    [InlineData(HealthSeverity.Information)]
    public void TheRightColumn_IsAboutHealthPresetsDaemon_Whatever_TheHealth(HealthSeverity worst)
    {
        var page = Home(out var health, out _, out _);
        Add(health, new HealthIssue("x", worst, "x", "", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        var headings = Headings(page.View, "your tablets", "about", "health", "presets", "daemon");

        Assert.True(headings["about"].Y < headings["health"].Y, "about comes before health");
        Assert.True(headings["health"].Y < headings["presets"].Y, "health comes before presets");
        Assert.True(headings["presets"].Y < headings["daemon"].Y, "presets come before the daemon");
        page.Close();
    }

    [AvaloniaFact]
    public void YourTablets_AreTheOnlyThingOnTheLeft()
    {
        var page = Home(out _, out _, out _);

        var headings = Headings(page.View, "your tablets", "about", "health", "presets", "daemon");

        foreach (var right in new[] { "about", "health", "presets", "daemon" })
            Assert.True(headings[right].X > headings["your tablets"].X + 300, $"{right} is in the right-hand column");
        page.Close();
    }

    // --- Remembered tablets -------------------------------------------------------------------------------

    /// <summary>The remembered tablets are behind one row: shown as a count, opened on a click, closed on another.
    /// Without this a machine that has met many tablets pushes everything else out of sight, which is what
    /// started this redesign.</summary>
    [AvaloniaFact]
    public void RememberedTablets_AreOneRow_UntilOpened()
    {
        var page = Home(out _, out var tablets, out _);
        tablets.Tablets =
        [
            Tablet("Wacom PTK-670", true), Tablet("Huion HS611", false), Tablet("Wacom PTK-470", false), Tablet("XP-Pen Deco L", false),
        ];
        tablets.HasTablets = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Wacom PTK-670", VisibleTexts(page.View));
        Assert.DoesNotContain("Huion HS611", VisibleTexts(page.View));
        Assert.Contains("3 remembered tablets", VisibleTexts(page.View));

        var row = page.View.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "3 remembered tablets");
        PenButtonCardTests.Click(page.Window, row);

        Assert.Contains("Huion HS611", VisibleTexts(page.View));
        Assert.Contains("XP-Pen Deco L", VisibleTexts(page.View));

        PenButtonCardTests.Click(page.Window, row);

        Assert.DoesNotContain("Huion HS611", VisibleTexts(page.View));
        page.Close();
    }

    [AvaloniaFact]
    public void WithNothingPluggedIn_TheLeftSaysSo_AndOffersTheRememberedOnes()
    {
        var page = Home(out _, out var tablets, out _);
        tablets.Tablets = [Tablet("Huion HS611", false)];
        tablets.HasTablets = true;
        Dispatcher.UIThread.RunJobs();

        var texts = VisibleTexts(page.View);

        Assert.Contains("No tablet connected", texts);
        Assert.Contains("1 remembered tablet", texts);
        Assert.DoesNotContain("No tablets connected or remembered", texts);
        page.Close();
    }

    // --- The health card ----------------------------------------------------------------------------------

    /// <summary>The card does not grow: it is the same height whether everything is fine or something is broken,
    /// and however many findings there are. It lists none.</summary>
    [AvaloniaFact]
    public void TheHealthCard_IsTheSameHeight_InEveryState()
    {
        var heights = new List<double>();
        foreach (var worst in new HealthSeverity?[] { null, HealthSeverity.Information, HealthSeverity.Recommendation, HealthSeverity.Misconfigured, HealthSeverity.Broken })
        {
            var page = Home(out var health, out _, out _);
            if (worst is { } w)
                for (var i = 0; i < 5; i++) Add(health, new HealthIssue($"x{i}", w, $"Finding {i}", "Detail.", Remediation: null));
            Dispatcher.UIThread.RunJobs();

            heights.Add(CardHeight(page.View));
            Assert.DoesNotContain("Finding 0", VisibleTexts(page.View));
            page.Close();
        }

        Assert.Single(heights.Distinct());
    }

    [AvaloniaTheory]
    [InlineData(null, "Everything's working fine")]
    [InlineData(HealthSeverity.Information, "Worth a look")]
    [InlineData(HealthSeverity.Recommendation, "There is a way to improve things")]
    [InlineData(HealthSeverity.Misconfigured, "Action recommended")]
    [InlineData(HealthSeverity.Broken, "Action needed")]
    public void TheHealthCard_SaysWhatItShould(HealthSeverity? worst, string headline)
    {
        var page = Home(out var health, out _, out _);
        if (worst is { } w) Add(health, new HealthIssue("x", w, "x", "", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(headline, VisibleTexts(page.View));
        page.Close();
    }

    /// <summary>Only the states that ask for action get the loud (accent) button.</summary>
    [AvaloniaTheory]
    [InlineData(HealthSeverity.Broken, true)]
    [InlineData(HealthSeverity.Misconfigured, true)]
    [InlineData(HealthSeverity.Recommendation, false)]
    [InlineData(HealthSeverity.Information, false)]
    public void OnlyTheSevereCards_HaveTheLoudButton(HealthSeverity worst, bool loud)
    {
        var page = Home(out var health, out _, out _);
        Add(health, new HealthIssue("x", worst, "x", "", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        var button = HealthButton(page.View, page.Model);

        var accent = Application.Current!.FindResource("AccentButton");
        Assert.Equal(loud, ReferenceEquals(button.Theme, accent));
        page.Close();
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(HealthSeverity.Misconfigured)]
    public void TheHealthCardsButton_GoesToTheHealthPage(HealthSeverity? worst)
    {
        var page = Home(out var health, out _, out var opened);
        if (worst is { } w) Add(health, new HealthIssue("x", w, "x", "", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        PenButtonCardTests.Click(page.Window, HealthButton(page.View, page.Model));

        Assert.Equal(1, opened.Health);
        page.Close();
    }

    // --- The daemon card ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheDaemonCard_FollowsTheConnection_AndLeadsToTheDaemonPage()
    {
        var page = Home(out _, out _, out var opened, out var connection);
        Assert.Contains("Connected", VisibleTexts(page.View));
        Assert.Contains("OpenTabletDriver 0.6.8.1, bundled with OpenTabletArtist", VisibleTexts(page.View));

        connection.IsConnected = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Not connected", VisibleTexts(page.View));
        Assert.DoesNotContain("Connected", VisibleTexts(page.View));

        var link = page.View.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Open daemon page");
        PenButtonCardTests.Click(page.Window, link);

        Assert.Equal(1, opened.Daemon);
        page.Close();
    }

    // --- Harness ------------------------------------------------------------------------------------------

    internal sealed class Opened { public int Health; public int Daemon; }

    internal sealed record Shown(Window Window, DashboardView View, DashboardViewModel Model)
    {
        public void Close() => Window.Close();
    }

    internal static Shown Home(out HealthService health, out TabletsOverviewViewModel tablets, out Opened opened) =>
        Home(out health, out tablets, out opened, out _);

    internal static Shown Home(out HealthService health, out TabletsOverviewViewModel tablets, out Opened opened,
        out FakeConnectionState connection)
    {
        connection = new FakeConnectionState { IsConnected = true, DaemonVersion = "0.6.8.1" };
        health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor(),
            _ => new TaskCompletionSource<OtdHealth.Collector.HealthAnalysisReport>().Task);
        health.Issues.Clear();
        var counter = new Opened();
        opened = counter;
        var daemon = new DaemonStatusViewModel(connection, () => counter.Daemon++);
        tablets = new TabletsOverviewViewModel();
        var presets = new PresetsViewModel(new PresetStore(OtdInterop.Tests.NullOtdLog.Instance), new FakeSettingsCoordinator(),
            new FakeDeviceData(), new FakeDialogService(), new NoHotkeys(),
            new ProfileSwitchService(new FakeSettingsCoordinator(), new PresetStore(OtdInterop.Tests.NullOtdLog.Instance), () => ""));
        var badge = new HealthBadgeViewModel(health, daemon);
        var model = new DashboardViewModel(new FakeDialogService(), tablets, presets, badge, daemon, () => counter.Health++);
        var view = new DashboardView { DataContext = model };
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        window.Show();
        window.Measure(new Size(1280, 900));
        window.Arrange(new Rect(0, 0, 1280, 900));
        Dispatcher.UIThread.RunJobs();
        return new Shown(window, view, model);
    }

    private static void Add(HealthService health, HealthIssue issue)
    {
        health.Issues.Add(issue);
        health.HasIssues = health.Issues.Count > 0;
    }

    private static TabletOverviewItemViewModel Tablet(string name, bool detected) =>
        new(name, detected, detected ? "Detected" : "Not detected", detected ? "CONNECTED" : "Last seen Oct 27", () => { }, () => Task.CompletedTask);

    private static List<string?> VisibleTexts(Control view) =>
        view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    private static Dictionary<string, Point> Headings(Control view, params string[] names) =>
        names.ToDictionary(n => n, n =>
        {
            var block = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == n && t.IsEffectivelyVisible);
            return block.TranslatePoint(new Point(0, 0), view)!.Value;
        });

    /// <summary>The card's own height: the alert when there are findings, the plain card when there are none.</summary>
    private static double CardHeight(Control view) =>
        view.GetVisualDescendants().OfType<Control>().Single(c => c.Height == 112 && c.IsEffectivelyVisible).Bounds.Height;

    private static Button HealthButton(Control view, DashboardViewModel model) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, model.OpenHealthCommand));

    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }
}
