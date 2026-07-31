using System;
using System.IO;
using System.Linq;
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
}
