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
    /// <para>
    /// Two uses, and neither is "decide what this operation should do". An implementation reads it to run
    /// work inline when the caller is already here, rather than queueing behind itself; and the library
    /// reads it from <em>inside</em> work this context just ran, to check that the context ran it where
    /// it says it runs things. Code whose behaviour otherwise depends on where it was called from would
    /// be reintroducing the ambiguity this type exists to remove.
    /// </para>
    /// <para>
    /// <b>The scope is posted work only.</b> It covers what this library starts on its own account — the
    /// identification and invalidation that follow a transition. It is not a check on a host's own calls
    /// into the settings session, which the library has no way to intercept: those the host is trusted to
    /// make from its context, and nothing here verifies that it did.
    /// </para>
    /// </remarks>
    bool IsCurrent { get; }

    // -----------------------------------------------------------------------------------------------
    // WHAT A HOST HAS TO PROVIDE, beyond implementing this interface.
    //
    // A host that calls the asynchronous settings operations must do so from a thread whose
    // SynchronizationContext resumes continuations back onto this context. Avalonia's dispatcher does;
    // the switch-check tool's pump installs one for exactly this reason.
    //
    // This is a requirement rather than something the library can arrange. Some of its operations await
    // work that has to happen on the context -- learning where a daemon keeps its settings, for one --
    // and what runs after such an await is coordinator state access, a file write, and a host callback.
    // Without a synchronization context those resume on the thread pool, concurrently with the
    // identification and invalidation running on the context, and no gate in this library serializes
    // them against each other.
    //
    // It would be nice to say that completing on the context is enough, because an inline continuation
    // then keeps the caller there. That is PERMITTED rather than guaranteed -- it depends on how the
    // awaited task was built and on what the runtime decides -- and a guarantee that holds by accident
    // is not one worth documenting. So: a synchronization context, or the confinement is the host's to
    // lose.
    // -----------------------------------------------------------------------------------------------
}
