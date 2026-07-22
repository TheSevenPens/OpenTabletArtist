using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace OpenTabletArtist.Views;

/// <summary>The right-hand toolbar cluster shared by the Tablet + Pen detail views (#580): the tablet
/// switcher dropdown, the detection status chip, and Refresh. The switcher's list/selection come from the
/// hosting page VM (an ancestor), while detection + refresh come from the per-tablet detail VM, so both
/// sets are exposed as properties and each host wires them from the right DataContext.</summary>
public partial class TabletSwitcherBar : UserControl
{
    public static readonly StyledProperty<IEnumerable?> TabletsProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, IEnumerable?>(nameof(Tablets));

    public static readonly StyledProperty<object?> SelectedTabletProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, object?>(
            nameof(SelectedTablet), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, ICommand?>(nameof(RefreshCommand));

    public static readonly StyledProperty<bool> ShowRefreshProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, bool>(nameof(ShowRefresh), defaultValue: true);

    /// <summary>The connected + remembered tablets to switch between (from the page VM).</summary>
    public IEnumerable? Tablets { get => GetValue(TabletsProperty); set => SetValue(TabletsProperty, value); }
    /// <summary>The selected tablet (two-way, back to the page VM).</summary>
    public object? SelectedTablet { get => GetValue(SelectedTabletProperty); set => SetValue(SelectedTabletProperty, value); }
    /// <summary>Reload-from-daemon command (from the detail VM).</summary>
    public ICommand? RefreshCommand { get => GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
    /// <summary>Show the Refresh button. False = just the switcher dropdown (e.g. Scribble, which only needs
    /// to pick which tablet the Dynamics shortcut and Driver-mode mapping target).</summary>
    public bool ShowRefresh { get => GetValue(ShowRefreshProperty); set => SetValue(ShowRefreshProperty, value); }

    public TabletSwitcherBar() => InitializeComponent();
}
