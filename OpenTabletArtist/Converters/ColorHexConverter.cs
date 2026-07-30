using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenTabletArtist.Converters;

/// <summary>Formats a <see cref="Color"/> as "#RRGGBB" for the colour-picker readout (#563). The
/// rgb(…) suffix was dropped — the hex alone is enough for the picker.</summary>
public sealed class ColorHexConverter : IValueConverter
{
    public static readonly ColorHexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Color c) return "";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
