using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class PresetsView : UserControl
{
    public PresetsView()
    {
        InitializeComponent();
    }

    // The row's overflow menu (#preset-overflow). These are Click handlers rather than Command bindings
    // because a flyout's contents are hosted in a popup, outside the row's visual tree — so the
    // `$parent[ItemsControl].DataContext.<X>Command` binding the visible buttons use cannot resolve from
    // inside the menu. LogView's COPY menu is the same shape. The commands themselves still live on the
    // view model; only the dispatch is here.
    private void OnUpdatePreset(object? sender, RoutedEventArgs e) => Invoke(sender, vm => vm.UpdatePresetCommand);
    private void OnDuplicatePreset(object? sender, RoutedEventArgs e) => Invoke(sender, vm => vm.DuplicatePresetCommand);
    private void OnRenamePreset(object? sender, RoutedEventArgs e) => Invoke(sender, vm => vm.RenamePresetCommand);
    private void OnDeletePreset(object? sender, RoutedEventArgs e) => Invoke(sender, vm => vm.DeletePresetCommand);

    /// <summary>Runs <paramref name="pick"/>'s command for the preset the clicked menu item belongs to.
    /// The item inherits its row's <see cref="PresetInfo"/> as DataContext, which is what names the preset;
    /// the view's own DataContext is the page view model that owns the commands.</summary>
    private void Invoke(object? sender, Func<PresetsViewModel, ICommand> pick)
    {
        if (sender is not MenuItem { DataContext: PresetInfo preset }) return;
        if (DataContext is not PresetsViewModel vm) return;

        var command = pick(vm);
        if (command.CanExecute(preset.Name)) command.Execute(preset.Name);
    }
}
