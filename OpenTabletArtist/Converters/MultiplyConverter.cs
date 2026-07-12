using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>Multiplies a numeric (double) value by the factor given as the <c>ConverterParameter</c> —
/// e.g. sizing one control relative to another's measured width. Returns the input unchanged when it
/// isn't a number or the parameter isn't a valid factor.</summary>
public sealed class MultiplyConverter : IValueConverter
{
    public static readonly MultiplyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && parameter is not null &&
           double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
            ? d * factor
            : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
