using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class SettingsFileTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    private static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5);

    public SettingsFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ota-settingsfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void MissingFile_IsOk_WithEmptyData()
    {
        var (data, outcome) = SettingsFile.Read(_path, Stamp);
        Assert.Equal(SettingsLoadStatus.Ok, outcome.Status);
        Assert.False(data.HasValues);
    }

    [Fact]
    public void ValidFile_IsOk_AndParsed()
    {
        File.WriteAllText(_path, """{ "Theme": "Anime" }""");
        var (data, outcome) = SettingsFile.Read(_path, Stamp);
        Assert.Equal(SettingsLoadStatus.Ok, outcome.Status);
        Assert.Equal("Anime", data["Theme"]?.ToString());
    }

    [Fact]
    public void CorruptFile_IsPreserved_ToTimestampedBackup_AndDefaultsReturned()
    {
        File.WriteAllText(_path, "{ this is not valid json ");
        var (data, outcome) = SettingsFile.Read(_path, Stamp);

        Assert.Equal(SettingsLoadStatus.Preserved, outcome.Status);
        Assert.False(data.HasValues); // fell back to empty defaults

        // The unreadable file was moved aside (not left where the next save would clobber it), and the
        // outcome names the backup that now exists on disk.
        Assert.False(File.Exists(_path));
        Assert.NotNull(outcome.BackupName);
        var backup = Directory.GetFiles(_dir).Single();
        Assert.Equal(outcome.BackupName, Path.GetFileName(backup));
        Assert.Contains("corrupt-20260102-030405", backup); // deterministic from the injected timestamp
        Assert.Equal("{ this is not valid json ", File.ReadAllText(backup)); // original bytes preserved
    }

    // --- Write (#735) ---

    [Fact]
    public void Write_CreatesTheFile_AndRoundTrips()
    {
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));

        var (data, outcome) = SettingsFile.Read(_path, Stamp);
        Assert.Equal(SettingsLoadStatus.Ok, outcome.Status);
        Assert.Equal("Anime", data["Theme"]?.ToString());
    }

    [Fact]
    public void Write_CreatesTheDirectory_WhenMissing()
    {
        var nested = Path.Combine(_dir, "sub", "dir", "settings.json");
        Assert.True(SettingsFile.Write(nested, new JObject { ["Theme"] = "Anime" }));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Write_ReturnsFalse_WhenTheTargetIsReadOnly()
    {
        // The write failure this guards against is a permission denial, not a crash: Write must report
        // it rather than throw, so a preference write can't abort the caller (#735).
        File.WriteAllText(_path, "{}");
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        try
        {
            Assert.False(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
            Assert.Equal("{}", File.ReadAllText(_path)); // untouched
        }
        finally
        {
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Write_ReturnsFalse_WhenThePathIsNotWritable()
    {
        // A path whose parent is an existing *file* can never be created — a stand-in for any
        // unwritable destination that needs reporting rather than an exception.
        var blocked = Path.Combine(_path, "nested", "settings.json");
        File.WriteAllText(_path, "{}");
        Assert.False(SettingsFile.Write(blocked, new JObject()));
    }
}
