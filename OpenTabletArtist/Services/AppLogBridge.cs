using System;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Sends what OtdInterop wants recorded to this app's diagnostics log (#807).
///
/// The library takes an <see cref="IOtdLog"/> rather than calling <see cref="AppLog"/> directly, because
/// a library that reaches for a specific app's static logger is not a library. This is the whole of the
/// adapter: OTA already decides where log lines go, how they are formatted and when the file is rotated,
/// and none of that is the library's business.
///
/// Registered once and shared. <see cref="AppLog"/> is static and safe to call from any thread, which
/// matters here — the library calls this from inside settings operations, on whichever thread finished
/// the work.
/// </summary>
public sealed class AppLogBridge : IOtdLog
{
    /// <summary>The shared instance. There is no state, so there is no reason to have more than one.</summary>
    public static readonly AppLogBridge Instance = new();

    private AppLogBridge() { }

    /// <inheritdoc />
    public void Warn(string message, Exception? error = null) => AppLog.Warn(message, error);

    /// <inheritdoc />
    public void Info(string message) => AppLog.Info(message);
}
