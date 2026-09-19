using Newtonsoft.Json;
using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>
/// Turns <see cref="Settings"/> into bytes and back, in the exact form OpenTabletDriver reads and writes.
/// </summary>
///
/// <remarks>
/// <para>
/// Streams only. This resolves no path, chooses no destination, knows nothing about sessions, and applies
/// nothing to a daemon — which is what lets it be shared by callers that have no business writing the
/// active settings file. Deciding <em>where</em> bytes go is an authority; turning settings into bytes is
/// a mechanism, and they are separated here so that one shared implementation does not hand out the
/// other.
/// </para>
/// <para>
/// The settings file is shared with OpenTabletDriver's own interface, so the encoding has to match what
/// that interface produces or a user who edits in both places gets a diff-churning file. Indented, stock
/// otherwise — the same options upstream uses.
/// </para>
/// </remarks>
public static class SettingsCodec
{
    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="destination"/>, leaving the stream open.
    /// </summary>
    ///
    /// <remarks>
    /// A fresh serializer per call, deliberately. <see cref="JsonSerializer"/> is not safe to use from two
    /// threads at once, and one shared instance behind a method any caller can reach is an invitation to
    /// find that out the hard way. Constructing one is cheap next to the write it precedes.
    ///
    /// The stream is left open because the caller owns it and usually still has to flush it somewhere
    /// durable — see <c>AtomicFile</c>, which needs the file on disk before it swaps anything.
    /// </remarks>
    ///
    /// <param name="settings">What to write.</param>
    /// <param name="destination">Where to write it. Left open, and flushed before returning.</param>
    public static void Encode(Settings settings, Stream destination)
    {
        using var writer = new StreamWriter(destination, leaveOpen: true);
        using var json = new JsonTextWriter(writer);
        new JsonSerializer { Formatting = Formatting.Indented }.Serialize(json, settings);
        json.Flush();
        writer.Flush();
    }

    /// <summary>
    /// Reads settings from <paramref name="source"/>.
    /// </summary>
    ///
    /// <remarks>
    /// Delegates to OpenTabletDriver's own deserializer rather than configuring an equivalent one. That
    /// is the point: it carries a version converter and an error handler, and a reader assembled here to
    /// look the same would differ in ways that only show up on someone's real settings file. Reading is
    /// where being approximately compatible is most expensive, because the input was written by another
    /// program and possibly an older version of it.
    ///
    /// Only malformed content returns false. A caller that needs to tell "not there" from "not readable"
    /// has to check that before calling, because a stream cannot say which it is.
    /// </remarks>
    ///
    /// <param name="source">The bytes to read.</param>
    /// <param name="settings">The settings, or null when the content could not be read.</param>
    /// <returns>True when settings were read.</returns>
    public static bool TryDecode(Stream source, out Settings? settings)
    {
        try
        {
            settings = Serialization.Deserialize<Settings>(source);
            return settings != null;
        }
        catch (JsonException)
        {
            // Matches the upstream TryDeserialize contract, which guards this and nothing else.
            settings = null;
            return false;
        }
    }
}
