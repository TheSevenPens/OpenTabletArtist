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
/// The health indicator as it is drawn (#1050): the chip in the top bar, the Health page it opens, and the
/// presets that now sit where the findings used to be on Home.
/// </summary>
public class HealthChipViewTests
{
    /// <summary>Each tier shows exactly one icon, and it is that tier's own. Colour is not enough to tell a
    /// suggestion from a misconfiguration, so the shape has to differ and has to actually be drawn.</summary>
    [AvaloniaTheory]
    [InlineData(HealthChipTier.Healthy, "PART_IconHealthy")]
    [InlineData(HealthChipTier.Information, "PART_IconInfo")]
    [InlineData(HealthChipTier.Recommendation, "PART_IconSuggestion")]
    [InlineData(HealthChipTier.Misconfigured, "PART_IconMisconfigured")]
    [InlineData(HealthChipTier.Broken, "PART_IconBroken")]
    public void EveryTier_DrawsExactlyItsOwnIcon(HealthChipTier tier, string expectedIcon)
    {
        var chip = new HealthChip { Tier = tier, Label = "x", Count = 2 };
        var window = HarnessTests.Show(chip);

        var drawn = chip.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsVisible).Select(i => i.Name).ToList();

        Assert.Equal([expectedIcon], drawn);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNumber_IsShownWhenThereAreFindings_AndHiddenWhenThereAreNone()
    {
        var chip = new HealthChip { Tier = HealthChipTier.Broken, Label = "broken", Count = 4 };
        var window = HarnessTests.Show(chip);

        Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "4" && t.IsEffectivelyVisible);

        chip.Count = 0;
        chip.Tier = HealthChipTier.Healthy;
        chip.Label = "healthy";
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "0" && t.IsEffectivelyVisible);
        Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "healthy" && t.IsEffectivelyVisible);
        window.Close();
    }

    /// <summary>The tiers are told apart by tint as well as shape, and information is the plain one.</summary>
    [AvaloniaFact]
    public void EachTier_HasItsOwnBorder_AndInformationIsTheOneThatLooksCalm()
    {
        var borders = new[]
        {
            HealthChipTier.Healthy, HealthChipTier.Information, HealthChipTier.Recommendation,
            HealthChipTier.Misconfigured, HealthChipTier.Broken,
        }.Select(tier =>
        {
            var chip = new HealthChip { Tier = tier, Label = "x", Count = 1 };
            var window = HarnessTests.Show(chip);
            var border = chip.GetVisualDescendants().OfType<Border>().First().BorderBrush as Avalonia.Media.ISolidColorBrush;
            window.Close();
            return (tier, color: border!.Color);
        }).ToList();

        Assert.Equal(borders.Count, borders.Select(b => b.color).Distinct().Count());
    }

    [AvaloniaFact]
    public void ClickingTheChip_RunsItsCommand()
    {
        var ran = 0;
        var chip = new HealthChip { Tier = HealthChipTier.Broken, Label = "broken", Count = 1, Command = new Command(() => ran++) };
        var window = HarnessTests.Show(chip);

        PenButtonCardTests.Click(window, chip);

        Assert.Equal(1, ran);
        window.Close();
    }

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
    /// steps aside, so the page shows what the chip counts and not twice that.</summary>
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
