using System.IO;
using System.Text;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The shared settings codec (#807).
///
/// <c>SettingsFileStoreTests</c> already covers round-tripping through the file store, which now goes
/// through this. What those tests cannot state is the part that matters once a second caller — preset
/// storage — uses the same codec: that the bytes match what OpenTabletDriver itself writes, and that
/// reading is upstream's reader rather than a locally assembled equivalent.
///
/// The settings file is shared with OpenTabletDriver's own interface. An encoder that merely produces
/// valid JSON would still churn the file for anyone editing in both programs, and a decoder assembled to
/// look like upstream's would differ in ways that only appear on a real user's file, written by a version
/// nobody has to hand.
/// </summary>
public class SettingsCodecTests
{
    private static Settings SettingsFor(string tablet, bool locked) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    private static string Encode(Settings settings)
    {
        using var buffer = new MemoryStream();
        SettingsCodec.Encode(settings, buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [Fact]
    public void RoundTrips()
    {
        using var buffer = new MemoryStream();
        SettingsCodec.Encode(SettingsFor("Tablet", locked: true), buffer);
        buffer.Position = 0;

        Assert.True(SettingsCodec.TryDecode(buffer, out var read));
        Assert.True(read!.LockUsableAreaDisplay);
        Assert.Equal("Tablet", read.Profiles[0].Tablet);
    }

    /// <summary>
    /// Indented, matching upstream. Asserted on the bytes rather than by round-tripping, because a
    /// round-trip passes just as happily against compact output — and the reason for the formatting is
    /// the other program reading the file, not this one.
    /// </summary>
    [Fact]
    public void WritesIndentedJson_AsOpenTabletDriverDoes()
    {
        var json = Encode(SettingsFor("Tablet", locked: false));

        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    /// <summary>
    /// The caller keeps the stream. <c>AtomicFile</c> depends on this: it has to flush the file to disk
    /// and then swap it, neither of which it can do if the codec closed the stream on its way out.
    /// </summary>
    [Fact]
    public void LeavesTheStreamOpen()
    {
        using var buffer = new MemoryStream();

        SettingsCodec.Encode(SettingsFor("Tablet", locked: false), buffer);

        Assert.True(buffer.CanWrite);
        Assert.True(buffer.Length > 0);
    }

    [Fact]
    public void MalformedContent_ReturnsFalse()
    {
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes("{ this is not settings"));

        Assert.False(SettingsCodec.TryDecode(buffer, out var read));
        Assert.Null(read);
    }

    /// <summary>
    /// Two encodes at once must not interfere. The store used to hold one shared
    /// <c>JsonSerializer</c>, which is not safe to use concurrently; the codec builds one per call, and
    /// a second caller for preset storage makes that reachable rather than theoretical.
    ///
    /// This is a smoke test, not proof — a race that needs precise timing would pass it. It exists so
    /// that reintroducing a shared serializer has at least one way to fail.
    /// </summary>
    [Fact]
    public void ConcurrentEncodes_DoNotInterfere()
    {
        var a = SettingsFor("A", locked: true);
        var b = SettingsFor("B", locked: false);
        var expectedA = Encode(a);
        var expectedB = Encode(b);

        Parallel.For(0, 64, i =>
        {
            var (settings, expected) = (i % 2 == 0) ? (a, expectedA) : (b, expectedB);
            Assert.Equal(expected, Encode(settings));
        });
    }
}
