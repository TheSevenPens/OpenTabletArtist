using System;
using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Static facade over <see cref="IDisplayEnumerator"/> (#140). Preserves the existing
/// <c>DisplayEnumerator.Enumerate()</c> call sites while the platform decision lives in one place:
/// on Windows it dispatches to <see cref="WindowsDisplayEnumerator"/> (GDI / DisplayConfig); on any
/// other OS it returns an empty list until a cross-platform implementation is wired in (macOS plan,
/// Phase 1). Tests inject a fake via <see cref="Use"/>.
/// </summary>
public static class DisplayEnumerator
{
    private static IDisplayEnumerator? _impl;

    /// <summary>Override the implementation (unit tests). Pass <c>null</c> to reset to the OS default.</summary>
    public static void Use(IDisplayEnumerator? impl) => _impl = impl;

    public static IReadOnlyList<DisplayInfo> Enumerate() => (_impl ??= CreateDefault()).Enumerate();

    private static IDisplayEnumerator CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsDisplayEnumerator() : new EmptyDisplayEnumerator();
}

/// <summary>Fallback for non-Windows until a real cross-platform enumerator lands (macOS plan, Phase 1).
/// The app is Windows-only today, so this is never hit in production — it keeps the facade total.</summary>
public sealed class EmptyDisplayEnumerator : IDisplayEnumerator
{
    public IReadOnlyList<DisplayInfo> Enumerate() => Array.Empty<DisplayInfo>();
}
