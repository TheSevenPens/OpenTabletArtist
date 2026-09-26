using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Domain.Health;
using OpenTabletDriver.Desktop.Profiles;
using OtdHealth;
using OtdHealth.Collector;
using OtdInterop;

namespace OpenTabletArtist.Services;

internal static class HealthCollectionAdapter
{
    internal static (HealthSources Sources, HealthPolicy Policy) Capture(IConnectionState connection,
        IDeviceData device, bool dataLoaded, DiagnosticsConnection diagnostics)
    {
        bool connected = connection.IsConnected;
        var profiles = ProfileInspector.Identify(device.Profiles.Select(p =>
            (JsonConvert.DeserializeObject<Profile>(JsonConvert.SerializeObject(p.Profile))!, p.IsDetected)));
        var optedOut = profiles.Where(p => WinInkAutoOptOut.IsOptedOut(p.Name)).Select(p => p.Id).ToHashSet();
        var pluginDir = device.PluginDirectory;
        var configDir = device.ConfigurationDirectory;
        var version = connection.DaemonVersion;
        var live = LiveHealthSource.Create(diagnostics);
        IReadOnlyList<DisplayBounds>? displays = null;
        Exception? displayFailure = null;
        try { displays = DisplayEnumerator.Enumerate().Select(d => new DisplayBounds(d.X, d.Y, d.Width, d.Height)).ToArray(); }
        catch (Exception ex) { displayFailure = ex; }
        return (new HealthSources
        {
            Connect = ct => connected ? live.Connect(ct) : Task.FromResult(false),
            Version = _ => Task.FromResult(version),
            Profiles = _ => dataLoaded || profiles.Count > 0 ? Task.FromResult(profiles)
                : throw new ProbeUnavailableException("OTA has not loaded daemon data yet."),
            PluginDirectory = _ => Task.FromResult(pluginDir),
            ConfigurationDirectory = _ => Task.FromResult(configDir),
            Displays = _ => displayFailure != null ? throw displayFailure : Task.FromResult(displays!),
            Conflicts = live.Conflicts,
            MacOSAccess = live.MacOSAccess,
        }, new HealthPolicy
        {
            ExpectedOtdVersion = HealthService.ExpectedOtdVersion,
            ForeignDaemon = connection.IsForeignDaemon,
            DaemonIsManagedButNotSelected = connection.DaemonIsManagedButNotSelected,
            DaemonSourceUnknown = connection.ShowDaemonSourceUnknown,
            RequiredDynamicsFilter = connection.IsAppOwnedDaemon ? PressureCurveProfile.FilterTypeName : null,
            WinInkOptedOutProfileIds = optedOut,
        });
    }

    internal static HealthInputs ToInputs(HealthSnapshot s) => new()
    {
        IsWindows = s.Platform == HealthPlatform.Windows,
        IsMacOS = s.Platform == HealthPlatform.MacOS,
        IsLinux = s.Platform == HealthPlatform.Linux,
        DaemonConnected = s.DaemonConnected,
        ForeignDaemon = s.ForeignDaemon,
        DaemonIsManagedButNotSelected = s.DaemonIsManagedButNotSelected,
        DaemonSourceUnknown = s.DaemonSourceUnknown,
        DaemonCannotOpenTablet = s.DaemonCannotOpenTablet,
        DaemonVersion = s.DaemonVersion,
        ExpectedOtdVersion = s.ExpectedOtdVersion,
        WinInkInstalled = s.WinInkInstalled,
        WinInkVersionMismatch = s.WinInkVersionMismatch,
        VMultiInstalled = s.VMultiInstalled,
        HasDriverConflict = s.HasDriverConflict,
        BlockingDriverConflict = s.BlockingDriverConflict,
        RunningElevated = s.RunningElevated,
        LinuxUdevRulesMissing = s.LinuxUdevRulesMissing,
        LinuxUserManagerRunning = s.LinuxUserManagerRunning,
        LinuxConflictingModulesLoaded = s.LinuxConflictingModulesLoaded,
        LinuxConflictingModulesNotBlacklisted = s.LinuxConflictingModulesNotBlacklisted,
        LinuxHidAccess = s.LinuxHidAccess switch
        {
            HidAccessStatus.Blocked => LinuxHidAccess.Blocked,
            HidAccessStatus.PendingRestart => LinuxHidAccess.PendingReboot,
            _ => LinuxHidAccess.Ok,
        },
        Tablets = s.Tablets.Select(t => new TabletHealthInput(t.Name, t.Detected, t.OutputModeIsWinInk,
            (DisplayMappingValidity)t.Mapping, t.NonCardinalRotation, !t.DynamicsWarningRequired,
            t.ConfigIsOverride, t.WinInkOptedOut, t.PenTipDisabled, t.PressureDisabled, t.TiltDisabled, t.Id)).ToArray(),
    };
}
