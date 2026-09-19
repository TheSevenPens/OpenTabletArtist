using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// The settings an operation actually worked with: the caller's request after policy and format repairs
/// have been applied to it, detached from everything the caller holds.
/// </summary>
///
/// <remarks>
/// <para>
/// Three different objects are easy to confuse, and confusing them is how settings get lost:
/// </para>
/// <list type="bullet">
/// <item><b>The draft</b> — the caller's own object, which it may keep editing at any time. The library
/// never retains it and never modifies it.</item>
/// <item><b>The prepared revision</b> — this type. A private copy, normalized and repaired. It is what
/// gets sent and what gets written. It is <em>not</em> evidence that either succeeded.</item>
/// <item><b>What the daemon confirmed</b> — reported by the operation's outcome, which is the only thing
/// entitled to say a change is live.</item>
/// </list>
/// <para>
/// This is returned so a caller can see what its request became. Policy and repairs can legitimately
/// change a request — disabling a filter, filling in a missing area — and a caller that displays its own
/// draft afterwards would show something that was never sent. Adopting this is the caller's decision,
/// made explicitly, rather than a side effect of having called the library.
/// </para>
/// <para>
/// Check <see cref="Stamp"/> before adopting. A result can arrive after the daemon it was meant for has
/// gone, or after a newer edit has already been made; in both cases the right thing to do with it is
/// nothing.
/// </para>
/// </remarks>
///
/// <param name="Settings">
/// A detached copy. Nothing else references it, so the caller may keep or mutate it freely — doing so
/// cannot affect what was sent, what was written, or the library's own state.
/// </param>
/// <param name="Stamp">Which session this belonged to, and where it sat in that session's order.</param>
public sealed record PreparedSettings(Settings Settings, SettingsStamp Stamp);
