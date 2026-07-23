using Avalonia;
using Avalonia.Controls;
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DaemonViewModel vm) vm.Process.StopPolling();
        base.OnDetachedFromVisualTree(e);
    }
}
