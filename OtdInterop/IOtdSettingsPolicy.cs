using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Facts about the operation a policy is running inside. Deliberately small: only what current policy
/// needs to decide, and nothing it could use to reach the daemon or the disk.
/// </summary>
///
/// <param name="Stamp">Which session this operation belongs to, and its order within it.</param>
/// <param name="IsOwnedDaemon">
/// True when this application positively knows the daemon is one it manages.
///
/// Deliberately phrased as "is ours" rather than "is not theirs". Provenance is three-valued — ours,
/// someone else's, or not determinable — and an unidentifiable daemon is neither of the first two. A
/// policy that rewrites another user's settings because it could not tell whose they were is worse than
/// one that declines to act, so the undeterminable case reads as false here.
/// </param>
/// <param name="Persisting">
/// True when this operation <em>requests</em> a disk write. False for live-only and temporary overrides,
/// where the saved default is deliberately left alone.
///
/// Intent, not outcome — a requested write can still fail. And it is not a guarantee that settings from a
/// non-persisting operation can never reach a file: they are live on the daemon, and another program
/// reading that live state may save it.
/// </param>
public readonly record struct SettingsPolicyContext(
    SettingsStamp Stamp,
    bool IsOwnedDaemon,
    bool Persisting);

/// <summary>
/// A host-supplied rule applied to settings on their way out, before they reach the daemon or the disk.
/// </summary>
///
/// <remarks>
/// <para>
/// This exists because some decisions are the host application's, not this library's. Which third-party
/// filters an application is willing to leave enabled, for instance, is a product decision about that
/// application's users — the library has no business holding an opinion about it. But the decision has to
/// take effect inside the operation, after the request has been isolated and before anything is sent,
/// which is why it arrives as a callback rather than as something the caller does beforehand.
/// </para>
///
/// <para><b>What the library guarantees.</b></para>
/// <list type="bullet">
/// <item>The working copy is private. It is not the caller's object, and editing it
/// cannot affect anything the host still holds.</item>
/// <item>The result is copied again before it is sent or written. Whatever the implementation retains
/// afterwards is therefore inert: it is not the object that goes to the daemon, to the disk, or into the
/// library's own state.</item>
/// <item>Called with no library lock held, so an implementation cannot deadlock the session.</item>
/// <item>If this throws, the operation stops before anything leaves the process. Nothing is sent and
/// nothing is written.</item>
/// </list>
///
/// <para><b>What the implementation must guarantee.</b></para>
/// <list type="bullet">
/// <item>Return only when done. This is synchronous on purpose — copying cannot make a working copy safe
/// against a background thread still writing to it after the method returns.</item>
/// <item>Do not call back into the session from inside it. Re-entering an operation from within its own
/// policy step is not supported.</item>
/// <item>Do not depend on being called. A request that is superseded before admission never runs policy
/// at all, and that is not an error.</item>
/// </list>
/// </remarks>
public interface IOtdSettingsPolicy
{
    /// <summary>Applies the host's rules to <paramref name="workingCopy"/>, in place.</summary>
    /// <param name="workingCopy">A private copy to edit. Never the caller's object.</param>
    /// <param name="context">What operation this is running inside.</param>
    void Apply(Settings workingCopy, SettingsPolicyContext context);
}
