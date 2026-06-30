using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the Console page — a live view of the OpenTabletDriver daemon's log, like OTD's
/// own Console tab. Seeds from the daemon's buffered log on connect (<see cref="IDaemonLogSource"/>)
/// and appends each pushed message. A minimum-level filter and a bounded buffer keep it readable and
/// cheap; the view handles auto-scroll + copy via <see cref="ScrollToEndRequested"/> / <see cref="BuildLogText"/>.
/// </summary>
public partial class ConsoleViewModel : ObservableObject, IDisposable
{
    // Bound so a long-running daemon can't grow the log without limit; oldest entries drop off.
    private const int MaxEntries = 5000;

    private readonly IDaemonLogSource _log;
    private readonly IConnectionState? _connection;
    // Unfiltered history, so changing the level filter can rebuild the visible list without a reseed.
    private readonly List<LogEntry> _all = new();

    /// <summary>The currently-visible (level-filtered) log entries.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>Minimum severity shown. Entries below this are hidden (kept in history).</summary>
    public LogLevel[] Levels { get; } = { LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error };
    [ObservableProperty] private LogLevel _minLevel = LogLevel.Info;

    /// <summary>Follow the tail: scroll to the newest entry as messages arrive.</summary>
    [ObservableProperty] private bool _autoScroll = true;

    [ObservableProperty] private bool _isConnected;

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Raised (UI thread) when the view should scroll to the latest entry.</summary>
    public event Action? ScrollToEndRequested;

    public ConsoleViewModel(IDaemonLogSource log, IConnectionState? connection = null)
    {
        _log = log;
        _connection = connection;
        _log.LogReceived += OnLogReceived;
        if (_connection != null)
        {
            _connection.PropertyChanged += OnConnectionChanged;
            IsConnected = _connection.IsConnected;
        }
        if (IsConnected) _ = SeedAsync();
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IConnectionState.IsConnected)) return;
        IsConnected = _connection!.IsConnected;
        // On a fresh connection, re-seed from the daemon's authoritative buffer.
        if (IsConnected) _ = SeedAsync();
    }

    private async Task SeedAsync()
    {
        var current = await _log.GetCurrentLogAsync();
        // GetCurrentLogAsync resumes off the UI thread; mutate the collections on the UI thread.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _all.Clear();
            foreach (var m in current) _all.Add(new LogEntry(m));
            if (_all.Count > MaxEntries) _all.RemoveRange(0, _all.Count - MaxEntries);
            Rebuild();
            if (AutoScroll) ScrollToEndRequested?.Invoke();
        });
    }

    // The daemon pushes Message off the RPC thread — marshal to the UI thread before touching state.
    private void OnLogReceived(LogMessage message) =>
        Dispatcher.UIThread.Post(() =>
        {
            var entry = new LogEntry(message);
            _all.Add(entry);
            if (_all.Count > MaxEntries) _all.RemoveAt(0);
            if (!Passes(entry)) return;
            Entries.Add(entry);
            if (Entries.Count > MaxEntries) Entries.RemoveAt(0);
            OnPropertyChanged(nameof(HasEntries));
            if (AutoScroll) ScrollToEndRequested?.Invoke();
        });

    private bool Passes(LogEntry e) => e.RawLevel >= MinLevel;

    private void Rebuild()
    {
        Entries.Clear();
        foreach (var e in _all.Where(Passes)) Entries.Add(e);
        OnPropertyChanged(nameof(HasEntries));
    }

    partial void OnMinLevelChanged(LogLevel value) => Rebuild();

    [RelayCommand]
    private void Clear()
    {
        _all.Clear();
        Entries.Clear();
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>Plain-text dump of the visible log, for the view's Copy action.</summary>
    public string BuildLogText()
    {
        var sb = new StringBuilder();
        foreach (var e in Entries)
            sb.AppendLine($"[{e.Time}] [{e.Level}] {e.Group}: {e.Message}");
        return sb.ToString();
    }

    public void Dispose()
    {
        _log.LogReceived -= OnLogReceived;
        if (_connection != null) _connection.PropertyChanged -= OnConnectionChanged;
    }
}

/// <summary>One log line, projected from a daemon <see cref="LogMessage"/> for display.</summary>
public sealed class LogEntry
{
    // Fixed per-level accent colours (shared instances). A console reads fine with stable level
    // colours rather than theme-driven ones.
    private static readonly IBrush DebugBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#EF4444"));

    public LogEntry(LogMessage m)
    {
        RawLevel = m.Level;
        Time = m.Time.ToString("HH:mm:ss");
        Level = m.Level.ToString().ToUpperInvariant();
        Group = m.Group ?? "";
        Message = m.Message ?? "";
        LevelBrush = m.Level switch
        {
            LogLevel.Debug => DebugBrush,
            LogLevel.Warning => WarnBrush,
            LogLevel.Error or LogLevel.Fatal => ErrorBrush,
            _ => InfoBrush,
        };
    }

    public LogLevel RawLevel { get; }
    public string Time { get; }
    public string Level { get; }
    public string Group { get; }
    public string Message { get; }
    public IBrush LevelBrush { get; }
}
