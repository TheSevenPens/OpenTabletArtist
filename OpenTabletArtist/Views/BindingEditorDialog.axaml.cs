using System.Threading.Tasks;
using Avalonia.Controls;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

/// <summary>
/// Modal editor for a single express-key / wheel binding, with a tab per type (Keyboard / Mouse /
/// Scroll). Returns the chosen binding on Save, <see cref="AuxBinding.Unbound"/> on Clear, or null on
/// Cancel. Opened via <see cref="ShowAsync"/>.
/// </summary>
public partial class BindingEditorDialog : Window
{
    public BindingEditorDialog() => InitializeComponent();   // designer

    public BindingEditorDialog(BindingEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }

    /// <summary>Open the editor modally over <paramref name="owner"/> and return the result.</summary>
    public static async Task<AuxBinding?> ShowAsync(Window? owner, AuxBinding initial, string title)
    {
        var vm = new BindingEditorViewModel(initial, title);
        var dialog = new BindingEditorDialog(vm);
        if (owner != null) await dialog.ShowDialog(owner);
        else dialog.Show();
        return vm.Result;
    }
}
