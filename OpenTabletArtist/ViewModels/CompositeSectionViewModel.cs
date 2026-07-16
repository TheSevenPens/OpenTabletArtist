using System.Collections.Generic;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// A tabbed-page pivot whose content is several existing subpage view models stacked vertically (Zune
/// Phase 2 merges): e.g. the SETTINGS <b>System</b> pivot = Startup + Shortcut + Driver Cleanup, or the
/// ADVANCED <b>Daemon</b> pivot = Daemon + Console. Each section resolves to its normal view via the typed
/// DataTemplates; <c>CompositeSectionView</c> renders them in order with spacing. Purely a container — it
/// holds no state of its own, just references to the shared sub-VMs, so the merge changes nothing about
/// how those pages work.
/// </summary>
public sealed class CompositeSectionViewModel
{
    public CompositeSectionViewModel(params object[] sections) => Sections = sections;

    /// <summary>The stacked subpage view models, in display order.</summary>
    public IReadOnlyList<object> Sections { get; }
}
