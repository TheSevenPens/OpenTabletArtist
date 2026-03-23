using System.Windows;
using TabletDriverUX.ViewModels;

namespace TabletDriverUX;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }
}
