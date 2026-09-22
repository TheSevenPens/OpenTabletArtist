using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The panel that says a pause has happened, and what leaving it costs (#920).
/// </summary>
///
/// <remarks>
/// <para>
/// A pause stops every settings edit in the app. It used to be announced by one line in the footer,
/// sharing that line with routine save status — and the hands-on pass found precisely that: it paused,
/// and the only sign was text at the very bottom of the window.
/// </para>
/// <para>
/// This lives in the page area because a pause frees that space up: the editing pages are already inert
/// while one is on the board. It is deliberately not a dialog. The pause is detected by a background
/// refresh, so OTA is often not the focused app, and a modal would steal focus for something the artist
/// did not ask for — and would stack on the calibration overlay, which is a modal window with an
/// interrupted state of its own.
/// </para>
/// </remarks>
public class SettingsPausedTests
{
    private sealed class Recording : ICommand
    {
        public int Ran;
#pragma warning disable CS0067   // required by ICommand; this fake never changes availability
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => Ran++;
    }

    private sealed class PageState
    {
        public SessionState SettingsSession { get; init; } = new();
        public Recording Reload { get; } = new();
        public ICommand ReloadSettingsCommand => Reload;
    }

    private sealed class SessionState
    {
        public bool SettingsPaused { get; init; }
        public bool SettingsBusy { get; init; }
        public string SaveStatusText { get; init; } = "";
    }

    private static (PageState State, Border Panel, Button[] Buttons) Panel(
        bool paused, bool busy = false, string status = "")
    {
        var state = new PageState
        {
            SettingsSession = new SessionState
            {
                SettingsPaused = paused,
                SettingsBusy = busy,
                SaveStatusText = status,
            },
        };
        var view = new SettingsPausedView();
        var window = new Window { Content = view, DataContext = state, Width = 1100 };
        window.Show();
        window.Measure(new Size(1100, 700));
        window.Arrange(new Rect(0, 0, 1100, 700));
        Dispatcher.UIThread.RunJobs();

        var panel = view.GetVisualDescendants().OfType<Border>().First();
        return (state, panel, view.GetVisualDescendants().OfType<Button>().ToArray());
    }

    /// <summary>It is not there the rest of the time.</summary>
    /// <remarks>
    /// A standing banner about a state the app is not in is the thing an artist learns to stop reading,
    /// which is how the footer line stopped working.
    /// </remarks>
    [AvaloniaFact]
    public void WhenNothingIsPaused_ThePanelIsNotOnScreen()
    {
        var (_, panel, _) = Panel(paused: false);

        Assert.False(panel.IsEffectivelyVisible);
    }

    /// <summary>When one is on the board it says which pause this is, and offers the way out.</summary>
    [AvaloniaFact]
    public void WhilePaused_ItNamesThePauseAndOffersTheWayOut()
    {
        const string said = "Driver settings changed. Reload to continue.";
        var (state, panel, buttons) = Panel(paused: true, status: said);

        Assert.True(panel.IsEffectivelyVisible);

        var text = panel.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Editing paused", text);
        Assert.Contains(said, text);

        var reload = Assert.Single(buttons);
        Assert.Equal("Reload settings", reload.Content);
        Assert.True(reload.IsEffectivelyEnabled);

        reload.Command!.Execute(null);
        Assert.Equal(1, state.Reload.Ran);
    }

    /// <summary>
    /// And it says what reloading will cost, which is the whole reason it is not a status line.
    /// </summary>
    /// <remarks>
    /// Reloading takes the driver's current values, so an edit typed and not yet submitted goes with it.
    /// A few words at the bottom of the window had nowhere to put that, and after the status lines were
    /// cut to a glance's worth, nowhere to put what caused the pause either.
    /// </remarks>
    [AvaloniaFact]
    public void ItSaysWhatReloadingCosts()
    {
        var (_, panel, _) = Panel(paused: true);

        var prose = string.Join(" ", panel.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? ""));

        Assert.Contains("had not submitted yet will go", prose);
        Assert.Contains("editing stays off across the whole app", prose);

        // And what caused it, which the footer line no longer has room for.
        Assert.Contains("Attaching a new tablet can cause this", prose);
    }

    /// <summary>The one action still waits for an operation already running.</summary>
    [AvaloniaFact]
    public void WhileBusy_TheWayOutWaitsItsTurn()
    {
        var (_, _, buttons) = Panel(paused: true, busy: true);

        Assert.False(Assert.Single(buttons).IsEffectivelyEnabled);
    }
}
