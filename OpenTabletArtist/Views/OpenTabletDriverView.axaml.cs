using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Hosts the OpenTabletDriver tabbed page's subpage navigation (tab rail). Lazily mounts each subpage
/// on first selection (#388). Reacts to <see cref="OpenTabletDriverViewModel.SelectedTab"/> for deep-links.
/// </summary>
public partial class OpenTabletDriverView : UserControl
{
    private OpenTabletDriverViewModel? _vm;
    private readonly Dictionary<OtdTab, object> _mounted = new();
    private bool _syncingTab;

    public OpenTabletDriverView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as OpenTabletDriverViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        WireTabHandlers();
        ShowTab(_vm?.SelectedTab ?? OtdTab.Daemon);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnwireTabHandlers();
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _ = _vm?.Diagnostics.StopDebuggingAsync();
        _vm = null;
        _mounted.Clear();
    }

    private void WireTabHandlers()
    {
        DaemonTab.IsCheckedChanged += OnDaemonTabChecked;
        WinInkTab.IsCheckedChanged += OnWinInkTabChecked;
        ConfigsTab.IsCheckedChanged += OnConfigsTabChecked;
        DiagnosticsTab.IsCheckedChanged += OnDiagnosticsTabChanged;
        LogTab.IsCheckedChanged += OnLogTabChecked;
        PluginsTab.IsCheckedChanged += OnPluginsTabChecked;
    }

    private void UnwireTabHandlers()
    {
        DaemonTab.IsCheckedChanged -= OnDaemonTabChecked;
        WinInkTab.IsCheckedChanged -= OnWinInkTabChecked;
        ConfigsTab.IsCheckedChanged -= OnConfigsTabChecked;
        DiagnosticsTab.IsCheckedChanged -= OnDiagnosticsTabChanged;
        LogTab.IsCheckedChanged -= OnLogTabChecked;
        PluginsTab.IsCheckedChanged -= OnPluginsTabChecked;
    }

    private void OnDaemonTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || DaemonTab.IsChecked != true) return;
        ShowTab(OtdTab.Daemon);
    }

    private void OnWinInkTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || WinInkTab.IsChecked != true) return;
        ShowTab(OtdTab.WindowsInk);
    }

    private void OnConfigsTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || ConfigsTab.IsChecked != true) return;
        ShowTab(OtdTab.CustomTabletConfigs);
    }

    private void OnLogTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || LogTab.IsChecked != true) return;
        ShowTab(OtdTab.Log);
    }

    private void OnPluginsTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || PluginsTab.IsChecked != true) return;
        ShowTab(OtdTab.Plugins);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpenTabletDriverViewModel.SelectedTab))
            ShowTab(_vm?.SelectedTab ?? OtdTab.Daemon);
    }

    private void OnDiagnosticsTabChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab) return;
        if (DiagnosticsTab.IsChecked == true)
            ShowTab(OtdTab.Diagnostics);
        else
            _ = _vm?.Diagnostics.StopDebuggingAsync();
    }

    private void ShowTab(OtdTab tab)
    {
        if (_vm == null) return;
        if (!_mounted.TryGetValue(tab, out var content))
        {
            content = tab switch
            {
                OtdTab.WindowsInk => _vm.WindowsInk,
                OtdTab.CustomTabletConfigs => _vm.Configs,
                OtdTab.Diagnostics => _vm.Diagnostics,
                OtdTab.Log => _vm.Log,
                OtdTab.Plugins => _vm.Plugins,
                _ => _vm.Daemon,
            };
            _mounted[tab] = content;
        }
        TabHost.Content = content;
        // Complex header shows the breadcrumb "OPENTABLETDRIVER › <subpage>" — the subpage name comes
        // from the tab's own label, so it stays in sync with the rail.
        Header.Title = $"OPENTABLETDRIVER › {RadioFor(tab).Content}";
        if (_vm.SelectedTab != tab) _vm.SelectedTab = tab;
        _syncingTab = true;
        SelectTabRadio(tab);
        _syncingTab = false;
    }

    private void SelectTabRadio(OtdTab tab) => RadioFor(tab).IsChecked = true;

    private RadioButton RadioFor(OtdTab tab) => tab switch
    {
        OtdTab.WindowsInk => WinInkTab,
        OtdTab.CustomTabletConfigs => ConfigsTab,
        OtdTab.Diagnostics => DiagnosticsTab,
        OtdTab.Log => LogTab,
        OtdTab.Plugins => PluginsTab,
        _ => DaemonTab,
    };
}
