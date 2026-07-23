using Avalonia.Controls;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class DaemonView : UserControl
{
    public DaemonView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // On-demand: re-check the OTD system-package state each time the Daemon tab is shown (fast, off the
        // UI thread). Fires on first show and every re-entry, so it reflects an install/removal live.
        if (DataContext is DaemonViewModel vm && vm.CheckOtdPackageCommand.CanExecute(null))
            vm.CheckOtdPackageCommand.Execute(null);
    }
}
