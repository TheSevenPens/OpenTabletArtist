using System.Collections.Generic;
using Avalonia.Controls;

namespace OpenTabletArtist.Views;

/// <summary>
/// A view whose content divides again into a rail of RadioButtons — the tablet and pen pages' tab menus,
/// and the Theme and Dev subtab rails. Implemented so the screenshot sweep
/// (<c>MainWindow.CaptureAllPagesAsync</c>) can visit each one without knowing the concrete view types.
/// <para>
/// It used to know them: the sweep tested <c>CurrentPage is TabletPageViewModel</c> and then looked for a
/// <see cref="TabletDetailView"/>. <see cref="ViewModels.PenPageViewModel"/> derives from
/// <c>TabletPageViewModel</c>, so the Pen page passed the first test and failed the second — its view is
/// <see cref="PenDetailView"/> — and the sweep silently saved one shot of whichever tab happened to be
/// selected. A new tabbed view now joins the sweep by implementing this, rather than by someone
/// remembering to add a case (#690).
/// </para>
/// </summary>
public interface ITabbedContent
{
    /// <summary>The rail's currently-visible tab buttons, in rail order.</summary>
    IReadOnlyList<RadioButton> VisibleTabButtons();
}
