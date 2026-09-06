using System.Collections.Generic;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// A tabbed-page pivot whose content is two columns of existing subpage view models — the side-by-side
/// counterpart to <see cref="CompositeSectionViewModel"/>, which stacks them. Each section resolves to its
/// normal view via the typed DataTemplates; <c>TwoColumnSectionView</c> lays the two columns out. Purely a
/// container — it holds no state of its own, just references to the shared sub-VMs, so the layout change
/// affects nothing about how those pages work.
///
/// Used by the SETTINGS <b>Drivers</b> pivot: driver cleanup on the left, the VMulti driver on the right.
/// </summary>
public sealed class TwoColumnSectionViewModel
{
    public TwoColumnSectionViewModel(IReadOnlyList<object> left, IReadOnlyList<object> right)
    {
        LeftSections = left;
        RightSections = right;
    }

    /// <summary>Left column, top to bottom.</summary>
    public IReadOnlyList<object> LeftSections { get; }

    /// <summary>Right column, top to bottom.</summary>
    public IReadOnlyList<object> RightSections { get; }
}
