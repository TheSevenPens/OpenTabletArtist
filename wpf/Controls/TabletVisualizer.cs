using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TabletDriverUX.Controls;

/// <summary>
/// Draws a scaled tablet area with a pen position dot.
/// Bind PenX/PenY (tablet coordinates), MaxX/MaxY (tablet max coordinates),
/// and DigitizerWidth/DigitizerHeight (physical mm) to drive the display.
/// </summary>
public class TabletVisualizer : Control
{
    public static readonly StyledProperty<double> PenXProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(PenX));
    public static readonly StyledProperty<double> PenYProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(PenY));
    public static readonly StyledProperty<double> MaxXProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(MaxX));
    public static readonly StyledProperty<double> MaxYProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(MaxY));
    public static readonly StyledProperty<double> DigitizerWidthProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(DigitizerWidth));
    public static readonly StyledProperty<double> DigitizerHeightProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(DigitizerHeight));
    public static readonly StyledProperty<double> PressureProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(Pressure));
    public static readonly StyledProperty<double> MaxPressureProperty =
        AvaloniaProperty.Register<TabletVisualizer, double>(nameof(MaxPressure));
    public static readonly StyledProperty<bool> HasPositionProperty =
        AvaloniaProperty.Register<TabletVisualizer, bool>(nameof(HasPosition));

    public double PenX { get => GetValue(PenXProperty); set => SetValue(PenXProperty, value); }
    public double PenY { get => GetValue(PenYProperty); set => SetValue(PenYProperty, value); }
    public double MaxX { get => GetValue(MaxXProperty); set => SetValue(MaxXProperty, value); }
    public double MaxY { get => GetValue(MaxYProperty); set => SetValue(MaxYProperty, value); }
    public double DigitizerWidth { get => GetValue(DigitizerWidthProperty); set => SetValue(DigitizerWidthProperty, value); }
    public double DigitizerHeight { get => GetValue(DigitizerHeightProperty); set => SetValue(DigitizerHeightProperty, value); }
    public double Pressure { get => GetValue(PressureProperty); set => SetValue(PressureProperty, value); }
    public double MaxPressure { get => GetValue(MaxPressureProperty); set => SetValue(MaxPressureProperty, value); }
    public bool HasPosition { get => GetValue(HasPositionProperty); set => SetValue(HasPositionProperty, value); }

    static TabletVisualizer()
    {
        AffectsRender<TabletVisualizer>(
            PenXProperty, PenYProperty, MaxXProperty, MaxYProperty,
            DigitizerWidthProperty, DigitizerHeightProperty,
            PressureProperty, MaxPressureProperty, HasPositionProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var digiW = DigitizerWidth;
        var digiH = DigitizerHeight;

        // Draw background
        var bgBrush = new SolidColorBrush(Color.FromArgb(30, 99, 102, 241)); // subtle indigo
        var bgPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 99, 102, 241)), 1);
        context.DrawRectangle(bgBrush, bgPen, new Rect(0, 0, w, h), 8, 8);

        if (digiW <= 0 || digiH <= 0 || MaxX <= 0 || MaxY <= 0)
        {
            // No tablet data — draw placeholder text
            var text = new FormattedText("No tablet data",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI", FontStyle.Italic),
                12, new SolidColorBrush(Color.FromArgb(100, 148, 163, 184)));
            context.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
            return;
        }

        // Scale tablet area to fit control while maintaining aspect ratio
        const double padding = 12;
        var availW = w - padding * 2;
        var availH = h - padding * 2;
        var scaleX = availW / digiW;
        var scaleY = availH / digiH;
        var scale = Math.Min(scaleX, scaleY);

        var tabletW = digiW * scale;
        var tabletH = digiH * scale;
        var offsetX = (w - tabletW) / 2;
        var offsetY = (h - tabletH) / 2;

        // Draw tablet area
        var tabletBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        var tabletPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 99, 102, 241)), 1.5);
        context.DrawRectangle(tabletBrush, tabletPen, new Rect(offsetX, offsetY, tabletW, tabletH), 4, 4);

        if (!HasPosition) return;

        // Map pen coordinates to pixel position
        // Tablet coords: (0,0) to (MaxX, MaxY) maps to physical (0,0) to (DigitizerWidth, DigitizerHeight)
        var penPhysX = (PenX / MaxX) * digiW;
        var penPhysY = (PenY / MaxY) * digiH;
        var dotX = offsetX + penPhysX * scale;
        var dotY = offsetY + penPhysY * scale;

        // Pressure affects dot size (3-8px radius)
        var pressureNorm = MaxPressure > 0 ? Math.Clamp(Pressure / MaxPressure, 0, 1) : 0;
        var dotRadius = 3 + pressureNorm * 5;

        // Draw pen dot
        var dotBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241)); // indigo
        context.DrawEllipse(dotBrush, null, new Point(dotX, dotY), dotRadius, dotRadius);

        // Draw pressure ring
        if (pressureNorm > 0.01)
        {
            var ringPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(pressureNorm * 150), 99, 102, 241)), 1.5);
            var ringRadius = dotRadius + 4;
            context.DrawEllipse(null, ringPen, new Point(dotX, dotY), ringRadius, ringRadius);
        }
    }
}
