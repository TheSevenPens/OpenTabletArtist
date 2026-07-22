using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>A single entry in the TABLET page's switch dropdown: a tablet's name, its live detection
/// status (drives the status dot), and a selection flag.</summary>
public partial class TabletChoiceViewModel : ObservableObject
{
    public TabletChoiceViewModel(string name, bool isDetected)
    {
        Name = name;
        _isDetected = isDetected;
    }

    public string Name { get; }
    [ObservableProperty] private bool _isDetected;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The single TABLET page (#542): a dropdown of every known tablet (detected + remembered) whose page
/// shows the selected one's <see cref="TabletDetailViewModel"/> as content — replacing the old per-tablet
/// nav children. The selection defaults to the first detected tablet (the last-used one when several are
/// detected), is persisted across sessions, and the page shows a placeholder when nothing is known.
/// </summary>
public partial class TabletPageViewModel : ObservableObject
{
    private const string LastUsedKey = "tablet.lastUsed";

    // Resolves (lazily creating + caching) a tablet's detail VM by name; owned by the shell so all the
    // per-tablet state + the daemon plumbing stays where it already lives. Returns null if unknown.
    private readonly Func<string, TabletDetailViewModel?> _resolve;
    // The persisted "last-used tablet" store. Defaults to AppSettings; injectable so the selection policy
    // is unit-testable without touching the on-disk settings.
    private readonly Func<string?> _getLastUsed;
    private readonly Action<string> _setLastUsed;
    private bool _suppressPersist;
    // Last real selection + a re-entrancy guard, used to shrug off the spurious null the ComboBox writes
    // back when its bound ItemsSource is (re)set during a page crossfade. See OnSelectedTabletChanged.
    private TabletChoiceViewModel? _lastSelected;
    private bool _restoringSelection;

    public TabletPageViewModel(Func<string, TabletDetailViewModel?> resolve,
        Func<string?>? getLastUsed = null, Action<string>? setLastUsed = null)
    {
        _resolve = resolve;
        _getLastUsed = getLastUsed ?? (() => AppSettings.Get(LastUsedKey));
        _setLastUsed = setLastUsed ?? (v => AppSettings.Set(LastUsedKey, v));
    }

    /// <summary>The dropdown choices — every known tablet, detected first (see the shell's ordering).</summary>
    public ObservableCollection<TabletChoiceViewModel> Tablets { get; } = new();

    /// <summary>The chosen tablet (the ComboBox's selected item). Setting it swaps the hosted content and,
    /// when the change is user-initiated, persists it as the last-used tablet.</summary>
    [ObservableProperty] private TabletChoiceViewModel? _selectedTablet;

    /// <summary>The selected tablet's detail VM, shown headerless by the content host (null → placeholder).</summary>
    public TabletDetailViewModel? Content { get; private set; }

    /// <summary>Whether a tablet is selected — toggles the page between the detail view and the placeholder.</summary>
    public bool HasTablet => Content != null;

    partial void OnSelectedTabletChanged(TabletChoiceViewModel? value)
    {
        // Avalonia quirk: when the ComboBox's bound ItemsSource is (re)set — e.g. this switcher is
        // re-created as the page crossfades in — its SelectionModel resets and the TwoWay SelectedItem
        // binding writes a spurious null back here, which would drop the selection → Content and flash the
        // "no tablet" hero. The items are already in place by then, so keep the previous selection and let
        // the binding re-sync the ComboBox to it. A genuine "nothing selected" only happens with no tablets.
        if (value == null && !_restoringSelection && Tablets.Count > 0
            && _lastSelected is { } keep && Tablets.Contains(keep))
        {
            _restoringSelection = true;
            SelectedTablet = keep;   // re-assert → re-pushes to the ComboBox, leaves Content intact
            _restoringSelection = false;
            return;
        }

        _lastSelected = value;
        Content = value != null ? _resolve(value.Name) : null;
        if (Content != null) Content.ShowHeader = false; // the page owns the switcher + status/actions header
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(HasTablet));
        foreach (var t in Tablets) t.IsSelected = ReferenceEquals(t, value);
        if (!_suppressPersist && value != null) _setLastUsed(value.Name);
    }

    /// <summary>Reconcile the choice list with the freshly-ordered tablets, keeping the current selection
    /// when it survives; otherwise pick the default (first detected, or the last-used one).</summary>
    public void SetTablets(IReadOnlyList<(string Name, bool IsDetected)> ordered)
    {
        // Reconcile the choice list fully IN PLACE — never Clear()+rebuild, and never drop the currently
        // selected item unless it's actually gone. A rebuild (or removing the selected item) momentarily
        // clears the ComboBox's selection, which nulls the TwoWay-bound SelectedTablet → Content, flashing
        // the empty switcher / "no tablet" hero before the reselection restores it. Preserving surviving
        // item instances — the selected one above all — keeps the ComboBox's selection valid throughout,
        // and also avoids snapping the hosted detail view back to its first tab on every ~15s poll.

        // 1) Drop tablets that are no longer present.
        var wanted = new HashSet<string>(ordered.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = Tablets.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Tablets[i].Name))
                Tablets.RemoveAt(i);

        // 2) Insert new tablets and reorder to match `ordered`, refreshing detection on survivors in place.
        //    At step i, positions 0..i-1 already match ordered[0..i-1], so the item for ordered[i] (if it
        //    survives) is at some index >= i — Move() it up without disturbing the settled prefix.
        for (int i = 0; i < ordered.Count; i++)
        {
            var (name, detected) = ordered[i];
            int at = IndexOfTablet(name);
            if (at >= 0)
            {
                Tablets[at].IsDetected = detected;
                if (at != i) Tablets.Move(at, i);
            }
            else
            {
                Tablets.Insert(i, new TabletChoiceViewModel(name, detected));
            }
        }

        // 3) Selection: keep the current tablet when it survived (its instance is still in the list, so the
        //    ComboBox never lost it); otherwise pick the default (first detected / last-used). Reconciliation
        //    isn't a user choice, so it must not clobber the stored last-used.
        if (SelectedTablet == null || !wanted.Contains(SelectedTablet.Name))
        {
            _suppressPersist = true;
            SelectedTablet = ChooseSelection(SelectedTablet?.Name);
            _suppressPersist = false;
        }
        else
        {
            // Kept the selection (no OnSelectedTabletChanged fires) — resync the per-item flag after moves.
            foreach (var t in Tablets) t.IsSelected = ReferenceEquals(t, SelectedTablet);
        }
    }

    private int IndexOfTablet(string name)
    {
        for (int i = 0; i < Tablets.Count; i++)
            if (Eq(Tablets[i].Name, name)) return i;
        return -1;
    }

    // Default-selection policy (#542): keep the current tablet if it's still present; otherwise default to
    // the first detected tablet, preferring the last-used one when several are detected; with nothing
    // detected fall back to the last-used remembered tablet, else the most-recently-seen (first in order).
    private TabletChoiceViewModel? ChooseSelection(string? previous)
    {
        if (Tablets.Count == 0) return null;
        if (previous != null && Tablets.FirstOrDefault(t => Eq(t.Name, previous)) is { } keep) return keep;

        var lastUsed = _getLastUsed();
        var detected = Tablets.Where(t => t.IsDetected).ToList();
        if (detected.Count > 0)
            return detected.FirstOrDefault(t => Eq(t.Name, lastUsed)) ?? detected[0];
        return Tablets.FirstOrDefault(t => Eq(t.Name, lastUsed)) ?? Tablets[0];
    }

    /// <summary>Select a tablet as a user action (the HOME cards / a health-issue "Fix" deep-link),
    /// optionally deep-linking to one of its tabs. No-op if the tablet isn't known.</summary>
    public void Select(string name, TabletDetailTab? tab = null)
    {
        if (Tablets.FirstOrDefault(t => Eq(t.Name, name)) is not { } match) return;
        SelectedTablet = match;
        if (tab is { } t) Content?.RequestTab(t);
    }

    /// <summary>Follow an externally-chosen active tablet (another page's switcher, the tray, or auto-select
    /// on connect) so the tablet switchers stay linked. Unlike <see cref="Select"/> this is not a user
    /// action: it suppresses the last-used persist, so it doesn't loop back into SetActiveTablet. No-op if
    /// the tablet isn't in this list or is already selected.</summary>
    public void SyncSelection(string name)
    {
        if (Tablets.FirstOrDefault(t => Eq(t.Name, name)) is not { } match) return;
        if (ReferenceEquals(match, SelectedTablet)) return;
        _suppressPersist = true;
        SelectedTablet = match;
        _suppressPersist = false;
    }

    private static bool Eq(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
