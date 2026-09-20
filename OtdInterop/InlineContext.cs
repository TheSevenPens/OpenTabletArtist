namespace OtdInterop;

/// <summary>
/// Runs work where it was posted from, immediately.
/// </summary>
///
/// <remarks>
/// <para>
/// For tests whose subject is not ordering. Most of this library's tests drive one thread and care about
/// what happened, not when; making each of them supply a scheduler would be ceremony that obscures what
/// they are about.
/// </para>
/// <para>
/// <b>Not a default for hosts, and internal so it cannot become one.</b> It satisfies
/// <see cref="IOtdExecutionContext"/>'s letter — work is serialized, because a single-threaded caller
/// running things inline is trivially serialized — while providing none of its value: a transport thread
/// posting here runs on the transport thread. A host that reached for it would have written the bug the
/// interface exists to prevent, which is why it is not offered.
/// </para>
/// </remarks>
internal sealed class InlineContext : IOtdExecutionContext
{
    /// <summary>Always true: whatever thread asks is the one work would run on.</summary>
    public bool IsCurrent => true;

    /// <inheritdoc />
    public Task PostAsync(Action work)
    {
        try
        {
            work();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // Faulted rather than thrown, so callers see the same shape they would from a real context.
            return Task.FromException(ex);
        }
    }
}
