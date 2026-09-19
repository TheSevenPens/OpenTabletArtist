namespace OtdInterop;

/// <summary>
/// The one place this library's work happens, supplied by the host.
/// </summary>
///
/// <remarks>
/// <para>
/// <see cref="IOtdSettingsSession"/> has always required a single serialized execution context, and until
/// now could only <em>demand</em> it: every call came from the host, so the host's own threading decided
/// whether the requirement held. That stops being true as soon as the library reacts to something itself.
/// A connection drops and returns on its own schedule, on a thread belonging to the transport, and the
/// library has to identify the daemon and invalidate stale state — which touches the same fields a host
/// may be reading. Demanding a context it cannot reach is no longer enough; it needs one it can post to.
/// </para>
/// <para>
/// <b>Required, never inferred.</b> There is no fallback to the thread pool and no implicit capture of an
/// ambient <c>SynchronizationContext</c>. Capturing one that happens to be absent yields the thread pool
/// silently, which is precisely the failure being avoided — a host that never established a context would
/// get one that looks like it works and does not.
/// </para>
/// <para>
/// OTA supplies its dispatcher. A test supplies a scheduler it can step by hand, which is what makes the
/// orderings in #828 expressible at all rather than reproducible by luck. A headless host supplies any
/// single-threaded pump; nothing here needs a UI framework.
/// </para>
/// </remarks>
public interface IOtdExecutionContext
{
    /// <summary>
    /// Runs <paramref name="work"/> on this context, serialized against everything else posted here.
    /// </summary>
    /// <remarks>
    /// The returned task is the observable completion and failure path. Work posted here happens on
    /// somebody else's schedule, so an exception inside it has no caller to reach — without this it
    /// would be lost, and the library would have failed to do something it was asked to do with nothing
    /// to show for it.
    ///
    /// Posting from the context itself must not deadlock. Implementations may run such work inline or
    /// queue it behind what is already pending; either is acceptable, and neither may block waiting for
    /// a context the caller is already occupying.
    /// </remarks>
    /// <param name="work">What to run.</param>
    /// <returns>Completion of <paramref name="work"/>, faulted if it threw.</returns>
    Task PostAsync(Action work);

    /// <summary>
    /// True when the calling thread is already this context.
    /// </summary>
    /// <remarks>
    /// For asserting, not for branching. The library uses it to catch a host calling in from somewhere it
    /// promised not to; code that behaves differently depending on where it is called from would be
    /// reintroducing the ambiguity this type exists to remove.
    /// </remarks>
    bool IsCurrent { get; }
}
