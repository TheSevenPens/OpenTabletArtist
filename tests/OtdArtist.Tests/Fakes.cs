using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.Profiles;
using OtdArtist.Domain;
using OtdArtist.Services;

namespace OtdArtist.Tests;

/// <summary>Records dialog requests and lets tests script confirm/input results.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public Profile? ShownProfile { get; private set; }
    public int ShowCount { get; private set; }
    public List<string> Messages { get; } = new();
    public (string Title, string Content)? LastTextViewer { get; private set; }

    /// <summary>Result returned by <see cref="ShowConfirmAsync"/> (default false).</summary>
    public bool ConfirmResult { get; set; }
    /// <summary>Result returned by <see cref="ShowInputAsync"/> (default null = cancelled).</summary>
    public string? InputResult { get; set; }

    public bool ShownDynamicsOnly { get; set; }

    public Task ShowTabletSettingsAsync(Profile profile, bool dynamicsOnly = false)
    {
        ShownProfile = profile;
        ShownDynamicsOnly = dynamicsOnly;
        ShowCount++;
        return Task.CompletedTask;
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(ConfirmResult);

    public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "")
        => Task.FromResult(InputResult);

    public Task ShowTextViewerAsync(string title, string content)
    {
        LastTextViewer = (title, content);
        return Task.CompletedTask;
    }
}

/// <summary>Minimal <see cref="IDeviceData"/> with settable data and a manual DataLoaded trigger.</summary>
internal sealed class FakeDeviceData : IDeviceData
{
    public JToken? Tablets => null;
    public IReadOnlyList<DetectedTablet> DetectedTablets { get; set; } = new List<DetectedTablet>();
    public bool HasTablet { get; set; }
    public string TabletName { get; set; } = "";
    public string TabletArea => "";
    public string TabletPressure => "";
    public string TabletButtons => "";
    public IReadOnlyList<ProfileItem> Profiles { get; set; } = new List<ProfileItem>();
    public string OutputMode => "";
    public bool HasWindowsInk { get; set; }
    public string PresetDirectory { get; set; } = "";
    public string PluginDirectory { get; set; } = "";
    public string SettingsFilePath => "";
    public (float Width, float Height)? GetTabletDigitizer(string tabletName) => null;

    public event Action? DataLoaded;
    public void RaiseDataLoaded() => DataLoaded?.Invoke();

#pragma warning disable CS0067 // required by INotifyPropertyChanged; not exercised by these tests
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
}

/// <summary>Points the Custom Tablet Configs page at a test-controlled directory.</summary>
internal sealed class FakeConfigurationsDirectoryProvider : IConfigurationsDirectoryProvider
{
    private readonly string _dir;
    public FakeConfigurationsDirectoryProvider(string dir) => _dir = dir;
    public string GetOrCreate() => _dir;
}

/// <summary>Minimal <see cref="IConnectionState"/>; only IsConnected is interesting (raises PropertyChanged).</summary>
internal sealed class FakeConnectionState : IConnectionState
{
    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected))); }
    }

    public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";
    public bool IsDaemonRunning => _isConnected;
    public bool IsAppOwnedDaemon => false;
    public bool IsForeignDaemon => false;
    public string DaemonSourcePath => "";
    public bool ShowAppOwnedDaemon => false;
    public bool ShowForeignDaemonWarning => false;
    public bool ShowDaemonSourceUnknown => false;
    public bool CanStartDaemon => !_isConnected;
    public bool IsDaemonExeMissing => false;
    public bool ShowStartButton => !_isConnected;
    public string DaemonStatusText => ConnectionStatus;
    public bool IsDaemonBusy => false;
    public string DaemonOperationStatus => "";
    public string DaemonOperationError => "";
    public bool HasDaemonOperationError => false;
    public IAsyncRelayCommand StartDaemonCommand => null!;
    public IAsyncRelayCommand StopDaemonCommand => null!;
    public IAsyncRelayCommand RestartDaemonCommand => null!;
    public IRelayCommand LaunchOtdUxCommand => null!;

    public event PropertyChangedEventHandler? PropertyChanged;
}
