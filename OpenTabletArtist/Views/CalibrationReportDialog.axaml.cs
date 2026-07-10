using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Read-only viewer for the last calibration's recorded points (fit summary, natural tilt, per-tap
/// table). Bound to the <see cref="TabletDetailViewModel"/> it's opened over. Moved off the Calibration
/// tab into this dialog so the tab stays focused on running a calibration (#500/#501). Opened via
/// <see cref="ShowAsync"/>.
/// </summary>
public partial class CalibrationReportDialog : AppWindow
{
    public CalibrationReportDialog()
    {
        InitializeComponent();
        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
            closeButton.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Open the report modally over <paramref name="owner"/>, bound to <paramref name="vm"/>.</summary>
    public static Task ShowAsync(Window owner, TabletDetailViewModel vm)
    {
        var dialog = new CalibrationReportDialog { DataContext = vm };
        return dialog.ShowDialog(owner);
    }
}
