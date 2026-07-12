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
        // When the tablets + order are unchanged (the ~15s data poll normally reports the same set),
        // reconcile IN PLACE instead of Clear()+rebuild. A rebuild transiently nulls the ComboBox's bound
        // selection, which nulls Content and recreates the hosted detail view — snapping it back to its
        // first (About) tab every poll. Updating the detection dots in place avoids that churn entirely.
        bool sameSet = Tablets.Count == ordered.Count
            && Tablets.Zip(ordered, (existing, next) => Eq(existing.Name, next.Name)).All(match => match);
        if (sameSet)
        {
            for (int i = 0; i < ordered.Count; i++) Tablets[i].IsDetected = ordered[i].IsDetected;
            if (SelectedTablet == null && Tablets.Count > 0)
            {
                _suppressPersist = true;
                SelectedTablet = ChooseSelection(null);
                _suppressPersist = false;
            }
            return;
        }

        // Membership or order actually changed (a tablet was added / removed / reordered) — rebuild, keeping
        // the current selection where possible.
        var previous = SelectedTablet?.Name;
        Tablets.Clear();
        foreach (var (name, detected) in ordered)
            Tablets.Add(new TabletChoiceViewModel(name, detected));

        _suppressPersist = true;      // reconciliation is not a user choice — don't clobber the stored last-used
        SelectedTablet = ChooseSelection(previous);
        _suppressPersist = false;
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

    private static bool Eq(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
