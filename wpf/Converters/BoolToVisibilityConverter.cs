using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using TabletDriverUX.Views;

namespace TabletDriverUX.Converters;

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;
}

public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
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

/// <summary>
/// Converts a page name string to the corresponding UserControl view.
/// Caches views so they are only created once.
/// </summary>
public class PageToViewConverter : IValueConverter
{
    private readonly Dictionary<string, UserControl> _views = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string page) return null;
        if (!_views.TryGetValue(page, out var view))
        {
            view = page switch
            {
                "Dashboard" => new DashboardView(),
                "TabletSettings" => new TabletSettingsView(),
                "Presets" => new PresetsView(),
                "Diagnostics" => new DiagnosticsView(),
                "About" => new AboutView(),
                _ => new DashboardView()
            };
            _views[page] = view;
        }
        return view;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
