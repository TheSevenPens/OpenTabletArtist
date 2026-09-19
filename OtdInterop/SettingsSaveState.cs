namespace OtdInterop;

/// <summary>
/// What the save indicator is reporting.
///
/// Settings are persisted on every change, so this drives a quiet indicator rather than a Save button —
/// and its real job is surfacing a write that failed.
///
/// The distinction that matters: <see cref="Failed"/> means the daemon took the change and the disk did
/// not, so it is live now and will be lost on the next daemon restart. A change that never reached the
/// daemon at all is <see cref="ApplyFailed"/> or <see cref="Disconnected"/>, and neither may be
/// described to the user as live.
/// </summary>
public enum SettingsSaveState
{
    /// <summary>Nothing to report.</summary>
    None,

    /// <summary>A change is being applied and written.</summary>
    Saving,

    /// <summary>Live on the daemon and written to disk.</summary>
    Saved,

    /// <summary>Applied to the daemon but NOT written to disk. Live now, lost on the next restart.</summary>
    Failed,

    /// <summary>The daemon was reachable but rejected or failed the change. Not live, not saved.</summary>
    ApplyFailed,

    /// <summary>No transport, so nothing was sent. Not live, not saved, and not a fault.</summary>
    Disconnected,
}
