namespace OtdInterop;

/// <summary>
/// Where this library writes what it wants the user's log to record.
/// </summary>
///
/// <remarks>
/// <para>
/// Narrow on purpose. This library has no opinion about log files, levels, sinks or formatting — those
/// belong to whatever application hosts it, which already has a logging story. Three levels cover
/// everything written here: something went wrong, something happened, something a developer would want
/// while diagnosing.
/// </para>
/// <para>
/// It matters that these are called at all. Most of what this library reports is a partial failure — a
/// change that reached the daemon but not the disk, an operation discarded because the daemon changed
/// underneath it. Those are precisely the events that are invisible from the outside and that a user
/// reporting a problem cannot describe. Silence is the failure mode worth guarding against.
/// </para>
/// <para>
/// Implementations must not throw and must not block: these are called from inside settings operations,
/// and a logger that blocks stalls the session.
/// </para>
/// </remarks>
public interface IOtdLog
{
    /// <summary>
    /// Something did not work, or worked only partially, and the user may need to know.
    /// </summary>
    /// <param name="message">What happened, in terms a user could act on.</param>
    /// <param name="error">The underlying failure, when there was one.</param>
    void Warn(string message, Exception? error = null);

    /// <summary>
    /// Something worth recording that is not a problem — a repair that was applied, a decision that was
    /// taken automatically.
    /// </summary>
    /// <param name="message">What happened.</param>
    void Info(string message);

    /// <summary>
    /// Detail that matters only when something is being diagnosed, and that a user should not be shown
    /// as a problem. A connect attempt timing out while the daemon is still starting is the ordinary
    /// case, not a fault — but a connection that never comes up is diagnosed from exactly these lines.
    /// </summary>
    /// <param name="message">What happened.</param>
    /// <param name="error">The underlying failure, when there was one.</param>
    void Debug(string message, Exception? error = null);
}
