namespace OtdInterop;

/// <summary>What happened when the session re-read the daemon's settings.</summary>
///
/// <remarks>
/// Nothing here is a failure the host must handle. Every value is an ordinary thing that happens while a
/// daemon is starting, stopping or being switched, and the session has already done the right thing with
/// each. They are distinguished because "the baseline did not change" has several causes and they are
/// not interchangeable when something looks wrong.
/// </remarks>
public enum SettingsReloadStatus
{
    /// <summary>The daemon's settings are now this session's baseline.</summary>
    Adopted,

    /// <summary>
    /// A transient override is running, so the daemon is not holding the baseline and was not asked.
    ///
    /// Reading it here is the defect #737 fixed: the snapshot would become what the editor shows, what a
    /// save would write as the user's default, and what a restore would restore to.
    /// </summary>
    SkippedOverride,

    /// <summary>
    /// The read was overtaken by a change that completed while it was in flight, so its answer describes
    /// a moment that has passed and was discarded.
    ///
    /// Not a stale display: the next edit is built on the baseline, so adopting an overtaken read sends
    /// the reverted value back to the daemon.
    /// </summary>
    Overtaken,

    /// <summary>The daemon had nothing to give — not connected — so the baseline is now empty. This is
    /// what clears the editor when a daemon goes away, and is deliberately not a failure.</summary>
    Disconnected,
}

/// <summary>The result of <see cref="IOtdSettingsSession.ReloadFromDaemonAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Adopted">
/// The new baseline, detached and stamped, when one was adopted. Null otherwise — including when the
/// adopted baseline is genuinely empty, which <see cref="SettingsReloadStatus.Disconnected"/> reports.
/// </param>
public readonly record struct SettingsReloadOutcome(
    SettingsReloadStatus Status,
    PreparedSettings? Adopted = null)
{
    /// <summary>A transient override is running; the daemon was not read.</summary>
    public static readonly SettingsReloadOutcome SkippedOverride = new(SettingsReloadStatus.SkippedOverride);

    /// <summary>The read was overtaken and discarded.</summary>
    public static readonly SettingsReloadOutcome Overtaken = new(SettingsReloadStatus.Overtaken);

    /// <summary>Nothing was connected; the baseline is empty.</summary>
    public static readonly SettingsReloadOutcome Disconnected = new(SettingsReloadStatus.Disconnected);

    /// <summary>True when this session's baseline now describes the daemon.</summary>
    public bool ChangedTheBaseline => Status is SettingsReloadStatus.Adopted or SettingsReloadStatus.Disconnected;
}
