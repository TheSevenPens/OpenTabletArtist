using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
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

    /// <summary>Pretty-printed JSON of the whole background (base colour + glows) — copy this and paste it
    /// to bake in as the default. The base colour is owned by the Appearance tab; the editor just reflects
    /// its current persisted value here.</summary>
    [ObservableProperty] private string _settingsText = "";

    // A slider drag fires PropertyChanged continuously; rebuilding the live brushes each tick is cheap, but
    // persisting is a synchronous settings-file write, so it's debounced to a short idle (#612). Structural
    // edits (add/remove/duplicate/reset) persist immediately so they can't be lost to the debounce window.
    private readonly DispatcherTimer _saveTimer;

    public GradientEditorViewModel()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += (_, _) => PersistNow();
        foreach (var g in GradientBackground.Load()) Attach(new GradientGlowItem(g));
        RefreshText(); // don't apply on load — the theme already applied the saved background
    }

    private void Attach(GradientGlowItem item)
    {
        item.PropertyChanged += (_, _) => ApplyLive(persist: PersistMode.Debounced);
        Glows.Add(item);
    }

    [RelayCommand]
    private void AddGlow() { Attach(new GradientGlowItem(new GradientGlow())); ApplyLive(PersistMode.Immediate); }

    [RelayCommand]
    private void RemoveGlow(GradientGlowItem item) { Glows.Remove(item); ApplyLive(PersistMode.Immediate); }

    /// <summary>Insert a copy of <paramref name="item"/> right after it, so tweaks start from an existing glow.</summary>
    [RelayCommand]
    private void DuplicateGlow(GradientGlowItem item)
    {
        var copy = new GradientGlowItem(item.ToModel());
        copy.PropertyChanged += (_, _) => ApplyLive(PersistMode.Debounced);
        Glows.Insert(Glows.IndexOf(item) + 1, copy);
        ApplyLive(PersistMode.Immediate);
    }

    /// <summary>Discard the edited glows and restore the baked-in defaults (<see cref="GradientBackground.Defaults"/>).</summary>
    [RelayCommand]
    private void ResetGlows()
    {
        Glows.Clear();
        foreach (var g in GradientBackground.Defaults()) Attach(new GradientGlowItem(g));
        ApplyLive(PersistMode.Immediate);
    }

    private enum PersistMode { Debounced, Immediate }

    // Updates the live backdrop + copy text every call (cheap), then either debounces or immediately runs the
    // settings-file write.
    private void ApplyLive(PersistMode persist)
    {
        var list = Glows.Select(i => i.ToModel()).ToList();
        SettingsText = GradientBackground.Serialize(GradientBackground.LoadBaseColor(), list);
        // Only touch the live backdrop when the codegen background is actually showing, so editing here
        // never overrides the image / solid Sakura backgrounds (or other skins).
        if (Application.Current is { } app && SkinColorSettings.SakuraBackground == "codegen")
        {
            app.Resources["AppBackdropGlowBrush"] = GradientBackground.BuildGlowBrush(list, top: false);
            app.Resources["AppBackdropGlowTopBrush"] = GradientBackground.BuildGlowBrush(list, top: true);
        }

        if (persist == PersistMode.Immediate) PersistNow();
        else { _saveTimer.Stop(); _saveTimer.Start(); } // restart the idle window
    }

    // Persist the current glows — but only if every colour parses, so a mid-typed / invalid hex (e.g. "#12")
    // is never written to settings (#606). The live preview above already falls back gracefully.
    private void PersistNow()
    {
        _saveTimer.Stop();
        var list = Glows.Select(i => i.ToModel()).ToList();
        if (list.All(g => Color.TryParse(g.Color, out _)))
            GradientBackground.Save(list);
    }

    private void RefreshText() =>
        SettingsText = GradientBackground.Serialize(GradientBackground.LoadBaseColor(), Glows.Select(i => i.ToModel()));
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
