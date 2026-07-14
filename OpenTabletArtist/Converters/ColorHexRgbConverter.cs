using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenTabletArtist.Converters;

/// <summary>Formats a <see cref="Color"/> as "#RRGGBB · rgb(r, g, b)" for the colour-picker readout (#563).</summary>
public sealed class ColorHexRgbConverter : IValueConverter
{
    public static readonly ColorHexRgbConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Color c) return "";
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}  ·  rgb({c.R}, {c.G}, {c.B})";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
