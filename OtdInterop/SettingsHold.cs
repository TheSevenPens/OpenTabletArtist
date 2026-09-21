namespace OtdInterop;

/// <summary>
/// A change this session declined to send, and the state it was weighed against (#906).
/// </summary>
///
/// <remarks>
/// <para>
/// Handed back when a change is held by the comparison, and presented again with the same draft so that
/// resubmitting it is weighed against <b>the baseline that comparison used</b> rather than against
/// whatever this session has learned since. Without it the comparison is a single shared baseline, and a
/// reload — or another editor's decision — silently becomes permission to overwrite an edit nobody
/// weighed.
/// </para>
/// <para>
/// Not every held result carries one. An overwrite refused because the daemon moved again reports the
/// conflict it found but issues no hold, because it made no comparison on the draft's behalf; a caller
/// holding a draft keeps the hold it already had rather than taking a replacement built from that
/// refusal's fresh read. A hold over newly observed state would let the next ordinary edit write over
/// that state without anyone deciding to.
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
/// <para>
/// <b>It belongs to one draft.</b> Drop it as soon as that draft is resolved or replaced, and never
/// attach it to a later, unrelated one: what it protects is that draft's own expectation, and carrying it
/// onto a different change asks the session to weigh that change against something it was never built on.
/// </para>
/// <para>
/// The invariant is <em>compare this draft against its own unchanged expectation</em> — not "a hold can
/// only make a write harder", which is false. A daemon that moves away from the expected state and back
/// to it again will take the held draft while an ordinary submission of the same edit is held, because
/// the ordinary one is compared against the baseline a reload advanced in between. That is correct: the
/// draft was built on that state and the daemon is holding it, so nothing unseen is being replaced. This
/// compares state; it does not detect every edit that happened in between.
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

    /// <summary>
    /// The baseline the comparison that held this draft was made against, normalised.
    /// </summary>
    /// <remarks>
    /// Not "what the daemon was holding": in the conflict case those differ, and the difference is the
    /// whole of the conflict. What the daemon held is reported separately, in the
    /// <see cref="SettingsConflict"/>, because that is what an overwrite is authorised against. This is
    /// the other side — what the draft was weighed <em>with</em> — and it is what must not move.
    /// </remarks>
    internal string Expected { get; }
}
