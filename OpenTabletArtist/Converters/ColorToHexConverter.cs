using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OpenTabletArtist.Converters;

/// <summary>Two-way bridge between a <see cref="Color"/> and an editable <c>#RRGGBB</c> hex string, used by
/// the colour picker's inline hex field (#622). Alpha is dropped (the pickers are opaque-only); a half-typed
/// or invalid string leaves the colour untouched rather than clobbering it.</summary>
public sealed class ColorToHexConverter : IValueConverter
{
    public static readonly ColorToHexConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && Color.TryParse(s, out var c)
            ? Color.FromRgb(c.R, c.G, c.B)   // opaque; the picker doesn't edit alpha
            : BindingOperations.DoNothing;    // keep the current colour while the text is incomplete/invalid
}
