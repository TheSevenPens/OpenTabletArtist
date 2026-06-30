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
}
