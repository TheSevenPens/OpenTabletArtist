using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace OtdLinuxSetup;

public partial class MainWindow : Window
{
    private readonly List<HealthItem> _checks;
    private List<HealthCardViewModel> _cards = [];

    public MainWindow()
    {
        InitializeComponent();
        _checks = HealthChecker.CreateChecks();
        // Defer evaluation so the window renders first.
        Loaded += (_, _) => Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        HealthChecker.Evaluate(_checks);
        _cards = _checks.Select(c => new HealthCardViewModel(c)).ToList();
        ChecksList.ItemsSource = null;
        ChecksList.ItemsSource = _cards;

        var unhealthy = _checks.Count(c => c.Status == CheckStatus.Unhealthy);
        var pending = _checks.Count(c => c.Status == CheckStatus.PendingRelogin);
        FixAllButton.IsEnabled = unhealthy > 0;
        StatusText.Text = (unhealthy, pending) switch
        {
            (0, 0) => "All checks passed.",
            (0, _) => $"{pending} item(s) waiting for logout/login to take effect.",
            (_, 0) => $"{unhealthy} issue(s) need attention.",
            _ => $"{unhealthy} issue(s) need attention. {pending} waiting for logout/login.",
        };
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Refresh();

    /// <summary>Creates an IProgress that updates the step text on the UI thread.</summary>
    private IProgress<string> StepProgress() =>
        new Progress<string>(step => Dispatcher.UIThread.Post(() =>
        {
            ProgressStepText.Text = step;
        }));

    private void ShowProgress(bool visible)
    {
        ProgressPanel.IsVisible = visible;
        ProgressStepText.Text = "";
        RefreshButton.IsEnabled = !visible;
        FixAllButton.IsEnabled = !visible;
        // Disable fix buttons in the cards but keep the window enabled so it repaints
        ChecksList.IsEnabled = !visible;
    }

    private async void OnFixClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var check = _checks.FirstOrDefault(c => c.Id == id);
        if (check?.Fix == null) return;

        ShowProgress(true);
        StatusText.Text = $"Fixing: {check.Title}...";
        try
        {
            var result = await check.Fix(StepProgress());
            StatusText.Text = result;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ShowProgress(false);
            Refresh();
        }
    }

    private async void OnFixAllClick(object? sender, RoutedEventArgs e)
    {
        ShowProgress(true);
        var messages = new List<string>();
        foreach (var check in _checks.Where(c => c.CanFix).ToList())
        {
            StatusText.Text = $"Fixing: {check.Title}...";
            try
            {
                var result = await check.Fix!(StepProgress());
                messages.Add($"{check.Title}: {result}");
            }
            catch (Exception ex)
            {
                messages.Add($"{check.Title}: Error — {ex.Message}");
            }
        }
        ShowProgress(false);
        Refresh();
        StatusText.Text = string.Join("\n", messages);
    }
}

/// <summary>Thin view-model for binding a HealthItem to the card template.</summary>
public class HealthCardViewModel
{
    private readonly HealthItem _item;

    public HealthCardViewModel(HealthItem item) => _item = item;

    public string Id => _item.Id;
    public string Title => _item.Title;
    public string Description => _item.Description;
    public string? FixLabel => _item.FixLabel;
    public bool CanFix => _item.CanFix;

    public string StatusIcon => _item.Status switch
    {
        CheckStatus.Healthy => "✓",
        CheckStatus.Unhealthy => "✗",
        CheckStatus.PendingRelogin => "⏎",
        _ => "?",
    };

    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FBBF24"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#9CA3AF"));

    public IBrush StatusColor => _item.Status switch
    {
        CheckStatus.Healthy => Green,
        CheckStatus.Unhealthy => Red,
        CheckStatus.PendingRelogin => Amber,
        _ => Gray,
    };
}
