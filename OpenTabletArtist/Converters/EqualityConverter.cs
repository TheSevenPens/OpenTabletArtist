using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>
/// True when the bound value equals the <c>ConverterParameter</c> (string comparison). Two-way, so it
/// drives a group of RadioButtons off a single string property: each radio's <c>IsChecked</c> binds to
/// the property with its option as the parameter — checking one writes that option back, and the others
/// re-evaluate to false. Unchecking (from another radio winning) writes nothing.
/// </summary>
public sealed class EqualityConverter : IValueConverter
{
    public static readonly EqualityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : BindingOperations.DoNothing;
}
