using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class ApprovedConfigsServiceTests
{
    private const string Prefix = "OpenTabletDriver.Configurations/Configurations/";

    private static string Tree(params string[] paths)
    {
        var blobs = string.Join(",", paths.Select(p => $"{{ \"path\": \"{p}\", \"type\": \"blob\" }}"));
        return $"{{ \"tree\": [ {blobs} ] }}";
    }

    [Fact]
    public void ParseConfigPaths_ReturnsOnlyConfigJsonBlobs()
    {
        var json = Tree(
            Prefix + "Acme/AcmeOne.json",
            "README.md",                                   // not under Configurations
            Prefix + "Acme/notes.txt",                     // not json
            Prefix + "Wacom/PTH-660.json");
        // A tree (folder) node must be ignored even under the prefix.
        json = json.Replace("] }", $", {{ \"path\": \"{Prefix}Acme\", \"type\": \"tree\" }} ] }}");

        var paths = ApprovedConfigsService.ParseConfigPaths(json);

        Assert.Equal(2, paths.Count);
        Assert.Contains(Prefix + "Acme/AcmeOne.json", paths);
        Assert.Contains(Prefix + "Wacom/PTH-660.json", paths);
    }

    [Fact]
    public void ToApprovedConfig_DerivesManufacturerFileNameAndDisplay()
    {
        var c = ApprovedConfigsService.ToApprovedConfig(Prefix + "XP-Pen/Deco 01.json");
        Assert.Equal("XP-Pen", c.Manufacturer);
        Assert.Equal("Deco 01.json", c.FileName);
        Assert.Equal("XP-Pen Deco 01", c.DisplayName);
    }

    [Fact]
    public async Task ListAvailableAsync_ExcludesInstalledFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ota-approved-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "AcmeTwo.json"), "{ \"Name\": \"Acme Two\" }");
            var svc = new ApprovedConfigsService(_ => Task.FromResult(Tree(
                Prefix + "Acme/AcmeOne.json",
                Prefix + "Acme/AcmeTwo.json")));   // AcmeTwo already installed → excluded

            var available = await svc.ListAvailableAsync(dir);

            var one = Assert.Single(available);
            Assert.Equal("AcmeOne.json", one.FileName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InstallAsync_WritesNovelConfig_ButRejectsAlreadySupported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ota-install-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var novel = ApprovedConfigsService.ToApprovedConfig(Prefix + "Acme/AcmeOne.json");
            var svcNovel = new ApprovedConfigsService(_ => Task.FromResult("{ \"Name\": \"__Acme One__\" }"));
            var err = await svcNovel.InstallAsync(novel, dir);
            Assert.Null(err);
            Assert.True(File.Exists(Path.Combine(dir, "AcmeOne.json")));

            // A config whose Name is already in the base set must be refused (authoritative check).
            var baseName = TabletConfigInspector.BaseConfigNames.First();
            var dupe = ApprovedConfigsService.ToApprovedConfig(Prefix + "Acme/Dupe.json");
            var svcDupe = new ApprovedConfigsService(_ => Task.FromResult($"{{ \"Name\": \"{baseName}\" }}"));
            var err2 = await svcDupe.InstallAsync(dupe, dir);
            Assert.NotNull(err2);
            Assert.False(File.Exists(Path.Combine(dir, "Dupe.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
