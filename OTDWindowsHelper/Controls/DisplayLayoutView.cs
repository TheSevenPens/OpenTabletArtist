using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using OtdWindowsHelper.Domain;

namespace OtdWindowsHelper.Controls;

/// <summary>
/// A Windows-Display-Settings-style picker: draws the connected monitors as scaled, to-position,
/// numbered rectangles. Clicking one sets <see cref="SelectedNumber"/> (the caller applies it).
/// </summary>
public sealed class DisplayLayoutView : Control
{
    private static readonly IBrush SelFill = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
    private static readonly IBrush SelText = Brushes.White;
    private static readonly IBrush UnselFill = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xE3));
    private static readonly IBrush UnselText = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
    private static readonly IBrush SubText = new SolidColorBrush(Color.FromArgb(0xCC, 0x33, 0x33, 0x33));
    private static readonly IBrush SubTextOnSel = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
    private static readonly IPen UnselBorder = new Pen(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xCB)), 1);
    private static readonly IPen SelBorder = new Pen(new SolidColorBrush(Color.FromRgb(0x4B, 0x4E, 0xC9)), 1.5);
    private static readonly Typeface Face = new("Segoe UI");

    private readonly List<(DisplayInfo Display, Rect Box)> _hitRects = new();

    public static readonly StyledProperty<IReadOnlyList<DisplayInfo>?> DisplaysProperty =
        AvaloniaProperty.Register<DisplayLayoutView, IReadOnlyList<DisplayInfo>?>(nameof(Displays));

    public IReadOnlyList<DisplayInfo>? Displays
    {
        get => GetValue(DisplaysProperty);
        set => SetValue(DisplaysProperty, value);
    }

    public static readonly StyledProperty<int?> SelectedNumberProperty =
        AvaloniaProperty.Register<DisplayLayoutView, int?>(nameof(SelectedNumber), defaultBindingMode: BindingMode.TwoWay);

    public int? SelectedNumber
    {
        get => GetValue(SelectedNumberProperty);
        set => SetValue(SelectedNumberProperty, value);
    }

    static DisplayLayoutView()
    {
        AffectsRender<DisplayLayoutView>(DisplaysProperty, SelectedNumberProperty);
        AffectsMeasure<DisplayLayoutView>(DisplaysProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        return new Size(w, 200);
    }

    public override void Render(DrawingContext ctx)
    {
        _hitRects.Clear();
        var displays = Displays;
        if (displays == null || displays.Count == 0)
        {
            DrawCentered(ctx, new Rect(Bounds.Size), "No displays detected", 13, UnselText, null);
            return;
        }

        const double pad = 8, gap = 3;
        double availW = Bounds.Width - 2 * pad, availH = Bounds.Height - 2 * pad;
        if (availW <= 0 || availH <= 0) return;

        double minX = displays.Min(d => d.X), minY = displays.Min(d => d.Y);
        double vbW = displays.Max(d => d.X + d.Width) - minX;
        double vbH = displays.Max(d => d.Y + d.Height) - minY;
        if (vbW <= 0 || vbH <= 0) return;

        double scale = Math.Min(availW / vbW, availH / vbH);
        double offX = pad + (availW - vbW * scale) / 2;
        double offY = pad + (availH - vbH * scale) / 2;

        foreach (var d in displays)
        {
            var box = new Rect(offX + (d.X - minX) * scale, offY + (d.Y - minY) * scale,
                               d.Width * scale, d.Height * scale).Deflate(gap);
            if (box.Width <= 1 || box.Height <= 1) continue;
            _hitRects.Add((d, box));

            bool sel = SelectedNumber == d.Number;
            ctx.DrawRectangle(sel ? SelFill : UnselFill, sel ? SelBorder : UnselBorder, box, 8, 8);
            DrawLabels(ctx, box, d, sel);
        }
    }

    private void DrawLabels(DrawingContext ctx, Rect box, DisplayInfo d, bool selected)
    {
        var numBrush = selected ? SelText : UnselText;
        var subBrush = selected ? SubTextOnSel : SubText;

        double numSize = Math.Clamp(Math.Min(box.Height * 0.34, box.Width * 0.4), 12, 34);
        var num = Text(d.Number.ToString(), numSize, numBrush);

        // Sub-lines only when there's room.
        bool roomy = box.Height > numSize + 26 && box.Width > 70;
        FormattedText? name = roomy && !string.IsNullOrWhiteSpace(d.Name) ? Text(Ellipsize(d.Name, box.Width), 11, subBrush) : null;
        FormattedText? res = roomy ? Text(d.Resolution + (d.IsPrimary ? "  ·  Primary" : ""), 10.5, subBrush) : null;

        double totalH = num.Height + (name != null ? name.Height + 1 : 0) + (res != null ? res.Height + 1 : 0);
        double y = box.Y + (box.Height - totalH) / 2;
        double cx = box.X + box.Width / 2;

        ctx.DrawText(num, new Point(cx - num.Width / 2, y));
        y += num.Height + 1;
        if (name != null) { ctx.DrawText(name, new Point(cx - name.Width / 2, y)); y += name.Height + 1; }
        if (res != null) ctx.DrawText(res, new Point(cx - res.Width / 2, y));
    }

    private static void DrawCentered(DrawingContext ctx, Rect area, string text, double size, IBrush brush, IPen? _)
    {
        var ft = Text(text, size, brush);
        ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, area.Y + (area.Height - ft.Height) / 2));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush);

    private static string Ellipsize(string s, double boxWidth)
    {
        int max = Math.Max(6, (int)(boxWidth / 8));
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        foreach (var (display, box) in _hitRects)
        {
            if (box.Contains(p)) { SelectedNumber = display.Number; break; }
        }
    }
}
