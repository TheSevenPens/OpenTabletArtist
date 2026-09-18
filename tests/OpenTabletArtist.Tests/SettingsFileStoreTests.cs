using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class SettingsFileStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"otdtest_{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var store = new SettingsFileStore();
        var path = TempPath();
        try
        {
            var settings = new Settings { LockUsableAreaDisplay = true, LockUsableAreaTablet = false };

            Assert.True(store.TrySave(settings, path));
            Assert.True(File.Exists(path));

            Assert.True(store.TryLoad(path, out var loaded));
            Assert.NotNull(loaded);
            Assert.True(loaded!.LockUsableAreaDisplay);
            Assert.False(loaded.LockUsableAreaTablet);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsFalse()
    {
        var store = new SettingsFileStore();
        Assert.False(store.TryLoad(TempPath(), out var loaded));
        Assert.Null(loaded);
    }

    [Fact]
    public void TryLoad_GarbageFile_ReturnsFalse()
    {
        var store = new SettingsFileStore();
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "this is not valid json");
            Assert.False(store.TryLoad(path, out var loaded));
            Assert.Null(loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>A missing directory is now created rather than failing the save (#732). Refusing to save
    /// because a folder doesn't exist yet is the silent-loss behaviour we're removing, not a safeguard.</summary>
    [Fact]
    public void TrySave_MissingDirectory_CreatesItAndSaves()
    {
        var store = new SettingsFileStore();
        var dir = Path.Combine(Path.GetTempPath(), $"otd_missing_{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "settings.json");
        try
        {
            Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = true }, path));
            Assert.True(store.TryLoad(path, out var loaded));
            Assert.True(loaded!.LockUsableAreaDisplay);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TrySave_UnwritablePath_ReturnsFalse()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        Assert.False(store.TrySave(new Settings(), temp.BlockedPath));
    }

    [Fact]
    public void Save_UnwritablePath_Throws()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        Assert.ThrowsAny<Exception>(() => store.Save(new Settings(), temp.BlockedPath));
    }

    // --- Truthful outcomes (#732) ---

    /// <summary>
    /// The core regression. Upstream's <c>Settings.Serialize</c> catches
    /// <see cref="UnauthorizedAccessException"/> and only logs it, so a refused write came back as a
    /// completed one: the artist saw "Saved", restarted, and the change was gone.
    /// </summary>
    [Fact]
    public void TrySave_ReadOnlyTarget_ReturnsFalse()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = true }, path));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(store.TrySave(new Settings { LockUsableAreaDisplay = false }, path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public void Save_ReadOnlyTarget_Throws()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        store.Save(new Settings(), path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => store.Save(new Settings(), path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    // --- Durability (#733) ---

    /// <summary>
    /// Fault injection: the serialize succeeds, the swap is refused. The previous settings must still be
    /// on disk and loadable — upstream deleted the file before creating the replacement, so this same
    /// failure could leave no settings file at all.
    /// </summary>
    [Fact]
    public void FailedWrite_LeavesThePreviousSettingsLoadable()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = true }, path));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(store.TrySave(new Settings { LockUsableAreaDisplay = false }, path));

            Assert.True(File.Exists(path));
            Assert.True(store.TryLoad(path, out var loaded));
            Assert.True(loaded!.LockUsableAreaDisplay); // the OLD value, intact
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public void FailedWrite_LeavesNoTemporaryFilesBehind()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings(), path));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            store.TrySave(new Settings(), path);
            Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*"));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact]
    public void SuccessfulOverwrite_KeepsThePreviousVersionAsABackup()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = true }, path));
        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = false }, path));

        Assert.True(store.TryLoad(path + SettingsFileStore.BackupSuffix, out var backup));
        Assert.True(backup!.LockUsableAreaDisplay); // the version we just replaced
    }

    /// <summary>A first write has nothing to preserve, so it must not invent an empty backup that a
    /// later recovery would mistake for a known-good file.</summary>
    [Fact]
    public void FirstWrite_CreatesNoBackup()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings(), path));

        Assert.False(File.Exists(path + SettingsFileStore.BackupSuffix));
    }

    [Fact]
    public void TryLoad_CorruptFile_RecoversFromTheBackup()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");

        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = true }, path));
        Assert.True(store.TrySave(new Settings { LockUsableAreaDisplay = false }, path));

        // Something truncated the live file after the last good save.
        File.WriteAllText(path, "{ truncated");

        Assert.True(store.TryLoad(path, out var loaded));
        Assert.True(loaded!.LockUsableAreaDisplay);
    }

    [Fact]
    public void TryLoad_CorruptFile_WithNoBackup_StillReturnsFalse()
    {
        var store = new SettingsFileStore();
        using var temp = new TempDir();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "{ truncated");

        Assert.False(store.TryLoad(path, out var loaded));
        Assert.Null(loaded);
    }

    /// <summary>A scratch directory plus the two paths these tests need: a real file inside it, and a
    /// path that can never be created (its parent is an existing file).</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"otdstore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public string BlockedPath
        {
            get
            {
                var blocker = File("blocker");
                if (!System.IO.File.Exists(blocker)) System.IO.File.WriteAllText(blocker, "x");
                return System.IO.Path.Combine(blocker, "nested", "settings.json");
            }
        }

        public void Dispose()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path))
                    System.IO.File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Path, recursive: true);
            }
            catch { /* best-effort temp cleanup */ }
        }
    }
}
