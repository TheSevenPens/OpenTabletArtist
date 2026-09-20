using System.IO;
using OpenTabletArtist.Services;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;
using OtdInterop.Tests;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The app's preset store (#807).
///
/// Not covered by the shared codec's tests: those exercise encoding and decoding, and this type's own
/// behaviour is the orchestration around them — writing without leaving a half-written file, and falling
/// back to the last-known-good copy when the preset itself will not read. It is a new implementation
/// rather than a moved one, so it needs its own coverage even though it preserves what the store it
/// replaced did for presets.
/// </summary>
public class PresetStoreTests
{
    private static PresetStore Store() => new(NullOtdLog.Instance);

    private static Settings SettingsFor(string tablet, bool locked) => new()
    {
        LockUsableAreaDisplay = locked,
        Profiles = new ProfileCollection { new Profile { Tablet = tablet } },
    };

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        using var temp = new TempDir();
        var path = temp.File("preset.json");
        Store().Save(SettingsFor("Tablet", locked: true), path);

        Assert.True(Store().TryLoad(path, out var read));
        Assert.True(read!.LockUsableAreaDisplay);
        Assert.Equal("Tablet", read.Profiles[0].Tablet);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsFalse()
    {
        using var temp = new TempDir();

        Assert.False(Store().TryLoad(temp.File("absent.json"), out var read));
        Assert.Null(read);
    }

    [Fact]
    public void TryLoad_GarbageFile_ReturnsFalse()
    {
        using var temp = new TempDir();
        var path = temp.File("preset.json");
        File.WriteAllText(path, "{ not a preset");

        Assert.False(Store().TryLoad(path, out var read));
        Assert.Null(read);
    }

    /// <summary>
    /// The fallback is the reason this type does its own orchestration rather than just calling the
    /// codec. A preset that will not read is not necessarily lost: a completed earlier write left a copy.
    /// </summary>
    [Fact]
    public void AnUnreadablePreset_RecoversTheLastGoodCopy()
    {
        using var temp = new TempDir();
        var path = temp.File("preset.json");

        Store().Save(SettingsFor("First", locked: true), path);
        Store().Save(SettingsFor("Second", locked: false), path);   // leaves the first as the backup
        File.WriteAllText(path, "corrupted");

        Assert.True(Store().TryLoad(path, out var read));
        Assert.Equal("First", read!.Profiles[0].Tablet);
    }

    /// <summary>The control: with nothing to fall back to, an unreadable preset is simply unreadable.</summary>
    [Fact]
    public void WithNoBackup_AnUnreadablePresetStaysUnreadable()
    {
        using var temp = new TempDir();
        var path = temp.File("preset.json");
        File.WriteAllText(path, "corrupted");

        Assert.False(Store().TryLoad(path, out _));
    }

    /// <summary>
    /// A failed write must not destroy the preset that was there. Upstream's own serializer deletes
    /// before it creates, which is the behaviour this store exists to avoid inheriting.
    /// </summary>
    [Fact]
    public void AFailedWrite_LeavesThePreviousPresetLoadable()
    {
        using var temp = new TempDir();
        var path = temp.File("preset.json");
        Store().Save(SettingsFor("Kept", locked: true), path);

        // ThrowsAny<Exception>, matching the store tests next door: an unwritable target surfaces as
        // UnauthorizedAccessException on some platforms and IOException on others, and which one it is
        // is not what this test is about.
        using (UnwritablePath.For(path))
            Assert.ThrowsAny<System.Exception>(() => { Store().Save(SettingsFor("Never", locked: false), path); });

        Assert.True(Store().TryLoad(path, out var read));
        Assert.Equal("Kept", read!.Profiles[0].Tablet);
    }

    /// <summary>A scratch directory, removed with the test.</summary>
    private sealed class TempDir : System.IDisposable
    {
        private readonly string _path;

        public TempDir()
        {
            _path = Path.Combine(Path.GetTempPath(), $"otdpreset_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_path);
        }

        public string File(string name) => Path.Combine(_path, name);

        public void Dispose()
        {
            try { Directory.Delete(_path, recursive: true); } catch { /* best effort */ }
        }
    }
}
