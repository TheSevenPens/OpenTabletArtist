using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace OtdHealth.Tests;

public class BoundaryDependencyTests
{
    [Fact]
    public void EvaluatorAssemblyDependsOnlyOnTheFramework()
    {
        Assert.All(typeof(HealthEvaluator).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name == "System" || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
                $"OtdHealth must be independent of OTA, OTD, and UI dependencies: {reference.Name}"));
    }

    [Fact]
    public void ProjectAndTestGraphCannotBringInTheApplication()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "../.."));
        var library = XDocument.Load(Path.Combine(root, "OtdHealth/OtdHealth.csproj"));
        Assert.Empty(library.Descendants("ProjectReference"));
        Assert.Empty(library.Descendants("PackageReference"));

        var tests = XDocument.Load(Path.Combine(root, "tests/OtdHealth.Tests/OtdHealth.Tests.csproj"));
        Assert.Equal("../../OtdHealth/OtdHealth.csproj", Assert.Single(tests.Descendants("ProjectReference")).Attribute("Include")!.Value);
        Assert.DoesNotContain(typeof(BoundaryDependencyTests).Assembly.GetReferencedAssemblies(), a =>
            a.Name!.StartsWith("OpenTabletArtist", StringComparison.Ordinal) || a.Name.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishedCodesAreDistinct()
    {
        var codes = typeof(HealthCheckCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
