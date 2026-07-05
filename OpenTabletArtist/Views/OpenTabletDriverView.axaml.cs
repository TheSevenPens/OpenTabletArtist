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
/// Hosts the OpenTabletDriver hub's secondary tab rail. Lazily mounts each engine page on first
/// selection (#388). Reacts to <see cref="OpenTabletDriverViewModel.SelectedTab"/> for deep-links.
/// </summary>
public partial class OpenTabletDriverView : UserControl
{
    private OpenTabletDriverViewModel? _vm;
    private readonly Dictionary<OtdHubTab, object> _mounted = new();
    private bool _syncingTab;

    public OpenTabletDriverView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as OpenTabletDriverViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        WireTabHandlers();
        ShowTab(_vm?.SelectedTab ?? OtdHubTab.Daemon);
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
        ShowTab(OtdHubTab.Daemon);
    }

    private void OnWinInkTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || WinInkTab.IsChecked != true) return;
        ShowTab(OtdHubTab.WindowsInk);
    }

    private void OnConfigsTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || ConfigsTab.IsChecked != true) return;
        ShowTab(OtdHubTab.CustomTabletConfigs);
    }

    private void OnLogTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || LogTab.IsChecked != true) return;
        ShowTab(OtdHubTab.Log);
    }

    private void OnPluginsTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || PluginsTab.IsChecked != true) return;
        ShowTab(OtdHubTab.Plugins);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OpenTabletDriverViewModel.SelectedTab))
            ShowTab(_vm?.SelectedTab ?? OtdHubTab.Daemon);
    }

    private void OnDiagnosticsTabChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab) return;
        if (DiagnosticsTab.IsChecked == true)
            ShowTab(OtdHubTab.Diagnostics);
        else
            _ = _vm?.Diagnostics.StopDebuggingAsync();
    }

    private void ShowTab(OtdHubTab tab)
    {
        if (_vm == null) return;
        if (!_mounted.TryGetValue(tab, out var content))
        {
            content = tab switch
            {
                OtdHubTab.WindowsInk => _vm.WindowsInk,
                OtdHubTab.CustomTabletConfigs => _vm.Configs,
                OtdHubTab.Diagnostics => _vm.Diagnostics,
                OtdHubTab.Log => _vm.Log,
                OtdHubTab.Plugins => _vm.Plugins,
                _ => _vm.Daemon,
            };
            _mounted[tab] = content;
        }
        TabHost.Content = content;
        if (_vm.SelectedTab != tab) _vm.SelectedTab = tab;
        _syncingTab = true;
        SelectTabRadio(tab);
        _syncingTab = false;
    }

    private void SelectTabRadio(OtdHubTab tab)
    {
        var radio = tab switch
        {
            OtdHubTab.WindowsInk => WinInkTab,
            OtdHubTab.CustomTabletConfigs => ConfigsTab,
            OtdHubTab.Diagnostics => DiagnosticsTab,
            OtdHubTab.Log => LogTab,
            OtdHubTab.Plugins => PluginsTab,
            _ => DaemonTab,
        };
        radio.IsChecked = true;
    }
}
