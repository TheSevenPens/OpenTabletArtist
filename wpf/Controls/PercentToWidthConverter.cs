using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OtdWindowsHelper.Controls;

/// <summary>
/// Converts a percentage (0-100) and a parent width into a pixel width.
/// Values[0] = percent (double), Values[1] = parent width (double).
/// </summary>
public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is double percent
            && values[1] is double parentWidth
            && parentWidth > 0)
        {
            return Math.Clamp(percent / 100.0, 0, 1) * parentWidth;
        }
        return 0.0;
    }
}
