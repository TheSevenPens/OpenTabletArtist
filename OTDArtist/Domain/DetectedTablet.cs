namespace OtdArtist.Domain;

/// <summary>
/// A currently-connected tablet, as surfaced to the UI (#190). One per entry in the daemon's
/// <c>GetTablets()</c> result, so the Dashboard can show a card per tablet instead of collapsing to
/// the first one (where a second connected tablet would look undetected).
/// </summary>
/// <param name="Name">The tablet's reported name (matches its profile's <c>Tablet</c>).</param>
/// <param name="Area">Digitizer size, formatted (e.g. "152 x 95 mm").</param>
/// <param name="Pressure">Max pressure levels, as text.</param>
/// <param name="Buttons">Pen button count, as text.</param>
public record DetectedTablet(string Name, string Area, string Pressure, string Buttons);
