using System;
using System.Globalization;
using Avalonia.Data.Converters;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;

namespace OpenTabletArtist.Converters;

/// <summary>Maps a <see cref="HealthSeverity"/> to the <see cref="AlertSeverity"/> tier an
/// <see cref="Alert"/> renders — so the "Needs attention" cards keep their per-tier tint after moving off
/// the old <c>attentionCard.*</c> classes (#574).</summary>
public sealed class HealthSeverityToAlertSeverityConverter : IValueConverter
{
    public static readonly HealthSeverityToAlertSeverityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is HealthSeverity s
            ? s switch
            {
                HealthSeverity.Broken => AlertSeverity.Error,
                HealthSeverity.Misconfigured => AlertSeverity.Warning,
                HealthSeverity.Recommendation => AlertSeverity.Neutral,
                _ => AlertSeverity.Information,
            }
            : AlertSeverity.Information;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
