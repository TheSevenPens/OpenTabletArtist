using Avalonia.Layout;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// How tall the shell makes a page: as tall as its content, or the whole space the window leaves for it.
/// </summary>
/// <remarks>
/// Filling is opt-in. A page that is happy to be as tall as its content (nearly all of them) is top-aligned,
/// which is what the shell's old vertical stack did. A page whose main element should grow with the window
/// asks to be stretched; today that is Scribble, whose canvas used to stay at its minimum height however tall
/// the window was.
/// </remarks>
public static class PageLayout
{
    /// <summary>Pages that take the space the window leaves for them.</summary>
    public static bool FillsHeight(object? page) => page is TestViewModel;

    public static VerticalAlignment VerticalAlignmentFor(object? page) =>
        FillsHeight(page) ? VerticalAlignment.Stretch : VerticalAlignment.Top;
}
