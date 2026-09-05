using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>Lowercases content — for the Zune wordmark nav, where the section labels (stored UPPERCASE)
/// render as lowercase words (`home`, `tablet`, …), and for enum values shown in a dropdown, which would
/// otherwise arrive PascalCased (`Bottom`, `Radial`). Anything non-null goes through its own ToString
/// first, which is what a text binding would have done with it anyway.</summary>
public sealed class LowerCaseConverter : IValueConverter
{
    public static readonly LowerCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString()?.ToLowerInvariant();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
