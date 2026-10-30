using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OtdInterop;
using OtdInterop.Tests;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The health indicator as it is drawn (#1050): the badge on the Health tab, the Health page it leads to, and the
/// presets that now sit where the findings used to be on Home.
/// </summary>
public class HealthBadgeViewTests
{
    /// <summary>Each tier draws exactly one icon, and it is that tier's own. Colour is not enough to tell a
    /// suggestion from a misconfiguration, so the shape has to differ and has to actually be drawn.</summary>
    [AvaloniaTheory]
    [InlineData(HealthBadgeTier.Information, "PART_IconInfo")]
    [InlineData(HealthBadgeTier.Recommendation, "PART_IconSuggestion")]
    [InlineData(HealthBadgeTier.Misconfigured, "PART_IconMisconfigured")]
    [InlineData(HealthBadgeTier.Broken, "PART_IconBroken")]
    public void EveryTier_DrawsExactlyItsOwnIcon(HealthBadgeTier tier, string expectedIcon)
    {
        var tab = Tab(tier, 2);
        var window = HarnessTests.Show(tab);

        var drawn = tab.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsEffectivelyVisible).Select(i => i.Name).ToList();

        Assert.Equal([expectedIcon], drawn);
        window.Close();
    }

    /// <summary>A healthy app shows a plain word: no badge, no number, no icon.</summary>
    [AvaloniaFact]
    public void WithNothingToCount_TheTabIsAPlainWord()
    {
        var tab = Tab(HealthBadgeTier.Healthy, 0);
        var window = HarnessTests.Show(tab);

        Assert.DoesNotContain(tab.GetVisualDescendants().OfType<PathIcon>(), i => i.IsEffectivelyVisible);
        Assert.DoesNotContain(tab.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "0" && t.IsEffectivelyVisible);
        // The word is the theme's own ("Word"): a tab that never got the nav template would still show a word.
        Assert.Contains(tab.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "Word" && t.Text == "health" && t.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNumber_IsShownBesideTheWord_AndGoesWhenTheFindingsDo()
    {
        var tab = Tab(HealthBadgeTier.Broken, 4);
        var window = HarnessTests.Show(tab);

        var texts = tab.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("health", texts);
        Assert.Contains("4", texts);

        tab.BadgeCount = 0;
        tab.BadgeTier = HealthBadgeTier.Healthy;
        Dispatcher.UIThread.RunJobs();

        // The badge itself goes, not just the old number: no "0" in its place, no icon, no border.
        Assert.DoesNotContain(tab.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is "4" or "0" && t.IsEffectivelyVisible);
        Assert.DoesNotContain(tab.GetVisualDescendants().OfType<Border>(), b => b.Name == "PART_Badge" && b.IsEffectivelyVisible);
        window.Close();
    }

    /// <summary>The tiers are told apart by tint as well as shape.</summary>
    [AvaloniaFact]
    public void EachTier_HasItsOwnBorder()
    {
        var borders = new[]
        {
            HealthBadgeTier.Information, HealthBadgeTier.Recommendation,
            HealthBadgeTier.Misconfigured, HealthBadgeTier.Broken,
        }.Select(tier =>
        {
            var tab = Tab(tier, 1);
            var window = HarnessTests.Show(tab);
            var badge = tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Badge");
            var color = ((Avalonia.Media.ISolidColorBrush)badge.BorderBrush!).Color;
            window.Close();
            return color;
        }).ToList();

        Assert.Equal(borders.Count, borders.Distinct().Count());
    }

    /// <summary>The badge is part of the tab: pressing it is pressing the tab, so one click goes where the
    /// word goes.</summary>
    [AvaloniaFact]
    public void ClickingTheBadge_RunsTheTabsCommand()
    {
        var ran = 0;
        var tab = Tab(HealthBadgeTier.Broken, 1);
        tab.Command = new Command(() => ran++);
        var window = HarnessTests.Show(tab);
        var badge = tab.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Badge");

        PenButtonCardTests.Click(window, badge);

        Assert.Equal(1, ran);
        window.Close();
    }

    private static NavTab Tab(HealthBadgeTier tier, int count) => new()
    {
        Theme = (Avalonia.Styling.ControlTheme)Avalonia.Application.Current!.FindResource("WordmarkNav")!,
        Content = "health",
        BadgeTier = tier,
        BadgeCount = count,
    };

    // --- The Health page --------------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheHealthPage_NamesEachFindingsTier_AndCountsThem()
    {
        var (view, health, _, window) = HealthPage();
        Add(health, new HealthIssue("a", HealthSeverity.Broken, "Something is broken", "It is.", Remediation: null));
        Add(health, new HealthIssue("b", HealthSeverity.Recommendation, "Try this", "Why.", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Contains("BROKEN", texts);
        Assert.Contains("SUGGESTION", texts);
        Assert.Contains("2 findings, worst first.", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void TheHealthPage_SaysSoWhenThereIsNothing()
    {
        var (view, _, _, window) = HealthPage();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Contains("Nothing needs attention.", texts);
        window.Close();
    }

    /// <summary>With no daemon the page must not say nothing needs attention above a card saying nothing is
    /// connected.</summary>
    [AvaloniaFact]
    public void TheHealthPage_CountsADaemonThatIsDown_AndLeadsWithIt()
    {
        var (view, _, connection, window) = HealthPage();

        connection.IsConnected = false;
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("1 finding.", texts);
        Assert.DoesNotContain("Nothing needs attention.", texts);
        Assert.Contains("Not connected to daemon", texts);
        Assert.Contains("BROKEN", texts);
        window.Close();
    }

    /// <summary>A refused daemon is one problem with one card, the one in the findings. The daemon card
    /// steps aside, so the page shows what the badge counts and not twice that.</summary>
    [AvaloniaFact]
    public void ARefusedDaemon_ShowsOneCard_NotTwo()
    {
        var (view, health, connection, window) = HealthPage();
        connection.IsConnected = false;
        connection.IsDaemonRefused = true;
        Add(health, new HealthIssue("otd.versionBlocked", HealthSeverity.Broken, "OpenTabletArtist isn't connected",
            "OpenTabletDriver 0.6.7 is running.", Remediation: null));
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Equal(1, texts.Count(t => t == "BROKEN"));
        Assert.Contains("1 finding.", texts);
        Assert.DoesNotContain("Not connected to daemon", texts);
        Assert.DoesNotContain("Not connected: the running daemon is the wrong version", texts);
        window.Close();
    }

    /// <summary>While the daemon is connecting the spinner says so; a line saying nothing needs attention
    /// above it would be wrong a moment later.</summary>
    [AvaloniaFact]
    public void WhileConnecting_ThePageDoesNotClaimAllIsWell()
    {
        var (view, _, connection, window) = HealthPage();
        connection.IsConnected = false;
        connection.ShowDaemonActivity = true;
        connection.DaemonActivityText = "Connecting to daemon...";
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.DoesNotContain("Nothing needs attention.", texts);
        Assert.Contains("Connecting to daemon...", texts);
        window.Close();
    }

    // --- Home's presets ---------------------------------------------------------------------------------

    /// <summary>One button per preset, the last-loaded one marked, and pressing a button loads that preset
    /// through the same call the Presets page makes. The button is clicked, not the command called, because
    /// the binding between them is what could be broken.</summary>
    [AvaloniaFact]
    public async Task HomeListsThePresets_MarksTheLastLoaded_AndLoadsOnAClick()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"otahomepresets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var store = new PresetStore(NullOtdLog.Instance);
            store.Save(new Settings(), Path.Combine(dir, "Inking.json"));
            store.Save(new Settings(), Path.Combine(dir, "Sketching.json"));
            var coordinator = new FakeSettingsCoordinator { CurrentSettings = new Settings() };
            var presets = new PresetsViewModel(store, coordinator, new FakeDeviceData(), new FakeDialogService(),
                new NoHotkeys(), new ProfileSwitchService(coordinator, store, () => dir))
            { PresetDirectory = dir };
            await presets.LoadAsync();
            var home = new DashboardViewModel(new FakeDialogService(), new TabletsOverviewViewModel(), presets);
            var view = new DashboardView { DataContext = home };
            var window = HarnessTests.Show(view);

            Assert.Equal(["Inking", "Sketching"], VisiblePresetButtons(view, home).Select(Name).OrderBy(n => n).ToArray());
            Assert.Empty(MarkedButtons(view, home));

            PenButtonCardTests.Click(window, VisiblePresetButtons(view, home).Single(b => Name(b) == "Sketching"));
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            var marked = Assert.Single(MarkedButtons(view, home));
            Assert.Equal("Sketching", Name(marked));
            Assert.Equal(2, VisiblePresetButtons(view, home).Count());

            // What a screen reader says. A button whose content is a TextBlock is otherwise announced by the
            // type name ("Avalonia.Controls.TextBlock"), which is what the first live run showed.
            Assert.Equal("Sketching, loaded last", AutomationProperties.GetName(marked));
            Assert.Equal("Inking", AutomationProperties.GetName(VisiblePresetButtons(view, home).Single(b => Name(b) == "Inking")));
            window.Close();
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>The preset buttons: the ones wired to Home's load command. (The About links below them are
    /// buttons with a string parameter too.)</summary>
    private static System.Collections.Generic.IEnumerable<Button> VisiblePresetButtons(DashboardView view, DashboardViewModel home) =>
        view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && ReferenceEquals(b.Command, home.LoadPresetCommand));

    /// <summary>A preset button that carries the tick: the last-loaded one.</summary>
    private static System.Collections.Generic.IEnumerable<Button> MarkedButtons(DashboardView view, DashboardViewModel home) =>
        VisiblePresetButtons(view, home).Where(b => b.GetVisualDescendants().OfType<PathIcon>().Any(i => i.IsEffectivelyVisible));

    private static string Name(Button b) => (string)b.CommandParameter!;

    /// <summary>Adds a finding the way the service does: the list and the flag that says it has any.</summary>
    private static void Add(HealthService health, HealthIssue issue)
    {
        health.Issues.Add(issue);
        health.HasIssues = health.Issues.Count > 0;
    }

    private static (HealthView View, HealthService Health, FakeConnectionState Connection, Window Window) HealthPage()
    {
        var connection = new FakeConnectionState { IsConnected = true };
        var health = new HealthService(connection, new FakeDeviceData(), new DriverConflictMonitor(),
            _ => new TaskCompletionSource<OtdHealth.Collector.HealthAnalysisReport>().Task);
        health.Issues.Clear();
        var session = new AppSession(FakeSession.Over(new FakeDaemonTransport(), new MemorySettingsFileStore()), new FakeLifecycle());
        var page = new HealthPageViewModel(session, new DaemonStatusViewModel(connection), health,
            navigateToTablet: (_, _) => { });
        var view = new HealthView { DataContext = page };
        return (view, health, connection, HarnessTests.Show(view));
    }

    private sealed class Command(Action run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }

    private sealed class NoHotkeys : IProfileHotkeys
    {
        public HotkeyChord? GetChord(string snapshot) => null;
        public HotkeySetResult SetHotkey(string snapshot, HotkeyChord chord) => HotkeySetResult.Ok;
        public void ClearHotkey(string snapshot) { }
        public void Sync(System.Collections.Generic.IEnumerable<string> snapshotNames) { }
        public void RenameSnapshot(string oldName, string newName) { }
    }
}
