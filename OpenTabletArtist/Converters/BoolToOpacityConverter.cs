using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTabletArtist.Converters;

/// <summary>Maps a bool to an opacity (true → 1, false → 0). Lets a section crossfade on a bool binding
/// (e.g. a tab's IsChecked) when paired with an opacity <c>Transition</c> — the standard way to fade
/// between overlapping panels that share one cell, without a data-driven content host. See the tablet
/// page's pivot content (TabletDetailView).</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && d >= 0.5;
}
