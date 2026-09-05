using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>Developer-only live editor for the code-generated Sakura backdrop's glows (#556). Editing a glow
/// rebuilds the edge glow brushes immediately (so the background updates live while the Sakura "CodeGen"
/// background is selected), persists the settings, and refreshes the copyable JSON in <see cref="SettingsText"/>.
///
/// The page is a list beside an inspector (#glow-linear): every glow shows in the list with its colour and
/// what it is, and the one you pick is the only one whose controls are on screen. That is what makes room
/// for a live preview of the selected glow, which the old grid of cards had nowhere to put.</summary>
public sealed partial class GradientEditorViewModel : ObservableObject
{
    public ObservableCollection<GradientGlowItem> Glows { get; } = new();

    /// <summary>The glow the inspector edits. Null only while the list is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GradientGlowItem? _selected;

    public bool HasSelection => Selected is not null;

    public bool HasGlows => Glows.Count > 0;

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
        Selected = Glows.FirstOrDefault();
        RefreshText(); // don't apply on load — the theme already applied the saved background
    }

    private void Attach(GradientGlowItem item, int? at = null)
    {
        item.PropertyChanged += (_, _) => ApplyLive(persist: PersistMode.Debounced);
        if (at is { } i) Glows.Insert(i, item); else Glows.Add(item);
        OnPropertyChanged(nameof(HasGlows));
    }

    [RelayCommand]
    private void AddGlow()
    {
        var item = new GradientGlowItem(new GradientGlow());
        Attach(item);
        Selected = item;
        ApplyLive(PersistMode.Immediate);
    }

    /// <summary>Remove <paramref name="item"/>, leaving the selection on its neighbour rather than nowhere.</summary>
    [RelayCommand]
    private void RemoveGlow(GradientGlowItem item)
    {
        var next = Glows.IndexOf(item);
        Glows.Remove(item);
        OnPropertyChanged(nameof(HasGlows));
        Selected = Glows.ElementAtOrDefault(Math.Min(next, Glows.Count - 1));
        ApplyLive(PersistMode.Immediate);
    }

    /// <summary>Insert a copy of <paramref name="item"/> right after it, so tweaks start from an existing glow.</summary>
    [RelayCommand]
    private void DuplicateGlow(GradientGlowItem item)
    {
        var copy = new GradientGlowItem(item.ToModel());
        Attach(copy, Glows.IndexOf(item) + 1);
        Selected = copy;
        ApplyLive(PersistMode.Immediate);
    }

    /// <summary>Discard the edited glows and restore the baked-in defaults (<see cref="GradientBackground.Defaults"/>).</summary>
    [RelayCommand]
    private void ResetGlows()
    {
        Glows.Clear();
        foreach (var g in GradientBackground.Defaults()) Attach(new GradientGlowItem(g));
        Selected = Glows.FirstOrDefault();
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
            GradientBackground.ApplyGlowBrushes(app.Resources, list);

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
    [ObservableProperty] private double _reachPx;
    [ObservableProperty] private string _color;
    [ObservableProperty] private double _centerOpacity;
    [ObservableProperty] private double _falloff;
    [ObservableProperty] private GlowStyle _style;
    [ObservableProperty] private GlowEdge _edge;

    public GradientGlowItem(GradientGlow g)
    {
        _centerX = g.CenterX;
        _width = g.Width;
        _reachPx = g.ReachPx;
        _color = g.Color;
        _centerOpacity = g.CenterOpacity;
        _falloff = g.Falloff;
        _style = g.Style;
        _edge = g.Edge;
    }

    public IReadOnlyList<GlowStyle> StyleOptions { get; } = new[] { GlowStyle.Radial, GlowStyle.Linear };

    /// <summary>The edges this glow may be anchored to — all four for a linear wash, bottom and top for a
    /// radial blob.</summary>
    public IReadOnlyList<GlowEdge> EdgeOptions => GradientBackground.EdgesFor(Style);

    public bool IsRadial => Style == GlowStyle.Radial;
    public bool IsLinear => Style == GlowStyle.Linear;

    /// <summary>What this glow is, for the list row: "radial · bottom".</summary>
    public string Summary => $"{Style} · {Edge}".ToLowerInvariant();

    /// <summary>The list chip. Neither to scale nor at the glow's real opacity: at 18px the true reach
    /// fraction is a couple of pixels, and a faint glow over a pink base is two shades of the same nothing.
    /// It names a colour and an edge, which is all a row needs to be told apart. The inspector's strip is
    /// the one that measures.</summary>
    public IBrush ChipBrush
    {
        get
        {
            var chip = ToModel();
            chip.CenterOpacity = 1;
            return GradientBackground.BuildPreviewBrush(chip, GradientBackground.LoadBaseColor(), 0.8);
        }
    }

    /// <summary>The inspector's preview strip, to scale against the glow band.</summary>
    public IBrush PreviewBrush =>
        GradientBackground.BuildPreviewBrush(ToModel(), GradientBackground.LoadBaseColor(), ReachPx / GradientBackground.BandHeight);

    // Switching to radial off a side edge would leave a glow the editor can no longer express (and that the
    // side bands were not added for), so the anchor falls back to the bottom.
    partial void OnStyleChanged(GlowStyle value)
    {
        if (value == GlowStyle.Radial && Edge is GlowEdge.Left or GlowEdge.Right) Edge = GlowEdge.Bottom;
        OnPropertyChanged(nameof(EdgeOptions));
    }

    // Every field feeds the two previews and the list summary, so recompute them on any change but their own
    // (the guard is what stops this recursing).
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ChipBrush) or nameof(PreviewBrush) or nameof(Summary)
            or nameof(EdgeOptions) or nameof(IsRadial) or nameof(IsLinear)) return;

        OnPropertyChanged(nameof(ChipBrush));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsRadial));
        OnPropertyChanged(nameof(IsLinear));
    }

    public GradientGlow ToModel() => new()
    {
        CenterX = CenterX,
        Width = Width,
        ReachPx = ReachPx,
        Color = Color,
        CenterOpacity = CenterOpacity,
        Falloff = Falloff,
        Style = Style,
        Edge = Edge,
    };
}
