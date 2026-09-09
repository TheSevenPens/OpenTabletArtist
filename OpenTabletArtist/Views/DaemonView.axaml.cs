using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DaemonView : UserControl
{
    public DaemonView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not DaemonViewModel vm) return;
        // On-demand: re-check the OTD system-package state each time the Daemon tab is shown (fast, off the
        // UI thread), and start polling the daemon-process state (running/uptime) while the card is visible.
        if (vm.CheckOtdPackageCommand.CanExecute(null)) vm.CheckOtdPackageCommand.Execute(null);
        vm.Process.StartPolling();
    }

    /// <summary>"Locate OpenTabletDriver…" — the native picker, in code-behind because it needs the
    /// window's StorageProvider (same split as the theme/config pickers).
    ///
    /// One picker, deliberately. An earlier version fell back to a folder picker when no file came back,
    /// which made Cancel reopen a second dialog — a cancelled picker and an empty result are the same
    /// value, so there is no fallback that doesn't trap the user. A file picker covers both real cases:
    /// macOS shows OpenTabletDriver.app as a selectable item, and elsewhere the daemon binary itself can
    /// be picked out of its folder. No type filter — the daemon has no extension on macOS/Linux, and a
    /// filter would hide the very file being looked for.</summary>
    private async void OnLocateDaemon(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DaemonViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose OpenTabletDriver",
            AllowMultiple = false,
        });

        // Cancelled — leave everything exactly as it was.
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && !string.IsNullOrWhiteSpace(path))
            await vm.ChooseDaemonPathAsync(path);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DaemonViewModel vm) vm.Process.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
