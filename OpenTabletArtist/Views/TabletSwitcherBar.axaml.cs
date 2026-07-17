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

    public static readonly StyledProperty<bool> IsDetectedProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, bool>(nameof(IsDetected));

    public static readonly StyledProperty<string?> DetectionTextProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, string?>(nameof(DetectionText));

    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<TabletSwitcherBar, ICommand?>(nameof(RefreshCommand));

    /// <summary>The connected + remembered tablets to switch between (from the page VM).</summary>
    public IEnumerable? Tablets { get => GetValue(TabletsProperty); set => SetValue(TabletsProperty, value); }
    /// <summary>The selected tablet (two-way, back to the page VM).</summary>
    public object? SelectedTablet { get => GetValue(SelectedTabletProperty); set => SetValue(SelectedTabletProperty, value); }
    /// <summary>Whether the current tablet is detected — drives the status chip (from the detail VM).</summary>
    public bool IsDetected { get => GetValue(IsDetectedProperty); set => SetValue(IsDetectedProperty, value); }
    /// <summary>The status chip's tooltip text (from the detail VM).</summary>
    public string? DetectionText { get => GetValue(DetectionTextProperty); set => SetValue(DetectionTextProperty, value); }
    /// <summary>Reload-from-daemon command (from the detail VM).</summary>
    public ICommand? RefreshCommand { get => GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }

    public TabletSwitcherBar() => InitializeComponent();
}
