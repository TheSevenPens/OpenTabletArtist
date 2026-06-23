using System.IO;
using OtdWindowsHelper.Domain;
using Xunit;

namespace OtdWindowsHelper.Tests;

public class TabletConfigNamingTests
{
    private static string PathIn(string folder, string file) =>
        Path.Combine("C:", "configs", folder, file);

    [Fact]
    public void JsonNameField_IsPreferred()
    {
        var path = PathIn("Wacom", "ctl672.json");
        Assert.Equal("Huion H640P", TabletConfigNaming.FriendlyName(path, "{\"Name\":\"Huion H640P\"}"));
    }

    [Fact]
    public void ManufacturerAndModel_UsedWhenNoName()
    {
        var path = PathIn("Wacom", "ctl672.json");
        Assert.Equal("Wacom CTL-672", TabletConfigNaming.FriendlyName(path, "{\"Manufacturer\":\"Wacom\",\"Model\":\"CTL-672\"}"));
    }

    [Fact]
    public void Vendor_IsUsedAsManufacturerFallback()
    {
        var path = PathIn("Huion", "h640p.json");
        Assert.Equal("Huion H640P", TabletConfigNaming.FriendlyName(path, "{\"Vendor\":\"Huion\",\"Model\":\"H640P\"}"));
    }

    [Fact]
    public void NoUsableFields_FallsBackToParentPlusStem()
    {
        var path = PathIn("Huion", "h640p.json");
        Assert.Equal("Huion h640p", TabletConfigNaming.FriendlyName(path, "{\"Something\":1}"));
    }

    [Fact]
    public void ConfigurationsParent_FallsBackToBareStem()
    {
        var path = PathIn("Configurations", "h640p.json");
        Assert.Equal("h640p", TabletConfigNaming.FriendlyName(path, "{\"Something\":1}"));
    }

    [Fact]
    public void NullContent_ReturnsBareStem()
    {
        var path = PathIn("Huion", "h640p.json");
        Assert.Equal("h640p", TabletConfigNaming.FriendlyName(path, null));
    }

    [Fact]
    public void InvalidJson_ReturnsBareStem_NotParentPrefixed()
    {
        // Parse failure keeps the bare filename (the parent-prefix only applies to parsed-but-empty).
        var path = PathIn("Huion", "h640p.json");
        Assert.Equal("h640p", TabletConfigNaming.FriendlyName(path, "not valid json"));
    }

    [Fact]
    public void BomPrefixedJson_IsParsed()
    {
        var path = PathIn("Wacom", "ctl672.json");
        var withBom = "﻿{\"Name\":\"Wacom CTL-672\"}";
        Assert.Equal("Wacom CTL-672", TabletConfigNaming.FriendlyName(path, withBom));
    }
}
