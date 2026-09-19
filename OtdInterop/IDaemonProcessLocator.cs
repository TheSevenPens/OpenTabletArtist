namespace OtdInterop;

/// <summary>
/// Turns a running daemon into the executable behind it.
/// </summary>
///
/// <remarks>
/// <para>
/// The library knows <em>that</em> a process answered its pipe and needs to know <em>which binary</em>
/// that was, because a different one is a session boundary: state describing the daemon that has gone
/// must not be applied to the one that replaced it. Reading a process's executable path is host
/// territory, so it is asked for here rather than done here.
/// </para>
/// <para>
/// Deliberately two facts and no decisions. Which executable the host <em>expects</em>, whether it
/// installed it, and whether the user should be asked before stopping it are product questions that stay
/// with the host — this is only "what is actually running", which is the part the library has to have.
/// </para>
/// <para>
/// Both members are best-effort. Returning null is an ordinary answer meaning "cannot see", not a
/// failure: an elevated daemon or one belonging to another user is unreadable by design, and the library
/// treats not knowing as a reason to leave state alone rather than to discard it.
/// </para>
/// </remarks>
public interface IDaemonProcessLocator
{
    /// <summary>The executable behind a process id, or null when it cannot be read.</summary>
    /// <param name="processId">The process answering the connection.</param>
    string? PathOf(int processId);

    /// <summary>
    /// The executable of the one running daemon, for when the connection cannot say which process
    /// answered it.
    ///
    /// The pipe-to-process-id lookup is Windows-only. Elsewhere the daemon is effectively a singleton, so
    /// "the running one" is a sound answer where "the one that answered this pipe" is unavailable. Null
    /// when there is not exactly one, or when its path cannot be read.
    /// </summary>
    string? SingleRunningDaemonPath();
}
