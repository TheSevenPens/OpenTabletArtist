namespace OpenTabletArtist.Controls;

/// <summary>
/// The <b>entity</b> card role (#574): a card that represents a discrete <i>object</i> the user acts on —
/// a connected tablet, an express-key button, a wheel, a saved preset, an installed plugin. Structurally a
/// <see cref="Section"/> (same glass look + optional title, so shared styles like <c>expressCard</c> apply),
/// but a distinct role so the cardless redesign (#573) can treat "a thing" differently from pure grouping.
/// </summary>
public class Entity : Section
{
}
