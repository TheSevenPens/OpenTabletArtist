using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>Lowercases string content — for the Zune wordmark nav, where the section labels (stored
/// UPPERCASE) render as lowercase words (`home`, `tablet`, …). Non-string content passes through
/// untouched.</summary>
public sealed class LowerCaseConverter : IValueConverter
{
    public static readonly LowerCaseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? s.ToLowerInvariant() : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
