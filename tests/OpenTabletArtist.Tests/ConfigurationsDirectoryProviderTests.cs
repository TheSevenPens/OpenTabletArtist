using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ConfigurationsDirectoryProviderTests
{
    [Fact]
    public void GetOrCreate_ReturnsOtdConfigurationsPath()
    {
        // Creation is best-effort (swallowed), so this returns the path even where the
        // directory can't be created — keeping it safe in restricted/sandbox profiles.
        var dir = new ConfigurationsDirectoryProvider().GetOrCreate();

        Assert.EndsWith(Path.Combine("OpenTabletDriver", "Configurations"), dir);
    }

    [Fact]
    public void GetOrCreate_PrefersDaemonDirectory_WhenProvided()
    {
        // The daemon's real folder (from AppInfo) wins over the fallback heuristic (#480/#467).
        var daemonDir = Path.Combine(Path.GetTempPath(), "ota-daemon-cfg-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var dir = new ConfigurationsDirectoryProvider(() => daemonDir).GetOrCreate();
            Assert.Equal(daemonDir, dir);
        }
        finally { if (Directory.Exists(daemonDir)) Directory.Delete(daemonDir, recursive: true); }
    }
}
