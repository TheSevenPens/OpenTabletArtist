using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The <b>control-panel</b> card role: a bordered content box meant to hold interactive controls grouped
/// like a ribbon (e.g. the Scribble input/mode controls above the paint surface). A plain content box with
/// the same glass fill + border as an <see cref="Entity"/>, kept a distinct role so control clusters can be
/// styled on their own terms. Content goes in the element body (it's a <see cref="ContentControl"/>); the
/// default <c>ControlTheme</c> lives in Themes/Styles.axaml.
/// </summary>
public class ControlPanel : ContentControl
{
}
