using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Serializes every test class that touches the global display-enumeration seam (#729).
///
/// <c>DisplayEnumerator.Use()</c> installs a process-wide override, and xUnit runs test <b>classes</b> in
/// parallel — so a class that installs a fake races any class that enumerates while it is installed. Both
/// sides can lose: the seam test sees its fake called more times than it called it, and the other class
/// gets the fake's monitors instead of the machine's.
///
/// Note this is not a race <em>within</em> a class. Tests in one class already run sequentially, so
/// putting a collection on the seam test alone — the obvious reading of the flake — would change nothing.
/// The other party has to join too.
///
/// <b>If you write a test that constructs <c>TabletDetailViewModel</c>, put its class in this
/// collection.</b> That constructor calls <c>DisplayEnumerator.Enumerate()</c>, which is easy to miss
/// because nothing in the test mentions displays. The durable fix is for the view model to take an
/// injected enumerator instead of reading a global — noted on #751, which would be doing that work anyway.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DisplayEnumeratorCollection
{
    public const string Name = "DisplayEnumerator";
}
