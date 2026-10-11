namespace OtdHealth.Collector.Tests;

public class DriverStorePackagesTests
{
    // The two packages found on a machine OTD warned about: provider "Wacom Technology" in both.
    private static readonly DriverStorePackage Wusd = new("oem92.inf", "wacom_wusd.inf", "Wacom Technology");
    private static readonly DriverStorePackage Helper = new("oem70.inf", "wacomwuhaservicecom.inf", "Wacom Technology");

    [Fact]
    public void FindsEveryWacomPackage_ByProviderOrByInfName()
    {
        var byProviderOnly = new DriverStorePackage("oem11.inf", "tablet.inf", "WACOM Co., Ltd.");
        var byInfOnly = new DriverStorePackage("oem12.inf", "Wacom_Filter.inf", "Some Vendor");
        var found = DriverStorePackages.Matching([Wusd, byProviderOnly, byInfOnly, Helper], DriverStorePackages.Wacom);
        Assert.Equal(["oem11.inf", "oem12.inf", "oem70.inf", "oem92.inf"], found.Select(p => p.PublishedName));
    }

    [Fact]
    public void LeavesOutPackagesThatAreNotWacom()
    {
        var found = DriverStorePackages.Matching(
            [new("oem17.inf", "vmulti.inf", "Pentablet HID 1.1"), new("oem95.inf", "ftdibus.inf", "FTDI"), Wusd],
            DriverStorePackages.Wacom);
        Assert.Equal(Wusd, Assert.Single(found));
    }

    // pnputil -e, which OTD reads, lists only third-party packages. A Windows package that merely mentions the
    // word must not show up as something OTD matched.
    [Fact]
    public void LeavesOutWindowsOwnPackages()
    {
        var inbox = new DriverStorePackage("hidusb.inf", "hidusb.inf", "Microsoft (Wacom-compatible)");
        Assert.Empty(DriverStorePackages.Matching([inbox], DriverStorePackages.Wacom));
    }

    [Fact]
    public void NothingInTheStore_IsAnEmptyList_NotAnError()
    {
        Assert.Empty(DriverStorePackages.Matching([], DriverStorePackages.Wacom));
    }

    // The one test that touches the real registry. It asserts the shape of what Windows returns rather than
    // any particular driver, so it holds on every Windows machine.
    [Fact]
    public void TheRealDriverStore_ReadsWithoutElevation_AndHasThirdPartyPackages()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The driver store is a Windows feature.");
        var all = DriverStorePackages.ReadAll();
        Assert.NotEmpty(all);
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.PublishedName)));
        Assert.Contains(all, p => System.Text.RegularExpressions.Regex.IsMatch(p.PublishedName, @"^oem\d+\.inf$"));
        Assert.Contains(all, p => p.OriginalName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummaryNamesThePublishedNameTheOriginalInfAndTheProvider()
    {
        Assert.Equal("oem92.inf · wacom_wusd.inf · Wacom Technology", Wusd.Summary);
    }
}
