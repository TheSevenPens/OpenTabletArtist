using System.IO;
using OtdWindowsHelper.Services;
using Xunit;

namespace OtdWindowsHelper.Tests;

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
