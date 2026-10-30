using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Controls;
using OpenTabletArtist.Domain.Health;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// What the top-bar health chip says (#1050): the worst finding's tier, how many findings there are, and
/// a sentence for the tooltip and screen readers.
/// </summary>
/// <remarks>
/// <para>
/// The worst finding sets the look, so a broken thing is never hidden by a pile of quiet ones. A finding
/// that is only information still counts and still shows, but it looks calm: the chip says something is
/// worth reading without saying something is wrong.
/// </para>
/// <para>
/// A daemon that is not connected counts as one broken finding, because that is how Home has always
/// treated it (it is deliberately not a health issue, #317). It is left out while a connect is in flight,
/// which is the state at every launch and would flash the chip red, and while the daemon is refused,
/// because that already has a card of its own in the catalog (#1042).
/// </para>
/// </remarks>
public sealed partial class HealthChipViewModel : ObservableObject, IDisposable
{
    private readonly HealthService _health;
    private readonly DaemonStatusViewModel _daemon;
    private readonly Action _open;

    public HealthChipViewModel(HealthService health, DaemonStatusViewModel daemon, Action open)
    {
        _health = health;
        _daemon = daemon;
        _open = open;
        _health.Issues.CollectionChanged += OnIssuesChanged;
        _health.PropertyChanged += OnSourceChanged;
        _daemon.PropertyChanged += OnSourceChanged;
        Recompute();
    }

    [ObservableProperty] private HealthChipTier _tier;
    [ObservableProperty] private int _count;
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _summary = "";

    /// <summary>Go to the Health page.</summary>
    [RelayCommand]
    private void Open() => _open();

    private void OnIssuesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Recompute();

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HealthService.HasIssues)
            or nameof(DaemonStatusViewModel.ShowDaemonProblem)
            or nameof(DaemonStatusViewModel.ShowDaemonActivity)
            or nameof(DaemonStatusViewModel.IsDaemonRefused)
            or nameof(DaemonStatusViewModel.IsConnected))
            Recompute();
    }

    private void Recompute()
    {
        var chip = Summarize(_health.Issues.Select(i => i.Severity), DaemonDown(_daemon));
        Tier = chip.Tier;
        Count = chip.Count;
        Label = LabelFor(chip.Tier);
        Summary = SummaryFor(chip.Tier, chip.Count);
    }

    /// <summary>Whether the daemon being unreachable is a finding of its own right now.</summary>
    internal static bool DaemonDown(DaemonStatusViewModel daemon) =>
        daemon.ShowDaemonProblem && !daemon.ShowDaemonActivity && !daemon.IsDaemonRefused;

    /// <summary>The chip for a set of findings. The worst severity wins; the count is all of them.</summary>
    internal static (HealthChipTier Tier, int Count) Summarize(IEnumerable<HealthSeverity> severities, bool daemonDown)
    {
        var count = 0;
        HealthSeverity? worst = null;
        foreach (var s in severities)
        {
            count++;
            if (worst is null || s > worst) worst = s;
        }
        if (daemonDown)
        {
            count++;
            worst = HealthSeverity.Broken;
        }
        return (worst is { } w ? TierFor(w) : HealthChipTier.Healthy, count);
    }

    internal static HealthChipTier TierFor(HealthSeverity severity) => severity switch
    {
        HealthSeverity.Broken => HealthChipTier.Broken,
        HealthSeverity.Misconfigured => HealthChipTier.Misconfigured,
        HealthSeverity.Recommendation => HealthChipTier.Recommendation,
        _ => HealthChipTier.Information,
    };

    internal static string LabelFor(HealthChipTier tier) => tier switch
    {
        HealthChipTier.Broken => "broken",
        HealthChipTier.Misconfigured => "misconfigured",
        HealthChipTier.Recommendation => "suggestion",
        HealthChipTier.Information => "info",
        _ => "healthy",
    };

    internal static string SummaryFor(HealthChipTier tier, int count)
    {
        if (count == 0) return "Health: all clear. Open the Health page.";
        var findings = count == 1 ? "1 finding" : $"{count} findings";
        var worst = tier switch
        {
            HealthChipTier.Broken => "something is broken",
            HealthChipTier.Misconfigured => "something is misconfigured",
            HealthChipTier.Recommendation => "the worst is a suggestion",
            _ => "none of them is a problem",
        };
        return $"Health: {findings}, {worst}. Open the Health page.";
    }

    public void Dispose()
    {
        _health.Issues.CollectionChanged -= OnIssuesChanged;
        _health.PropertyChanged -= OnSourceChanged;
        _daemon.PropertyChanged -= OnSourceChanged;
    }
}
