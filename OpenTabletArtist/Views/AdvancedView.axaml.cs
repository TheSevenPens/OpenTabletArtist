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
/// Hosts the ADVANCED tabbed page's subpage navigation (tab rail). Lazily mounts each subpage on first
/// selection (#388). Reacts to <see cref="AdvancedViewModel.SelectedTab"/> for deep-links. Replaces the
/// old OpenTabletDriverView: its six OTD tabs are now the first group here, followed by OTA's own pages.
/// </summary>
public partial class AdvancedView : UserControl
{
    private AdvancedViewModel? _vm;
    private readonly Dictionary<AdvancedTab, object> _mounted = new();
    private bool _syncingTab;

    public AdvancedView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as AdvancedViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        WireTabHandlers();
        ShowTab(_vm?.SelectedTab ?? AdvancedTab.Daemon);
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

    // Every tab shares one handler except Diagnostics, which also stops the debug stream when left.
    private void WireTabHandlers()
    {
        DaemonTab.IsCheckedChanged += OnTabChecked;
        WinInkTab.IsCheckedChanged += OnTabChecked;
        ConfigsTab.IsCheckedChanged += OnTabChecked;
        DiagnosticsTab.IsCheckedChanged += OnDiagnosticsTabChanged;
        LogTab.IsCheckedChanged += OnTabChecked;
        PluginsTab.IsCheckedChanged += OnTabChecked;
        VMultiTab.IsCheckedChanged += OnTabChecked;
        DriverCleanupTab.IsCheckedChanged += OnTabChecked;
        StartupTab.IsCheckedChanged += OnTabChecked;
        DeveloperTab.IsCheckedChanged += OnTabChecked;
        ThemeTab.IsCheckedChanged += OnTabChecked;
    }

    private void UnwireTabHandlers()
    {
        DaemonTab.IsCheckedChanged -= OnTabChecked;
        WinInkTab.IsCheckedChanged -= OnTabChecked;
        ConfigsTab.IsCheckedChanged -= OnTabChecked;
        DiagnosticsTab.IsCheckedChanged -= OnDiagnosticsTabChanged;
        LogTab.IsCheckedChanged -= OnTabChecked;
        PluginsTab.IsCheckedChanged -= OnTabChecked;
        VMultiTab.IsCheckedChanged -= OnTabChecked;
        DriverCleanupTab.IsCheckedChanged -= OnTabChecked;
        StartupTab.IsCheckedChanged -= OnTabChecked;
        DeveloperTab.IsCheckedChanged -= OnTabChecked;
        ThemeTab.IsCheckedChanged -= OnTabChecked;
    }

    private void OnTabChecked(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab || sender is not RadioButton rb || rb.IsChecked != true) return;
        ShowTab(TabFor(rb));
    }

    private void OnDiagnosticsTabChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncingTab) return;
        if (DiagnosticsTab.IsChecked == true)
            ShowTab(AdvancedTab.Diagnostics);
        else
            _ = _vm?.Diagnostics.StopDebuggingAsync();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdvancedViewModel.SelectedTab))
            ShowTab(_vm?.SelectedTab ?? AdvancedTab.Daemon);
    }

    private void ShowTab(AdvancedTab tab)
    {
        if (_vm == null) return;
        if (!_mounted.TryGetValue(tab, out var content))
        {
            content = tab switch
            {
                AdvancedTab.WindowsInk => _vm.WindowsInk,
                AdvancedTab.CustomTabletConfigs => _vm.Configs,
                AdvancedTab.Diagnostics => _vm.Diagnostics,
                AdvancedTab.Log => _vm.Log,
                AdvancedTab.Plugins => _vm.Plugins,
                AdvancedTab.VMulti => _vm.VMulti,
                AdvancedTab.DriverCleanup => _vm.DriverCleanup,
                AdvancedTab.Startup => _vm.Startup,
                AdvancedTab.Developer => _vm.Developer,
                AdvancedTab.Theme => _vm.Theme,
                _ => _vm.Daemon,
            };
            _mounted[tab] = content;
        }
        TabHost.Content = content;
        // Complex header shows the breadcrumb "ADVANCED › <subpage>" — the subpage name comes from the
        // tab's own label, so it stays in sync with the rail.
        Header.Title = $"ADVANCED › {RadioFor(tab).Content}";
        if (_vm.SelectedTab != tab) _vm.SelectedTab = tab;
        _syncingTab = true;
        SelectTabRadio(tab);
        _syncingTab = false;
    }

    private void SelectTabRadio(AdvancedTab tab) => RadioFor(tab).IsChecked = true;

    private RadioButton RadioFor(AdvancedTab tab) => tab switch
    {
        AdvancedTab.WindowsInk => WinInkTab,
        AdvancedTab.CustomTabletConfigs => ConfigsTab,
        AdvancedTab.Diagnostics => DiagnosticsTab,
        AdvancedTab.Log => LogTab,
        AdvancedTab.Plugins => PluginsTab,
        AdvancedTab.VMulti => VMultiTab,
        AdvancedTab.DriverCleanup => DriverCleanupTab,
        AdvancedTab.Startup => StartupTab,
        AdvancedTab.Developer => DeveloperTab,
        AdvancedTab.Theme => ThemeTab,
        _ => DaemonTab,
    };

    private AdvancedTab TabFor(RadioButton rb)
    {
        if (rb == WinInkTab) return AdvancedTab.WindowsInk;
        if (rb == ConfigsTab) return AdvancedTab.CustomTabletConfigs;
        if (rb == DiagnosticsTab) return AdvancedTab.Diagnostics;
        if (rb == LogTab) return AdvancedTab.Log;
        if (rb == PluginsTab) return AdvancedTab.Plugins;
        if (rb == VMultiTab) return AdvancedTab.VMulti;
        if (rb == DriverCleanupTab) return AdvancedTab.DriverCleanup;
        if (rb == StartupTab) return AdvancedTab.Startup;
        if (rb == DeveloperTab) return AdvancedTab.Developer;
        if (rb == ThemeTab) return AdvancedTab.Theme;
        return AdvancedTab.Daemon;
    }
}
