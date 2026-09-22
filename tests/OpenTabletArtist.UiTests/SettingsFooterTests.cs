using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The settings footer: three ranked actions, and which of them a paused session still offers.
/// </summary>
///
/// <remarks>
/// <para>
/// Save, Reload driver settings and Revert to saved were three buttons of equal weight, with the rarest
/// reading first. They are still three buttons — ranking and weight were the whole complaint, and
/// ranking them is all it needed.
/// </para>
/// <para>
/// <b>A SplitButton was tried and withdrawn, which is why these tests exist.</b> Save and Revert need
/// an editable session; Reload does not, and a paused session is exactly when it is needed, because it
/// is the way out. Binding a SplitButton's <c>IsEnabled</c> takes the chevron with it, and so does
/// letting its command report that it cannot execute. Disabling only the primary template part looked
/// like the answer and was not: Codex found that the keyboard and automation paths still reach the
/// primary action, and a probe here confirmed it — Space and Enter each fired Save while the primary
/// half was visibly disabled. Independent buttons cannot have that problem, and these check it through
/// the real control rather than through the markup.
/// </para>
/// </remarks>
public class SettingsFooterTests
{
    private sealed class Recording : ICommand
    {
        public int Ran;
#pragma warning disable CS0067   // required by ICommand; these fakes never change availability
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => Ran++;
    }

    /// <summary>What the footer binds, and nothing else. Inert: no session, no daemon, no timers.</summary>
    private sealed class FooterState
    {
        public SessionState SettingsSession { get; init; } = new();
        public ConnectionState Connection { get; init; } = new();
        public Recording Save { get; } = new();
        public Recording Reload { get; } = new();
        public Recording Revert { get; } = new();
        public ICommand SaveSettingsCommand => Save;
        public ICommand ReloadSettingsCommand => Reload;
        public ICommand RevertSettingsCommand => Revert;
    }

    private sealed class SessionState
    {
        public bool CanEditSettings { get; init; }
        public bool SettingsBusy { get; init; }
        public string SaveStatusText { get; init; } = "";
    }

    private sealed class ConnectionState
    {
        public string DiscardedChangeNotice => "";
        public bool HasDiscardedChangeNotice => false;
        public string SettingsRefreshedNotice => "";
        public bool HasSettingsRefreshedNotice => false;
    }

    private static (Window Window, FooterState State, Button[] Buttons) Footer(
        bool canEdit, bool busy = false, string status = "", double width = 1100)
    {
        var state = new FooterState
        {
            SettingsSession = new SessionState
            {
                CanEditSettings = canEdit,
                SettingsBusy = busy,
                SaveStatusText = status,
            },
        };
        var footer = new SettingsFooterView();
        var window = new Window { Content = footer, DataContext = state, Width = width };
        window.Show();
        window.Measure(new Size(width, 600));
        window.Arrange(new Rect(0, 0, width, 600));
        Dispatcher.UIThread.RunJobs();

        var buttons = footer.GetVisualDescendants().OfType<Button>().ToArray();
        return (window, state, buttons);
    }

    private static Button Named(Button[] buttons, string content) =>
        buttons.Single(b => (b.Content as string) == content);

    /// <summary>Ranked, and in the order the app uses everywhere else: affirmative first (#502).</summary>
    [AvaloniaFact]
    public void TheThreeActionsReadInOrderOfHowOftenTheyAreWanted()
    {
        var (_, _, buttons) = Footer(canEdit: true);

        Assert.Equal(["Save", "Reload driver settings", "Revert to saved"],
            buttons.Select(b => b.Content as string));
    }

    /// <summary>
    /// A paused session offers Reload and nothing else.
    /// </summary>
    /// <remarks>
    /// The one that matters. Reload is how an artist leaves a pause; if it goes dark with Save, the page
    /// is a dead end.
    /// </remarks>
    [AvaloniaFact]
    public void WhenSettingsCannotBeEdited_OnlyReloadIsOffered()
    {
        var (_, _, buttons) = Footer(canEdit: false);

        Assert.False(Named(buttons, "Save").IsEffectivelyEnabled);
        Assert.False(Named(buttons, "Revert to saved").IsEffectivelyEnabled);
        Assert.True(Named(buttons, "Reload driver settings").IsEffectivelyEnabled,
            "the way out of a pause went dark with the rest");
    }

    /// <summary>
    /// And an unavailable Save cannot be reached by keyboard either.
    /// </summary>
    /// <remarks>
    /// This is the defect that ended the SplitButton: it disabled the primary half's appearance and its
    /// pointer path, while Space and Enter went on firing Save through the parent control. A plain
    /// disabled Button has no second path to close, and this says so rather than assuming it.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnavailableSaveCannotBeActivatedByKeyboard()
    {
        var (_, state, buttons) = Footer(canEdit: false);
        var save = Named(buttons, "Save");

        foreach (var key in new[] { Key.Space, Key.Enter })
        {
            save.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = key,
                Source = save,
            });
            save.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyUpEvent,
                Key = key,
                Source = save,
            });
        }
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, state.Save.Ran);
    }

    /// <summary>Everything stops while an operation is in flight, Reload included.</summary>
    /// <remarks>
    /// The busy gate is a different thing from a pause: it means an operation of ours is running, and
    /// starting a second one would race it. Reload is no exception to that, and is to this.
    /// </remarks>
    [AvaloniaFact]
    public void WhileBusy_NothingIsOffered()
    {
        var (_, _, buttons) = Footer(canEdit: true, busy: true);

        Assert.All(buttons, b => Assert.False(b.IsEffectivelyEnabled));
    }

    /// <summary>The status line shows what the session says, and nothing when it says nothing.</summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("Unsaved changes")]
    public void TheStatusLineShowsWhatTheSessionSays(string status)
    {
        var (window, _, _) = Footer(canEdit: true, status: status);

        var line = window.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.Equal(status, line.Text);
    }

    /// <summary>
    /// The actions survive the narrowest window the app allows, next to its longest message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The status column takes what it needs and the actions take the rest, so a long message is the
    /// thing that can squeeze them. The longest is the one that appears when the driver's settings
    /// change underneath the artist — which is also the moment Reload has to be clickable, so a message
    /// that pushed it off the edge would take the way out with it.
    /// </para>
    /// <para>
    /// 800 is <c>MainWindow.MinWidth</c>. Below that the window cannot go, so this is the worst case
    /// rather than an arbitrary narrow one.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AtTheNarrowestWindow_TheLongestMessageDoesNotSqueezeOutTheActions()
    {
        const string longest = "Driver settings changed. Reload to continue. Attaching a new tablet "
            + "can cause this, as can another settings app.";

        var (window, _, buttons) = Footer(canEdit: false, status: longest, width: 800);

        foreach (var button in buttons)
        {
            Assert.True(button.Bounds.Width > 0, $"'{button.Content}' was squeezed to nothing");
            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window);
            Assert.True(right is { } p && p.X <= window.Width,
                $"'{button.Content}' runs past the window edge at 800px");
        }
    }
}
