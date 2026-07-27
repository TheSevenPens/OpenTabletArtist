using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenTabletArtist.Converters;

/// <summary>Bridges a <c>#RRGGBB</c> hex string (as stored on the gradient glow model) and the
/// <see cref="Color"/> that Avalonia's ColorPicker binds to. Alpha is dropped on the way back — the
/// gradient's opacity is a separate control — so the string stays a plain 6-digit RGB hex.</summary>
public sealed class HexColorConverter : IValueConverter
{
    public static readonly HexColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c))
            return Color.FromRgb(c.R, c.G, c.B); // force opaque for the picker; opacity is separate
        return Colors.Magenta;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : value;
}
