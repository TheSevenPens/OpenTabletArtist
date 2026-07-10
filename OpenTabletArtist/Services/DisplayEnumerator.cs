using System;
using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Static facade over <see cref="IDisplayEnumerator"/> (#140). Preserves the existing
/// <c>DisplayEnumerator.Enumerate()</c> call sites while the platform decision lives in one place:
/// on Windows it dispatches to <see cref="WindowsDisplayEnumerator"/> (GDI / DisplayConfig); on any
/// other OS to <see cref="AvaloniaScreensDisplayEnumerator"/> (cross-platform, via Avalonia's Screens).
/// Tests inject a fake via <see cref="Use"/>.
/// </summary>
public static class DisplayEnumerator
{
    private static IDisplayEnumerator? _impl;

    /// <summary>Override the implementation (unit tests). Pass <c>null</c> to reset to the OS default.</summary>
    public static void Use(IDisplayEnumerator? impl) => _impl = impl;

    public static IReadOnlyList<DisplayInfo> Enumerate() => (_impl ??= CreateDefault()).Enumerate();

    private static IDisplayEnumerator CreateDefault() =>
        OperatingSystem.IsWindows()
            ? new WindowsDisplayEnumerator()
            : new AvaloniaScreensDisplayEnumerator();
}
