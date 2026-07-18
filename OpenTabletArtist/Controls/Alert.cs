using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>Severity of an <see cref="Alert"/> — drives its background + border tint. Ordered
/// least→most severe; the values mirror the old <c>Border.attentionCard.*</c> classes.</summary>
public enum AlertSeverity
{
    /// <summary>Frosted look, plain grey border — an informational note.</summary>
    Information,
    /// <summary>Info-tinted — a neutral recommendation.</summary>
    Neutral,
    /// <summary>Warning-tinted — something is misconfigured.</summary>
    Warning,
    /// <summary>Error-tinted — something is broken.</summary>
    Error,
}

/// <summary>
/// The <b>severity</b> card role (#574): a bordered/tinted callout where the box <i>is</i> the meaning
/// (health alerts, the daemon-problem card, config-override / driver-conflict warnings). Owns its border +
/// padding permanently — unlike <see cref="Section"/>, this role keeps its chrome through the cardless
/// redesign (#573). The per-severity background/border come from the ControlTheme's Severity selectors.
/// Content goes in the element body (it's a <see cref="ContentControl"/>).
/// </summary>
public class Alert : ContentControl
{
    public static readonly StyledProperty<AlertSeverity> SeverityProperty =
        AvaloniaProperty.Register<Alert, AlertSeverity>(nameof(Severity), AlertSeverity.Information);

    /// <summary>Which tint/border this alert shows.</summary>
    public AlertSeverity Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
}
