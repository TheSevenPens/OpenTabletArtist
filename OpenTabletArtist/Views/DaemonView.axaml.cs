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
    /// window's StorageProvider (same split as the theme/config pickers). macOS shows an .app bundle as a
    /// file, Linux/Windows as a folder of binaries, so both pickers are offered and whichever the user
    /// completes is handed to the view model to vet.</summary>
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

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            // Nothing picked as a file — offer the folder picker for a plain install directory.
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the folder holding OpenTabletDriver",
                AllowMultiple = false,
            });
            path = folders.FirstOrDefault()?.TryGetLocalPath();
        }

        if (!string.IsNullOrWhiteSpace(path)) await vm.ChooseDaemonPathAsync(path);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DaemonViewModel vm) vm.Process.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
