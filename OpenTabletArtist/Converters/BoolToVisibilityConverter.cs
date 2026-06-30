using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;
}

public class NonEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true when ALL values are false.
/// </summary>
public class AllFalseBoolConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var v in values)
            if (v is true) return false;
        return true;
    }
}

/// <summary>
/// Returns true when: first value is false AND second value is true.
/// </summary>
public class NotFirstAndSecondBoolConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is bool first && values[1] is bool second)
            return !first && second;
        return false;
    }
}
