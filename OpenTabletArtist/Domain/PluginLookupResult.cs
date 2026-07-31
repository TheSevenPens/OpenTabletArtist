using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OpenTabletArtist.Domain;

/// <summary>How a plugin-repository lookup resolved. Lets the UI tell "up to date" from a real failure —
/// and one failure kind from another — instead of collapsing every outcome into a null (#21).</summary>
public enum PluginLookupStatus
{
    /// <summary>A compatible release was found (see <see cref="PluginLookupResult.Metadata"/>).</summary>
    Success,
    /// <summary>The repository was reached and read, but it has no release compatible with this build.</summary>
    NoCompatibleRelease,
    /// <summary>The repository couldn't be reached (offline, DNS, timeout, or an HTTP error).</summary>
    NetworkFailure,
    /// <summary>The repository responded, but its contents couldn't be read/parsed.</summary>
    ParseFailure,
}

/// <summary>The outcome of looking up the newest compatible plugin release: a status plus, on success,
/// the chosen <see cref="Metadata"/>. Non-success cases carry a short user-facing <see cref="StatusMessage"/>
/// so callers don't have to guess why a null came back (#21).</summary>
public sealed record PluginLookupResult(PluginLookupStatus Status, PluginMetadata? Metadata)
{
    public static PluginLookupResult Found(PluginMetadata metadata) => new(PluginLookupStatus.Success, metadata);
    public static readonly PluginLookupResult NoCompatibleRelease = new(PluginLookupStatus.NoCompatibleRelease, null);
    public static readonly PluginLookupResult NetworkFailure = new(PluginLookupStatus.NetworkFailure, null);
    public static readonly PluginLookupResult ParseFailure = new(PluginLookupStatus.ParseFailure, null);

    /// <summary>Classify a lookup exception: a failure to reach the repository (offline, DNS, timeout, HTTP
    /// error) is a <see cref="NetworkFailure"/>; anything else (archive/JSON couldn't be read) is a
    /// <see cref="ParseFailure"/>. Kept here so it's unit-testable without any network I/O.</summary>
    public static PluginLookupResult FromException(Exception ex) =>
        IsNetworkFailure(ex) ? NetworkFailure : ParseFailure;

    private static bool IsNetworkFailure(Exception ex) => ex switch
    {
        HttpRequestException => true,
        SocketException => true,
        TaskCanceledException => true,   // GetStreamAsync timeout / cancellation
        OperationCanceledException => true,
        TimeoutException => true,
        _ => ex.InnerException is { } inner && IsNetworkFailure(inner),
    };

    /// <summary>True when a compatible release was found.</summary>
    public bool IsSuccess => Status == PluginLookupStatus.Success && Metadata != null;

    /// <summary>True when the lookup genuinely failed to reach/read the repository (as opposed to reaching
    /// it and simply finding nothing compatible).</summary>
    public bool IsFailure => Status is PluginLookupStatus.NetworkFailure or PluginLookupStatus.ParseFailure;

    /// <summary>A short, user-facing explanation for a non-success result; empty for <see cref="Success"/>.</summary>
    public string StatusMessage => Status switch
    {
        PluginLookupStatus.NoCompatibleRelease => "No compatible plugin release is available for this version.",
        PluginLookupStatus.NetworkFailure => "Couldn't reach the plugin repository — check your connection.",
        PluginLookupStatus.ParseFailure => "The plugin repository responded but its contents couldn't be read.",
        _ => "",
    };
}
