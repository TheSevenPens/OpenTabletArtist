using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Platform seam for enumerating connected monitors (#140). The Windows implementation reads full
/// geometry + friendly names + connector/GPU via the Win32 GDI / DisplayConfig APIs; other platforms
/// get a lower-fidelity view from Avalonia's cross-platform <c>Screens</c> (geometry + a best-effort
/// name, no port/GPU/refresh). Callers go through the static <see cref="DisplayEnumerator"/> facade,
/// which picks the implementation for the current OS.
/// </summary>
public interface IDisplayEnumerator
{
    /// <summary>All connected monitors in virtual-desktop pixels, ordered by display number. Never throws
    /// and never returns null — an empty list means "couldn't determine displays".</summary>
    IReadOnlyList<DisplayInfo> Enumerate();
}
