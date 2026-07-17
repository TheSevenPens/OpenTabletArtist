using System;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// The PEN page host. Structurally identical to <see cref="TabletPageViewModel"/> — same tablet switcher,
/// selection policy, and per-tablet detail resolution — but a distinct type so the typed-nav DataTemplate
/// renders <c>PenPageView</c> (the pen pivots: movement · inputs · buttons) instead of the tablet page.
/// It shares the shell's per-tablet detail cache through the same resolver, so the pen page and the tablet
/// page edit the same <see cref="TabletDetailViewModel"/> for a given tablet.
/// </summary>
public sealed class PenPageViewModel : TabletPageViewModel
{
    public PenPageViewModel(Func<string, TabletDetailViewModel?> resolve,
        Func<string?>? getLastUsed = null, Action<string>? setLastUsed = null)
        : base(resolve, getLastUsed, setLastUsed)
    {
    }
}
