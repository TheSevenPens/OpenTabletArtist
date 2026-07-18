using Avalonia.Controls;

namespace OpenTabletArtist.Controls;

/// <summary>
/// The <b>canvas</b> card role (#574): a card whose surface is a live drawing/paint area rather than
/// grouped settings — the Scribble paint surface. A plain content box (glass look, its own border) so the
/// cardless redesign (#573) can treat the drawing surface on its own terms. (Not to be confused with
/// Avalonia's <c>Canvas</c> panel — this is a role wrapper; use the prefixed <c>c:Canvas</c> in XAML.)
/// </summary>
public class Canvas : ContentControl
{
}
