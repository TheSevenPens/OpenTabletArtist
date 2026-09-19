namespace OtdInterop;

/// <summary>
/// Creates a connection to the OpenTabletDriver daemon.
/// </summary>
///
/// <remarks>
/// <para>
/// This exists so that the implementation does not have to. The connection can write settings directly
/// to the daemon, bypassing every ordering, ownership and session check that
/// <see cref="IOtdSettingsSession"/> applies — so the class that does it is internal, and a host gets
/// back <see cref="IDaemonTransport"/> rather than something it can cast down to.
/// </para>
/// <para>
/// That is a boundary, not a sandbox. Nothing stops a determined host opening the same named pipe
/// itself; what this prevents is the accidental case, where a class that legitimately needed a device
/// list or a log stream turned out to also have a settings writer, and somebody used it.
/// </para>
/// </remarks>
public static class DaemonTransport
{
    /// <summary>
    /// Builds a connection. Nothing is opened until <see cref="IDaemonTransport.ConnectAsync"/> is
    /// called, and the caller owns disposing it.
    /// </summary>
    /// <param name="log">
    /// Where the connection records what it could not do. Connect failures are expected and frequent
    /// while a daemon is starting, so they are throttled and reported at debug level rather than as
    /// faults; a connection that never comes up is diagnosed from those lines.
    /// </param>
    /// <returns>A connection that has not yet been opened.</returns>
    public static IDaemonTransport Create(IOtdLog log) => new DaemonClient(log);
}
