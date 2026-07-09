using System.IO;
using System.Linq;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class TabletConfigInspectorTests
{
    [Fact]
    public void BaseConfigNames_AreLoadedFromTheBundledDaemon()
    {
        // The embedded config set is non-trivial; a specific well-known Wacom is present.
        var names = TabletConfigInspector.BaseConfigNames;
        Assert.True(names.Count > 50);
        Assert.Contains(names, n => n.Contains("Wacom"));
    }

    [Fact]
    public void OverriddenBaseNames_FlagsAFileThatShadowsABaseConfig_ButNotANovelOne()
    {
        var baseName = TabletConfigInspector.BaseConfigNames.First();
        var dir = Path.Combine(Path.GetTempPath(), "ota-cfg-inspector-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A file whose Name matches a built-in → an override.
            File.WriteAllText(Path.Combine(dir, "override.json"), $"{{ \"Name\": \"{baseName}\" }}");
            // A file for a tablet OTD doesn't ship → a legitimate custom config, not an override.
            File.WriteAllText(Path.Combine(dir, "novel.json"), "{ \"Name\": \"__Not A Real Tablet__\" }");
            // Not a config at all → ignored.
            File.WriteAllText(Path.Combine(dir, "junk.json"), "not json");

            var overridden = TabletConfigInspector.OverriddenBaseNames(dir);

            Assert.Contains(baseName, overridden);
            Assert.DoesNotContain("__Not A Real Tablet__", overridden);
            Assert.Single(overridden);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void OverriddenBaseNames_EmptyOrMissingDir_IsEmpty()
    {
        Assert.Empty(TabletConfigInspector.OverriddenBaseNames(""));
        Assert.Empty(TabletConfigInspector.OverriddenBaseNames(Path.Combine(Path.GetTempPath(), "ota-does-not-exist-xyz")));
    }

    [Fact]
    public void PathKey_CollapsesRepoPathAndMangledResourceNameToTheSameKey()
    {
        // A repo path (slashes, hyphen, space) and the embedded resource name (dots, underscore) for the
        // same config must produce the same key, so browse-time diffing matches them (#480).
        var repo = "OpenTabletDriver.Configurations/Configurations/Wacom/Intuos Pro/PTH-660.json";
        var resource = "OpenTabletDriver.Configurations.Configurations.Wacom.Intuos_Pro.PTH-660.json";
        Assert.Equal(TabletConfigInspector.PathKey(resource), TabletConfigInspector.PathKey(repo));
    }

    [Fact]
    public void BaseConfigKeys_AreLoaded()
    {
        Assert.True(TabletConfigInspector.BaseConfigKeys.Count > 50);
    }
}
