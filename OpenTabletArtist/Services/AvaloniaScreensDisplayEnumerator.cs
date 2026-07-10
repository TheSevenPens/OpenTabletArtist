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
/// <para>Geometry is in Avalonia's coordinate space — on macOS that's CoreGraphics <em>logical points</em>
/// (a 4K panel reports 1920×1080), which is the <em>same</em> space OTD's macOS output stores its display
/// area in, so <c>DisplayMappingApplier</c> maps correctly with no scaling fix-up (verified against the live
/// daemon's stored area). Do not assume physical pixels here.</para>
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
        // Enumeration is best-effort and MUST NOT throw — callers treat an empty list as "couldn't determine
        // displays" and the UI renders that gracefully, whereas an exception bubbling out of a display refresh
        // (e.g. Avalonia teardown, or a call before the window handle exists) would crash the app. So the
        // catches are deliberately broad, matching WindowsDisplayEnumerator's never-throw contract; "fail
        // visible" applies to actionable failures, not to a geometry read that has a safe empty fallback.
        Screens? screens;
        try { screens = _screensProvider(); }
        catch { screens = null; }
        if (screens is null) return Array.Empty<DisplayInfo>();

        IReadOnlyList<Screen> all;
        try { all = screens.All; }
        catch { return Array.Empty<DisplayInfo>(); }

        // Avalonia has no notion of the OS "display number", so number by enumeration order (primary
        // first, then top-left-most) for a stable, human-friendly 1..N.
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
