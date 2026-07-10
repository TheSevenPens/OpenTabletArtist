using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// The display-enumeration seam (#140). One interface per OS concern so the platform choice lives in a
/// single factory (<see cref="DisplayEnumerator"/>) and the implementation is unit-testable via a fake.
/// The Windows implementation is <see cref="WindowsDisplayEnumerator"/> (GDI / DisplayConfig); the
/// cross-platform (macOS / Linux) implementation is <see cref="AvaloniaScreensDisplayEnumerator"/>
/// (Avalonia's <c>Screens</c>).
/// </summary>
public interface IDisplayEnumerator
{
    /// <summary>Connected monitors in virtual-desktop pixels, ordered by display number.</summary>
    IReadOnlyList<DisplayInfo> Enumerate();
}
