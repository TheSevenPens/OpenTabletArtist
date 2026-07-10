using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;                 // Screens
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;                 // Screen
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>
/// Cross-platform (macOS / Linux) implementation of <see cref="IDisplayEnumerator"/> built on Avalonia's
/// <c>Screens</c>. Lower fidelity than the Windows path — Avalonia exposes monitor geometry and a
/// best-effort <c>DisplayName</c>, but not the connector/port, driving GPU, or refresh rate — so those
/// <see cref="DisplayInfo"/> fields come back empty/zero, which the UI already renders gracefully (#140).
///
/// <para>Avalonia's <c>Screens</c> lives on a window, so this needs a live <see cref="Screens"/> instance.
/// It's supplied by a provider (default: the desktop lifetime's main window) so the type stays testable
/// and never hard-depends on a particular window. If no screens are available yet (e.g. the window handle
/// isn't created), it returns an empty list rather than throwing — same contract as the Windows path.</para>
/// </summary>
public sealed class AvaloniaScreensDisplayEnumerator : IDisplayEnumerator
{
    private readonly Func<Screens?> _screensProvider;

    public AvaloniaScreensDisplayEnumerator(Func<Screens?>? screensProvider = null)
    {
        _screensProvider = screensProvider ?? (() => DefaultScreens);
    }

    public IReadOnlyList<DisplayInfo> Enumerate()
    {
        Screens? screens;
        try { screens = _screensProvider(); }
        catch { screens = null; }
        if (screens is null) return Array.Empty<DisplayInfo>();

        IReadOnlyList<Screen> all;
        try { all = screens.All; }
        catch { return Array.Empty<DisplayInfo>(); }

        // Avalonia has no notion of the OS "display number", so number by enumeration order (primary
        // first, then the rest) for a stable, human-friendly 1..N. Geometry is in physical pixels.
        var ordered = all
            .OrderByDescending(s => s.IsPrimary)
            .ThenBy(s => s.Bounds.Y)
            .ThenBy(s => s.Bounds.X)
            .ToList();

        var result = new List<DisplayInfo>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            result.Add(new DisplayInfo(
                Number: i + 1,
                Name: s.DisplayName ?? "",
                Width: s.Bounds.Width,
                Height: s.Bounds.Height,
                X: s.Bounds.X,
                Y: s.Bounds.Y,
                IsPrimary: s.IsPrimary,
                RefreshHz: 0,   // not exposed by Avalonia
                Port: "",       // not exposed by Avalonia
                Gpu: ""));      // not exposed by Avalonia
        }
        return result;
    }

    /// <summary>Resolve <see cref="Screens"/> from the running app's main window. Null when there's no
    /// desktop lifetime or the window/handle isn't up yet.</summary>
    private static Screens? DefaultScreens =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Screens
            : null;
}
