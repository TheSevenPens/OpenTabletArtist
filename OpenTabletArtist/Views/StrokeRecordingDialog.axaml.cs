using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenTabletArtist.Helpers;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// The review-and-save window for a recording that has just been stopped. It is the only place a finished recording
/// is shown: the Scribble page goes back to normal the moment it is stopped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closing it without saving asks first.</b> A recording is a few seconds of drawing that cannot be redone, and
/// the window can be closed by a click on its edge, Esc, or the Discard button, none of which says "I mean it". All
/// three take the same path and ask once. Closing after it has been saved, or because the application is shutting
/// down, does not ask.
/// </para>
/// </remarks>
public partial class StrokeRecordingDialog : Window
{
    private readonly StrokeRecordingViewModel? _vm;
    private readonly Func<Task<bool>>? _confirmDiscard;
    private bool _agreedToDiscard;
    private bool _asking;

    public StrokeRecordingDialog()
    {
        InitializeComponent();
        ShellPenFeedback.DisableOnOpen(this);
    }

    private StrokeRecordingDialog(StrokeRecordingViewModel vm, Func<Task<bool>>? confirmDiscard) : this()
    {
        _vm = vm;
        DataContext = vm;
        _confirmDiscard = confirmDiscard ?? (() => Dialogs.ShowConfirmAsync(
            "Discard this recording?",
            "It has not been saved, and it cannot be recovered or recorded again.",
            this));

        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    private void OnDiscard(object? sender, RoutedEventArgs e) => Close();

    private void OnDone(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only a person closing the window is asked. The application shutting down, or the window it belongs to
        // closing, is not something to refuse.
        if (_vm is not { IsReview: true } || _agreedToDiscard || e.CloseReason != WindowCloseReason.WindowClosing) return;

        e.Cancel = true;
        if (_asking) return;

        _asking = true;
        _ = AskThenDiscardAsync();
    }

    private async Task AskThenDiscardAsync()
    {
        try
        {
            if (!await _confirmDiscard!()) return;

            _agreedToDiscard = true;
            _vm!.DiscardCommand.Execute(null);
            Close();
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>Opens the window modally over <paramref name="owner"/>; completes when it is closed.</summary>
    /// <param name="confirmDiscard">Asks whether to throw the recording away. Tests supply their own.</param>
    public static async Task ShowAsync(Window owner, StrokeRecordingViewModel recording, Func<Task<bool>>? confirmDiscard = null)
    {
        var dialog = new StrokeRecordingDialog(recording, confirmDiscard);
        await dialog.ShowDialog(owner);
    }
}
