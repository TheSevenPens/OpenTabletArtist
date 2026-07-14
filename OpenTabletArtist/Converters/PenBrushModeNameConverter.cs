using System;
using System.Globalization;
using Avalonia.Data.Converters;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Converters;

/// <summary>Friendly display names for the SCRIBBLE Mode dropdown. The <see cref="PenBrushMode"/> enum
/// values are kept (they're persisted and drive the canvas rendering); only the shown label differs.</summary>
public sealed class PenBrushModeNameConverter : IValueConverter
{
    public static readonly PenBrushModeNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is PenBrushMode mode
            ? mode switch
            {
                PenBrushMode.PressureToSize => "Pressure Brush",
                PenBrushMode.AzimuthToRotation => "Tilt Brush 1",
                PenBrushMode.AltitudeToSize => "Tilt Brush 2",
                PenBrushMode.TwistToRotation => "Barrel Rotation Brush",
                PenBrushMode.PointerOnly => "Crosshairs (No drawing)",
                _ => mode.ToString(),
            }
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
