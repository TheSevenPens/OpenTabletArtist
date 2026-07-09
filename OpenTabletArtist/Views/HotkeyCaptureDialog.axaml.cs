using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using OpenTabletArtist.Services;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// On-screen hotkey picker (#320): shows the keys a global hotkey can use and lets the user click a
/// chord or just press it on the physical keyboard. Returns the chosen <see cref="HotkeyChord"/> on Save,
/// or null on Cancel / Escape / close. Opened via <see cref="ShowAsync"/>.
/// </summary>
public partial class HotkeyCaptureDialog : Window
{
    private readonly TaskCompletionSource<HotkeyChord?> _resultTcs = new();

    public HotkeyCaptureDialog() => InitializeComponent();   // designer

    public HotkeyCaptureDialog(HotkeyCaptureViewModel vm)
    {
        InitializeComponent();
        ShellPenFeedback.DisableOnOpen(this);
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;

        // Physical keyboard: pressing the real shortcut fills the board in. Handle at the tunnel
        // (preview) stage so modifier+key chords are read before a focused button consumes them, and
        // so Enter/Space on a keycap doesn't get hijacked. Escape cancels.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HotkeyCaptureViewModel vm) return;
        if (e.Key == Key.Escape)
        {
            vm.CancelDialogCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // A physical chord (a non-modifier key with at least one modifier held) sets the board.
        if (e.KeyModifiers != KeyModifiers.None)
        {
            vm.CapturePhysical(e.KeyModifiers, e.Key);
            e.Handled = true;
        }
    }

    private void OnCloseRequested()
    {
        if (DataContext is HotkeyCaptureViewModel vm)
            _resultTcs.TrySetResult(vm.Result);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Complete the awaiter even when closed via the title-bar X / Alt+F4 (which bypass the VM path).
        _resultTcs.TrySetResult((DataContext as HotkeyCaptureViewModel)?.Result);
    }

    /// <summary>Open the picker modally over <paramref name="owner"/> and return the chosen chord.</summary>
    public static async Task<HotkeyChord?> ShowAsync(Window owner, HotkeyChord? initial = null)
    {
        var vm = new HotkeyCaptureViewModel(initial);
        var dialog = new HotkeyCaptureDialog(vm);
        await dialog.ShowDialog(owner);
        return await dialog._resultTcs.Task;
    }
}
