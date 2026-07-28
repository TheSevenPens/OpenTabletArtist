using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenTabletArtist.Converters;

/// <summary>A <c>#RRGGBB</c> hex string → a solid brush, for colour swatches. Invalid/blank hex shows
/// transparent so a mid-typed value doesn't flash a wrong colour.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && Color.TryParse(s, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
