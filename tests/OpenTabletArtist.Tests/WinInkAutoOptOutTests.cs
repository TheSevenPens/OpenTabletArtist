using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Exercises the opt-out set over an in-memory store (#738). This used to round-trip through the static
/// <c>AppSettings</c>, so running the suite wrote into the developer's real preference file under
/// <c>%LOCALAPPDATA%</c> — it failed where that path was unwritable, and mutated a real artist setup
/// where it wasn't.
/// </summary>
public class WinInkAutoOptOutTests
{
    /// <summary>The stored value, as the preference file would hold it.</summary>
    private sealed class FakeStore
    {
        public string? Value;
        public WinInkOptOutSet Set() => new(() => Value, v => Value = v);
    }

    [Fact]
    public void OptOut_AndClear_RoundTrip()
    {
        var set = new FakeStore().Set();
        const string tablet = "Test Tablet OptOut";

        set.OptOut(tablet);
        Assert.True(set.IsOptedOut(tablet));

        set.Clear(tablet);
        Assert.False(set.IsOptedOut(tablet));
    }

    [Fact]
    public void UnknownTablet_IsNotOptedOut()
    {
        Assert.False(new FakeStore().Set().IsOptedOut("Never Seen"));
    }

    [Fact]
    public void OptOut_KeepsOtherTablets()
    {
        var set = new FakeStore().Set();

        set.OptOut("One");
        set.OptOut("Two");
        set.Clear("One");

        Assert.False(set.IsOptedOut("One"));
        Assert.True(set.IsOptedOut("Two"));
    }

    /// <summary>Detection is case-insensitive, so the opt-out set has to be too — the daemon's reported
    /// casing can differ from the profile's.</summary>
    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var set = new FakeStore().Set();

        set.OptOut("Wacom Intuos");

        Assert.True(set.IsOptedOut("wacom intuos"));
    }

    [Fact]
    public void OptOut_IsIdempotent_AndWritesOnlyWhenTheSetChanges()
    {
        var store = new FakeStore();
        var set = store.Set();

        set.OptOut("One");
        var afterFirst = store.Value;
        set.OptOut("One");

        Assert.Equal(afterFirst, store.Value);
        Assert.True(set.IsOptedOut("One"));
    }

    [Fact]
    public void EmptyTabletName_IsIgnored()
    {
        var store = new FakeStore();
        var set = store.Set();

        set.OptOut("");
        set.Clear("");

        Assert.Null(store.Value);          // nothing written
        Assert.False(set.IsOptedOut(""));
    }
}
