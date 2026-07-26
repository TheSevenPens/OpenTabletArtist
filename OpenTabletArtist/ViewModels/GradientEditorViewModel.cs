using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>Developer-only live editor for the code-generated Sakura backdrop's glows (#556). Editing a glow
/// rebuilds AppBackdropGlowBrush immediately (so the background updates live while the Sakura "CodeGen"
/// background is selected), persists the settings, and refreshes the copyable JSON in <see cref="SettingsText"/>.</summary>
public sealed partial class GradientEditorViewModel : ObservableObject
{
    public ObservableCollection<GradientGlowItem> Glows { get; } = new();

    /// <summary>Pretty-printed JSON of the current glows — copy this and paste it to bake in as the default.</summary>
    [ObservableProperty] private string _settingsText = "";

    public GradientEditorViewModel()
    {
        foreach (var g in GradientBackground.Load()) Attach(new GradientGlowItem(g));
        RefreshText(); // don't apply on load — the theme already applied the saved glows
    }

    private void Attach(GradientGlowItem item)
    {
        item.PropertyChanged += (_, _) => Apply();
        Glows.Add(item);
    }

    [RelayCommand]
    private void AddGlow() { Attach(new GradientGlowItem(new GradientGlow())); Apply(); }

    [RelayCommand]
    private void RemoveGlow(GradientGlowItem item) { Glows.Remove(item); Apply(); }

    private void Apply()
    {
        var list = Glows.Select(i => i.ToModel()).ToList();
        GradientBackground.Save(list);
        SettingsText = GradientBackground.Serialize(list);
        if (Application.Current is { } app)
        {
            app.Resources["AppBackdropGlowBrush"] = GradientBackground.BuildGlowBrush(list, top: false);
            app.Resources["AppBackdropGlowTopBrush"] = GradientBackground.BuildGlowBrush(list, top: true);
        }
    }

    private void RefreshText() =>
        SettingsText = GradientBackground.Serialize(Glows.Select(i => i.ToModel()));
}

/// <summary>Editable view of one <see cref="GradientGlow"/>; any change re-applies the backdrop.</summary>
public sealed partial class GradientGlowItem : ObservableObject
{
    [ObservableProperty] private double _centerX;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _heightPx;
    [ObservableProperty] private string _color;
    [ObservableProperty] private double _centerOpacity;
    [ObservableProperty] private bool _top;

    public GradientGlowItem(GradientGlow g)
    {
        _centerX = g.CenterX;
        _width = g.Width;
        _heightPx = g.HeightPx;
        _color = g.Color;
        _centerOpacity = g.CenterOpacity;
        _top = g.Top;
    }

    public GradientGlow ToModel() => new()
    {
        CenterX = CenterX,
        Width = Width,
        HeightPx = HeightPx,
        Color = Color,
        CenterOpacity = CenterOpacity,
        Top = Top,
    };
}
