using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Helpers;

/// <summary>Shared mapping for driver-fed preview canvases (Scribble's test canvas and the pressure-preview
/// canvas): both take the daemon's raw pen position, map it to a virtual-desktop position, then to their own
/// on-screen rectangle so the stroke lands under the pen.</summary>
public static class PenSampleMapping
{
    /// <summary>Virtual-desktop position → canvas-local normalized (0..1). Returns false only when the canvas
    /// isn't laid out / attached; a returned point may still fall outside 0..1, which the caller treats as
    /// off-canvas.
    ///
    /// The subtlety is the coordinate space of <paramref name="desktopPx"/> vs
    /// <see cref="Visual.PointToScreen"/>, which differs by OS because OTD's display backend differs:
    /// <list type="bullet">
    /// <item><b>Windows</b> (<c>WindowsDisplay</c>, <c>dmPels</c>): the daemon's display space — hence
    /// desktopPx and Avalonia's <c>Screen.Bounds</c> — is in <b>physical pixels</b>, the same space
    /// <c>PointToScreen</c> returns. Canvas <c>Bounds</c> are DIPs, so the physical canvas size is
    /// <c>w*RenderScaling</c>: normalize with <c>(desktopPx - origin) / RenderScaling / w</c>.</item>
    /// <item><b>macOS</b> (<c>MacOSDisplay</c>, <c>CGDisplayBounds</c>): the daemon's display space — and both
    /// Avalonia's <c>Screen.Bounds</c> and <c>PointToScreen</c> — are in <b>points</b>, and a DIP is a point
    /// here. So normalize by the canvas DIP size directly: <c>(desktopPx - origin) / w</c>. Do NOT divide by
    /// <c>RenderScaling</c> — that is the Retina <em>backing</em> scale (e.g. 2), not a coordinate scale;
    /// dividing by it was the bug, squashing the stroke toward the canvas's top-left. (Verified empirically by
    /// pairing the driver-computed desktop with the OS-pointer's true canvas position during a pen sweep.)</item>
    /// </list></summary>
    public static bool TryDesktopToCanvasNormalized(Visual canvas, Vector2 desktopPx, out double nx, out double ny)
    {
        nx = ny = 0;
        double w = canvas.Bounds.Width, h = canvas.Bounds.Height;
        if (w <= 0 || h <= 0 || TopLevel.GetTopLevel(canvas) is not { } top) return false;
        var origin = canvas.PointToScreen(new Point(0, 0));

        if (OperatingSystem.IsMacOS())
        {
            nx = (desktopPx.X - origin.X) / w;
            ny = (desktopPx.Y - origin.Y) / h;
        }
        else
        {
            nx = (desktopPx.X - origin.X) / top.RenderScaling / w;
            ny = (desktopPx.Y - origin.Y) / top.RenderScaling / h;
        }
        return true;
    }
}
