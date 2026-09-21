namespace OtdInterop;

/// <summary>
/// A change this session declined to send, and the state it was weighed against (#906).
/// </summary>
///
/// <remarks>
/// <para>
/// Handed back with every held result, and presented again with the same draft so that resubmitting it
/// is compared against <b>what that draft was held against</b> rather than against whatever this session
/// has learned since. Without it the comparison is a single shared baseline, and a reload — or another
/// editor's decision — silently becomes permission to overwrite an edit nobody weighed.
/// </para>
/// <para>
/// The observation lives in the hold rather than in this session, which is what makes two editors
/// independent. Each carries its own, so one artist resolving theirs cannot release the protection
/// around somebody else's draft: there is no shared thing to release.
/// </para>
/// <para>
/// Bound to the session that issued it and the connection it was taken on, for the same reason
/// <see cref="SettingsConflict"/> is: channel numbers are a per-session counter, and a hold from another
/// session carrying a coincidentally equal number is not this one's business. A hold that does not match
/// is ignored rather than honoured, which falls back to the ordinary comparison rather than to writing
/// blind.
/// </para>
/// <para>
/// Opaque. What it carries is this library's business, and a caller reasoning about the contents is
/// reasoning about a comparison it does not own.
/// </para>
/// </remarks>
public sealed record SettingsHold
{
    internal SettingsHold(Guid issuer, int channel, string expected)
    {
        Issuer = issuer;
        Channel = channel;
        Expected = expected;
    }

    /// <summary>Which session held it. A hold from another is not this one's to honour.</summary>
    internal Guid Issuer { get; }

    /// <summary>The connection it was taken on. A different daemon is a different question.</summary>
    internal int Channel { get; }

    /// <summary>What the daemon was holding when this draft was set aside, normalised for comparison.</summary>
    internal string Expected { get; }
}
