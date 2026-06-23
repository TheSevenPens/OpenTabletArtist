using Avalonia.Controls;
using OtdWindowsHelper.ViewModels;

namespace OtdWindowsHelper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }
}
