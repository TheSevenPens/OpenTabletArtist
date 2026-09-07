using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The hotkey reconcile rule (#hotkey-orphans). <see cref="ProfileHotkeyManager.Sync"/> itself needs
/// Win32 and the settings file, so the decision it makes is a pure function and this tests that.
/// </summary>
public class ProfileHotkeyReconcileTests
{
    private const string Prefix = ProfileHotkeyManager.MappingKeyPrefix;

    [Fact]
    public void Orphans_DropsAMappingWhosePresetIsGone()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            [Prefix + "Deleted"], ["Alpha"]);

        Assert.Equal([Prefix + "Deleted"], orphans);
    }

    [Fact]
    public void Orphans_KeepsAMappingWhosePresetStillExists()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            [Prefix + "Alpha"], ["Alpha", "Beta"]);

        Assert.Empty(orphans);
    }

    /// <summary>
    /// The regression this whole change exists to prevent: the display-toggle hotkey is persisted under
    /// the same "Hotkey:" prefix and has no preset behind it, so a naive prefix sweep would delete it.
    /// </summary>
    [Fact]
    public void Orphans_NeverReapsTheMonitorCycleHotkey()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            [MonitorCycleHotkeys.MapKey], []);

        Assert.Empty(orphans);
    }

    [Fact]
    public void Orphans_MatchesPresetNamesCaseInsensitively()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            [Prefix + "csp preset"], ["CSP Preset"]);

        Assert.Empty(orphans);
    }

    [Fact]
    public void Orphans_IgnoresKeysThatArentHotkeyMappings()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            ["Theme", "Developer:ShowJson"], []);

        Assert.Empty(orphans);
    }

    /// <summary>A bare "Hotkey:" with no name behind it maps to no preset and is not a mapping; leave it
    /// rather than removing a key this code doesn't own.</summary>
    [Fact]
    public void Orphans_IgnoresThePrefixOnItsOwn()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys([Prefix], []);

        Assert.Empty(orphans);
    }

    [Fact]
    public void Orphans_ReapsEveryDeadMappingAtOnce()
    {
        var orphans = ProfileHotkeyManager.OrphanedMappingKeys(
            [Prefix + "Gone1", Prefix + "Alpha", MonitorCycleHotkeys.MapKey, Prefix + "Gone2"],
            ["Alpha"]);

        Assert.Equal([Prefix + "Gone1", Prefix + "Gone2"], orphans);
    }
}
