using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Hosts the OpenTabletDriver hub's secondary tab rail. Reacts to <see cref="OpenTabletDriverViewModel.SelectedTab"/>
/// so callers can deep-link to a tab (e.g. a health "Fix" opening Windows Ink), and preserves the
/// Diagnostics lifecycle the shell used to own: the debug stream is stopped when the Diagnostics tab is
/// left or the hub is closed (its Start is user-driven inside the Diagnostics page).
/// </summary>
public partial class OpenTabletDriverView : UserControl
{
    private OpenTabletDriverViewModel? _vm;

    public OpenTabletDriverView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as OpenTabletDriverViewModel;
        SelectTab(_vm?.SelectedTab ?? 0); // honor a deep-link target
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        DiagnosticsTab.IsCheckedChanged += OnDiagnosticsTabChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DiagnosticsTab.IsCheckedChanged -= OnDiagnosticsTabChanged;
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _ = _vm?.Diagnostics.StopDebuggingAsync(); // leaving the hub → stop the debug stream
        _vm = null;
    }

    // Deep-link while the hub is already open (e.g. a second health Fix).
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpenTabletDriverViewModel.SelectedTab)) SelectTab(_vm?.SelectedTab ?? 0);
    }

    // Stop the debug stream when the Diagnostics tab is left (its Start is user-driven).
    private void OnDiagnosticsTabChanged(object? sender, RoutedEventArgs e)
    {
        if (DiagnosticsTab.IsChecked != true) _ = _vm?.Diagnostics.StopDebuggingAsync();
    }

    private void SelectTab(int index)
    {
        var tab = index switch
        {
            1 => WinInkTab,
            2 => ConfigsTab,
            3 => DiagnosticsTab,
            4 => LogTab,
            5 => PluginsTab,
            _ => DaemonTab,
        };
        tab.IsChecked = true;
    }
}
