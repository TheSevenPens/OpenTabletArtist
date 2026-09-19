using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;
using OtdInterop;

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
    public void Write_ReturnsFalse_WhenTheTargetCannotBeWritten()
    {
        // The write failure this guards against is a permission denial, not a crash: Write must report
        // it rather than throw, so a preference write can't abort the caller (#735).
        File.WriteAllText(_path, "{}");
        using (UnwritablePath.For(_path))
        {
            Assert.False(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
            Assert.Equal("{}", File.ReadAllText(_path)); // untouched
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

    // --- Atomic replacement and backup recovery (#768) ---

    [Fact]
    public void FailedWrite_LeavesThePreviousPreferencesLoadable()
    {
        // The point of the atomic write: the previous file is still the previous file, not a truncated
        // one. A preference write that fails must cost the next launch nothing.
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));

        using (UnwritablePath.For(_path))
        {
            Assert.False(SettingsFile.Write(_path, new JObject { ["Theme"] = "Nope" }));
        }

        var (data, outcome) = SettingsFile.Read(_path, Stamp);
        Assert.Equal(SettingsLoadStatus.Ok, outcome.Status);
        Assert.Equal("Anime", data["Theme"]?.ToString());
    }

    [Fact]
    public void FailedWrite_LeavesNoTemporaryFilesBehind()
    {
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));

        using (UnwritablePath.For(_path))
        {
            Assert.False(SettingsFile.Write(_path, new JObject { ["Theme"] = "Nope" }));
        }

        // A stale temp file is not corruption, but it accumulates in a directory the user can see.
        Assert.Empty(Directory.GetFiles(_dir, ".*.tmp-*"));
    }

    [Fact]
    public void SuccessfulOverwrite_KeepsThePreviousVersionAsABackup()
    {
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Mono" }));

        var backup = _path + AtomicFile.BackupSuffix;
        Assert.True(File.Exists(backup));
        Assert.Equal("Anime", JObject.Parse(File.ReadAllText(backup))["Theme"]?.ToString());
    }

    [Fact]
    public void FirstWrite_CreatesNoBackup()
    {
        // Nothing was replaced, so there is nothing to keep — and an empty ".bak" would be a lie about
        // what the last good save contained.
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
        Assert.False(File.Exists(_path + AtomicFile.BackupSuffix));
    }

    [Fact]
    public void CorruptFile_RecoversFromTheBackup_RatherThanFallingBackToDefaults()
    {
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Mono" }));
        File.WriteAllText(_path, "{ this is not valid json ");

        var (data, outcome) = SettingsFile.Read(_path, Stamp);

        Assert.Equal(SettingsLoadStatus.Recovered, outcome.Status);
        Assert.Equal("Anime", data["Theme"]?.ToString());
        Assert.Equal(Path.GetFileName(_path) + AtomicFile.BackupSuffix, outcome.BackupName);

        // Recovering doesn't excuse leaving the unreadable file where the next save would clobber it.
        Assert.False(File.Exists(_path));
        Assert.Single(Directory.GetFiles(_dir, "*.corrupt-*"));
    }

    [Fact]
    public void MissingFile_WithABackupBesideIt_Recovers()
    {
        // A write interrupted between the backup being taken and the swap completing leaves exactly this:
        // no main file, a good backup. Treating it as first-run would silently reset the user.
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Anime" }));
        Assert.True(SettingsFile.Write(_path, new JObject { ["Theme"] = "Mono" }));
        File.Delete(_path);

        var (data, outcome) = SettingsFile.Read(_path, Stamp);

        Assert.Equal(SettingsLoadStatus.Recovered, outcome.Status);
        Assert.Equal("Anime", data["Theme"]?.ToString());
    }

    [Fact]
    public void CorruptFile_WithAnEquallyCorruptBackup_StillFallsBackToDefaults()
    {
        File.WriteAllText(_path, "{ nope ");
        File.WriteAllText(_path + AtomicFile.BackupSuffix, "{ also nope ");

        var (data, outcome) = SettingsFile.Read(_path, Stamp);

        Assert.Equal(SettingsLoadStatus.Preserved, outcome.Status);
        Assert.False(data.HasValues);
    }
}
