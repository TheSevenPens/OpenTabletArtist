using System;
using System.IO;
using OpenTabletDriver.Desktop;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

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

    [Fact]
    public void TrySave_InvalidPath_ReturnsFalse()
    {
        var store = new SettingsFileStore();
        // Directory does not exist → write fails → best-effort returns false.
        var path = Path.Combine(Path.GetTempPath(), $"otd_missing_{Guid.NewGuid():N}", "settings.json");
        Assert.False(store.TrySave(new Settings(), path));
    }

    [Fact]
    public void Save_InvalidPath_Throws()
    {
        var store = new SettingsFileStore();
        var path = Path.Combine(Path.GetTempPath(), $"otd_missing_{Guid.NewGuid():N}", "settings.json");
        Assert.ThrowsAny<Exception>(() => store.Save(new Settings(), path));
    }
}
