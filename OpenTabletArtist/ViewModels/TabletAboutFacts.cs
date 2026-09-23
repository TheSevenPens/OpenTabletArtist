using System.Collections.Generic;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// The ABOUT tab's spec rows, and the three pure functions that build them from a

// TabletAboutInfo. Static and side-effect free, which is why they could move without ceremony.

/// <summary>One label/value row in the tablet ABOUT tab's spec list.</summary>
public sealed record TabletFact(string Label, string Value);

/// <summary>Builds the ABOUT tab's rows. Pure: every input arrives as a parameter.</summary>
public static class TabletAboutFacts
{
    // Core dimensions — the SPECIFICATIONS card (up to and including the active-area aspect ratio).
    public static IReadOnlyList<TabletFact> BuildFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (!string.IsNullOrEmpty(a.Name)) facts.Add(new("Name", a.Name));
        return facts;
    }

    // The active area's own measurements, split out of the identity card so the ACTIVE AREA section can
    // hold them next to the drawing of the same thing. Labels drop the "Active area" prefix — the section
    // heading carries it, and repeating it on every row was only there when they sat under "BASICS".
    public static IReadOnlyList<TabletFact> BuildActiveAreaFacts(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (a.WidthMm > 0 && a.HeightMm > 0)
        {
            facts.Add(new("Size", TabletAboutInfo.FormatSize(a.WidthMm, a.HeightMm)));
            double diag = System.Math.Sqrt(a.WidthMm * a.WidthMm + a.HeightMm * a.HeightMm);
            facts.Add(new("Diagonal", TabletAboutInfo.FormatLength(diag)));
            facts.Add(new("Aspect ratio", TabletAboutInfo.FormatAspectRatio(a.WidthMm, a.HeightMm)));
        }
        return facts;
    }

    // Capabilities — the FEATURES card (everything after the active-area aspect ratio).
    public static IReadOnlyList<TabletFact> BuildFeatures(TabletAboutInfo a)
    {
        var facts = new System.Collections.Generic.List<TabletFact>();
        if (a.LpMm is > 0 && a.Lpi is > 0)
            facts.Add(new("Digitizer resolution", $"{a.LpMm:N0} LPmm ({a.Lpi:N0} LPI)"));
        if (a.MaxPressure is > 0) facts.Add(new("Pressure levels", $"{a.MaxPressure:N0}"));
        if (a.PenButtons is { } pb) facts.Add(new("Pen buttons", pb.ToString()));
        if (a.ExpressKeys is > 0) facts.Add(new("Buttons", a.ExpressKeys!.Value.ToString()));
        if (a.MouseButtons is > 0) facts.Add(new("Mouse buttons", a.MouseButtons!.Value.ToString()));
        if (a.WheelCount > 0) facts.Add(new("Wheels", a.WheelCount == 1 ? "Yes" : a.WheelCount.ToString()));
        if (a.StripCount > 0) facts.Add(new("Touch strips", a.StripCount.ToString()));
        if (a.HasTouch) facts.Add(new("Touch input", "Supported"));
        return facts;
    }
}
