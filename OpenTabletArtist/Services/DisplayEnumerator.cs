using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Static facade over the platform display-enumeration seam (#140). Keeps the long-standing
/// <c>DisplayEnumerator.Enumerate()</c> call shape used across the app while dispatching to the
/// implementation for the current OS: the full-fidelity Win32 path on Windows, Avalonia's cross-platform
/// <c>Screens</c> elsewhere. Swap the implementation via <see cref="Use"/> (tests, or a future
/// platform-specific enumerator); otherwise it picks a sensible default on first use.
/// </summary>
public static class DisplayEnumerator
{
    private static IDisplayEnumerator? _impl;

    /// <summary>Install the implementation to use (e.g. from tests, or app startup). Overrides the
    /// lazy OS default.</summary>
    public static void Use(IDisplayEnumerator impl) => _impl = impl;

    /// <summary>All connected monitors in virtual-desktop pixels, ordered by display number. Never throws
    /// and never returns null.</summary>
    public static IReadOnlyList<DisplayInfo> Enumerate() => (_impl ??= CreateDefault()).Enumerate();

    private static IDisplayEnumerator CreateDefault() =>
        System.OperatingSystem.IsWindows()
            ? new WindowsDisplayEnumerator()
            : new AvaloniaScreensDisplayEnumerator();
}
