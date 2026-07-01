using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using OpenTabletArtist.Domain;
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
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                vm.CancelDialogCommand.Execute(null);
                e.Handled = true;
            }
        };
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
    public static async Task<AuxBinding?> ShowAsync(Window owner, AuxBinding initial, string title)
    {
        var vm = new BindingEditorViewModel(initial, title);
        var dialog = new BindingEditorDialog(vm);
        await dialog.ShowDialog(owner);
        return await dialog._resultTcs.Task;
    }
}
