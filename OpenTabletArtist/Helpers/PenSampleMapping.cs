using System.Numerics;
using Avalonia;
using Avalonia.Controls;

namespace OpenTabletArtist.Helpers;

/// <summary>Shared mapping for driver-fed preview canvases (Scribble's test canvas and the pressure-preview
/// canvas): both take the daemon's raw pen position, map it to a virtual-desktop pixel, then to their own
/// on-screen rectangle so the stroke lands under the pen.</summary>
public static class PenSampleMapping
{
    /// <summary>Virtual-desktop pixel → canvas-local normalized (0..1). PointToScreen + RenderScaling are
    /// physical px (the same space OTD maps into); divide by scaling to DIPs, then by the canvas size.
    /// Returns false only when the canvas isn't laid out / attached; a returned point may still fall outside
    /// 0..1, which the caller treats as off-canvas.</summary>
    public static bool TryDesktopToCanvasNormalized(Visual canvas, Vector2 desktopPx, out double nx, out double ny)
    {
        nx = ny = 0;
        double w = canvas.Bounds.Width, h = canvas.Bounds.Height;
        if (w <= 0 || h <= 0 || TopLevel.GetTopLevel(canvas) is not { } top) return false;
        var origin = canvas.PointToScreen(new Point(0, 0));
        nx = (desktopPx.X - origin.X) / top.RenderScaling / w;
        ny = (desktopPx.Y - origin.Y) / top.RenderScaling / h;
        return true;
    }
}
