using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class ThemeView : UserControl
{
    public ThemeView()
    {
        InitializeComponent();
        // Arm base-colour persistence only once the view (and its ColorPicker) has loaded, and disarm on
        // unload — this defeats the ColorPicker's load-time two-way echo silently overwriting the colour.
        Loaded += (_, _) => (DataContext as ThemeViewModel)?.ArmBaseColorPersistence();
        Unloaded += (_, _) => (DataContext as ThemeViewModel)?.DisarmBaseColorPersistence();
    }

    /// <summary>Opens the native file picker for a background image and hands the path to the view model
    /// (which persists it and applies the Custom backdrop live). Done in code-behind so the VM stays free
    /// of window/StorageProvider references.</summary>
    private async void OnChooseImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ThemeViewModel vm) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a background image",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.BackgroundImagePath = path;
    }
}
