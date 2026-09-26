using OpenTabletDriver.Desktop.Profiles;

namespace OtdHealth.Collector;

public sealed record ProfileObservation(string Id, string Name, Profile Profile, bool Detected);
public sealed record DisplayBounds(float X, float Y, float Width, float Height);
public sealed record InkObservation(bool Installed, bool VersionMismatch);
public sealed record ConflictObservation(bool HasConflict, bool Blocking);
public sealed record LinuxModulesObservation(IReadOnlyList<string> Loaded, bool NotBlacklisted);
public sealed record LinuxAccessObservation(HidAccessStatus Access, bool UserManagerRunning);

/// <summary>Read-only seams for live or already observed sessions. Delegates may throw; the collector
/// bounds each invocation and records failures. They must not mutate machine state. A fresh source is
/// required per analysis; in-flight native reads may finish after a deadline but cannot update reports.</summary>
public sealed class HealthSources
{
    public HealthPlatform Platform { get; init; } = HostProbes.Platform;
    public Func<CancellationToken, Task<bool>> Connect { get; init; } = Missing<bool>;
    public Func<CancellationToken, Task<string>> Version { get; init; } = Missing<string>;
    public Func<CancellationToken, Task<IReadOnlyList<ProfileObservation>>> Profiles { get; init; } = Missing<IReadOnlyList<ProfileObservation>>;
    public Func<CancellationToken, Task<string>> ConfigurationDirectory { get; init; } = Missing<string>;
    public Func<CancellationToken, Task<string>> PluginDirectory { get; init; } = Missing<string>;
    public Func<CancellationToken, Task<ConflictObservation>> Conflicts { get; init; } = Missing<ConflictObservation>;
    public Func<CancellationToken, Task<bool>> MacOSAccess { get; init; } = Missing<bool>;
    public Func<CancellationToken, Task<IReadOnlyList<DisplayBounds>>> Displays { get; init; } = _ => Task.FromResult(HostProbes.Displays());
    public Func<CancellationToken, Task<bool>> VMulti { get; init; } = _ => Task.FromResult(HostProbes.VMulti());
    public Func<CancellationToken, Task<bool>> ProcessElevation { get; init; } = _ => Task.FromResult(HostProbes.IsElevated());
    public Func<CancellationToken, Task<bool>> LinuxUdev { get; init; } = _ => Task.FromResult(HostProbes.LinuxUdev());
    public Func<CancellationToken, Task<LinuxModulesObservation>> LinuxModules { get; init; } = _ => Task.FromResult(HostProbes.LinuxModules());
    public Func<CancellationToken, Task<LinuxAccessObservation>> LinuxHidAccess { get; init; } = _ => Task.FromResult(HostProbes.LinuxAccess());
    private static Task<T> Missing<T>(CancellationToken _) => throw new ProbeUnavailableException("No evidence source was supplied.");
}
