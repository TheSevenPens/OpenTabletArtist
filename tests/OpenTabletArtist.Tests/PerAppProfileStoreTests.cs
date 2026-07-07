using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class PerAppProfileStoreTests
{
    // In-memory persistence so the store is tested off-disk.
    private sealed class Mem
    {
        public string? Value;
        public PerAppProfileStore Store() => new(() => Value, v => Value = v);
    }

    private static AppIdentity App(string name, string path = "") => new(path, name);

    [Fact]
    public void Resolve_PrefersExactPath_ThenName_ThenDefault()
    {
        var mem = new Mem();
        var s = mem.Store();
        s.SetDefaultSnapshot("Fallback");
        s.Upsert(new PerAppMapping(@"C:\Apps\krita.exe", "krita.exe", "ByPath"));
        s.Upsert(new PerAppMapping("", "chrome.exe", "ByName"));

        Assert.Equal("ByPath", s.Resolve(App("krita.exe", @"C:\Apps\krita.exe")));
        Assert.Equal("ByName", s.Resolve(App("chrome.exe", @"C:\Other\chrome.exe")));
        Assert.Equal("Fallback", s.Resolve(App("notepad.exe")));   // unmapped → default
    }

    [Fact]
    public void Resolve_MatchedCurrentSettingsMapping_WinsOverDefault()
    {
        var s = new Mem().Store();
        s.SetDefaultSnapshot("Fallback");
        s.Upsert(new PerAppMapping("", "krita.exe", SnapshotName: null)); // explicitly "Current settings"

        // A matched mapping targeting Current settings (null) must win over the default, not fall through.
        Assert.Null(s.Resolve(App("krita.exe")));
        Assert.Equal("Fallback", s.Resolve(App("other.exe"))); // unmapped still uses the default
    }

    [Fact]
    public void Resolve_NullWhenNoMatchAndNoDefault()
    {
        var s = new Mem().Store();
        s.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        Assert.Null(s.Resolve(App("random.exe")));
    }

    [Fact]
    public void Resolve_IgnoresDisabledMapping()
    {
        var s = new Mem().Store();
        s.Upsert(new PerAppMapping("", "krita.exe", "Painting", Enabled: false));
        Assert.Null(s.Resolve(App("krita.exe")));
    }

    [Fact]
    public void Persistence_RoundTrips()
    {
        var mem = new Mem();
        var a = mem.Store();
        a.SetDefaultSnapshot("Def");
        a.Upsert(new PerAppMapping("", "krita.exe", "Painting"));

        var b = mem.Store(); // reads the same backing string
        Assert.Equal("Def", b.Config.DefaultSnapshot);
        Assert.Equal("Painting", b.Resolve(App("krita.exe")));
    }

    // Implicit enable: the feature is on only when a mapping targets a real profile. A mapping to Current
    // settings (null snapshot) is a no-op, and a lone default doesn't count.
    [Fact]
    public void HasActiveMappings_OnlyWhenAMappingTargetsARealProfile()
    {
        var s = new Mem().Store();
        Assert.False(s.HasActiveMappings);                                    // empty

        s.SetDefaultSnapshot("Painting");
        Assert.False(s.HasActiveMappings);                                    // default alone doesn't arm

        s.Upsert(new PerAppMapping("", "krita.exe", null));                   // → Current settings
        Assert.False(s.HasActiveMappings);                                    // no-op mapping doesn't arm

        s.Upsert(new PerAppMapping("", "krita.exe", "Painting"));             // → a real profile
        Assert.True(s.HasActiveMappings);

        s.Upsert(new PerAppMapping("", "krita.exe", "Painting", Enabled: false)); // disabled row
        Assert.False(s.HasActiveMappings);
    }

    [Fact]
    public void Upsert_ReplacesByExeName()
    {
        var s = new Mem().Store();
        s.Upsert(new PerAppMapping("", "krita.exe", "Old"));
        s.Upsert(new PerAppMapping("", "krita.exe", "New"));
        Assert.Single(s.Config.Mappings);
        Assert.Equal("New", s.Resolve(App("krita.exe")));
    }

    [Fact]
    public void Remove_DropsMapping()
    {
        var s = new Mem().Store();
        s.Upsert(new PerAppMapping("", "krita.exe", "Painting"));
        s.Remove("krita.exe");
        Assert.Empty(s.Config.Mappings);
    }

    [Fact]
    public void RenameSnapshotReferences_UpdatesDefaultAndMappings()
    {
        var s = new Mem().Store();
        s.SetDefaultSnapshot("Base");
        s.Upsert(new PerAppMapping("", "krita.exe", "Base"));
        s.Upsert(new PerAppMapping("", "chrome.exe", "Web"));

        s.RenameSnapshotReferences("Base", "Baseline");

        Assert.Equal("Baseline", s.Config.DefaultSnapshot);
        Assert.Equal("Baseline", s.Resolve(App("krita.exe")));
        Assert.Equal("Web", s.Resolve(App("chrome.exe")));  // untouched
    }
}
