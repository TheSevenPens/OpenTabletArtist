using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Modal editor for a single express-key / wheel binding, with a tab per type (Keyboard / Mouse /
/// Scroll). Returns the chosen binding on Save, <see cref="AuxBinding.Unbound"/> on Clear, or null on
/// Cancel. Opened via <see cref="ShowAsync"/>.
/// </summary>
public partial class BindingEditorDialog : Window
{
    private readonly TaskCompletionSource<AuxBinding?> _resultTcs = new();

    public BindingEditorDialog() => InitializeComponent();   // designer

    public BindingEditorDialog(BindingEditorViewModel vm)
    {
        InitializeComponent();
        ShellPenFeedback.DisableOnOpen(this);
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;
        // While recording, every physical key belongs to the recorder. Tunnel stage, so a focused button
        // never sees Space or Enter, and the keys are read before anything else can act on them.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                vm.CancelDialogCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BindingEditorViewModel { IsRecording: true } vm) return;
        if (e.Key == Key.Escape)
        {
            vm.CancelRecording();   // Escape abandons the recording, not the dialog
            e.Handled = true;
            return;
        }
        e.Handled = vm.CaptureKeyDown(e.Key);
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not BindingEditorViewModel { IsRecording: true } vm) return;
        e.Handled = vm.CaptureKeyUp(e.Key);
    }

    private void OnCloseRequested()
    {
        if (DataContext is BindingEditorViewModel vm)
            _resultTcs.TrySetResult(vm.Result);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Complete the awaiter even when the dialog is closed via the title-bar X / Alt+F4, which
        // bypass the VM's CloseRequested path. Without this, ShowAsync's `await _resultTcs.Task` would
        // never return. TrySetResult is a no-op if the normal Save/Cancel path already set it.
        _resultTcs.TrySetResult((DataContext as BindingEditorViewModel)?.Result);
    }

    /// <summary>Open the editor modally over <paramref name="owner"/> and return the result.</summary>
    public static async Task<AuxBinding?> ShowAsync(Window owner, AuxBinding initial, string title,
        BindingEditorOptions? options = null)
    {
        var vm = new BindingEditorViewModel(initial, title, options);
        var dialog = new BindingEditorDialog(vm);
        await dialog.ShowDialog(owner);
        return await dialog._resultTcs.Task;
    }
}
