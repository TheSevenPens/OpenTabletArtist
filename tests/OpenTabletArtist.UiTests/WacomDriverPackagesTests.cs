using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.Services;
using OpenTabletArtist.Tests;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Plugin.Logging;
using OtdHealth.Collector;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// OpenTabletDriver's Wacom warning never says which driver-store package it matched, so the Driver
/// cleanup card names them. Only for Wacom, and only when the daemon raised the warning.
/// </summary>
public class WacomDriverPackagesTests
{
    private const string WacomWarning =
        "'Wacom' driver is detected. It will cause flaky support to tablets. Processes found: []. " +
        "If any problems arise, visit 'https://opentabletdriver.net/Wiki/FAQ/Windows'.";

    private const string XpPenWarning =
        "'XP-Pen' driver is detected. Processes found: [PenTablet.exe]. " +
        "If any problems arise, visit 'https://opentabletdriver.net/Wiki/FAQ/Windows'.";

    private static readonly DriverStorePackage Wusd = new("oem92.inf", "wacom_wusd.inf", "Wacom Technology");
    private static readonly DriverStorePackage Helper = new("oem70.inf", "wacomwuhaservicecom.inf", "Wacom Technology");
    private static readonly DriverStorePackage Ftdi = new("oem95.inf", "ftdibus.inf", "FTDI");

    private sealed class FakeLog(params string[] warnings) : IDaemonLogSource
    {
        public event Action<LogMessage>? LogReceived { add { } remove { } }
        public Task<List<LogMessage>> GetCurrentLogAsync() =>
            Task.FromResult(warnings.Select(w => new LogMessage { Group = "Detect", Message = w }).ToList());
    }

    private static async Task<DriverConflictMonitor> Seeded(string warning, Func<IReadOnlyList<DriverStorePackage>> packages)
    {
        var monitor = new DriverConflictMonitor(new FakeLog(warning), new FakeConnectionState { IsConnected = true }, packages);
        for (var i = 0; i < 200 && !monitor.HasConflicts; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(monitor.HasConflicts, "the monitor never saw the warning");
        return monitor;
    }

    [AvaloniaFact]
    public async Task WacomWarning_ListsTheMatchingPackages_AndOnlyThose()
    {
        using var monitor = await Seeded(WacomWarning, () => [Ftdi, Wusd, Helper]);
        var wacom = Assert.Single(monitor.Drivers);
        Assert.Equal(["oem70.inf", "oem92.inf"], wacom.Packages!.Select(p => p.PublishedName));
        Assert.True(wacom.HasPackages);
    }

    [AvaloniaFact]
    public async Task WacomWarning_WithNothingInTheStore_SaysSo()
    {
        using var monitor = await Seeded(WacomWarning, () => [Ftdi]);
        var wacom = Assert.Single(monitor.Drivers);
        Assert.NotNull(wacom.Packages);
        Assert.Empty(wacom.Packages!);
        Assert.False(wacom.HasPackages);
        Assert.Contains("No Wacom packages", wacom.PackagesNote);
    }

    [AvaloniaFact]
    public async Task OtherDrivers_AreNeverLookedUp()
    {
        var lookups = 0;
        using var monitor = await Seeded(XpPenWarning, () => { lookups++; return [Wusd]; });
        var xppen = Assert.Single(monitor.Drivers);
        Assert.Null(xppen.Packages);
        Assert.False(xppen.HasPackageLookup);
        Assert.Equal(0, lookups);
    }

    [AvaloniaFact]
    public async Task AFailedLookup_LeavesTheDaemonsWarningAsItWas()
    {
        using var monitor = await Seeded(WacomWarning,
            () => throw new ProbeUnavailableException("The driver package registry key could not be read."));
        var wacom = Assert.Single(monitor.Drivers);
        Assert.Null(wacom.Packages);
        Assert.False(wacom.HasPackageLookup);
        Assert.Equal(WacomWarning, wacom.Detail);
    }

    [AvaloniaFact]
    public async Task TheCardNamesEachPackage_UnderAHeadingThatSaysWhereTheyWereFound()
    {
        using var monitor = await Seeded(WacomWarning, () => [Wusd, Helper]);
        var text = RenderedText(monitor);
        Assert.Contains("Found in the Windows driver store", text);
        Assert.Contains("oem92.inf · wacom_wusd.inf · Wacom Technology", text);
        Assert.Contains("oem70.inf · wacomwuhaservicecom.inf · Wacom Technology", text);
        Assert.Contains("matched on the word \"Wacom\"", string.Join("\n", text));
    }

    [AvaloniaFact]
    public async Task TheCardForAnotherDriver_HasNoPackageBlock()
    {
        using var monitor = await Seeded(XpPenWarning, () => [Wusd]);
        Assert.DoesNotContain("Found in the Windows driver store", RenderedText(monitor));
    }

    /// <summary>Every piece of text the Driver cleanup page actually shows for these detections.</summary>
    private static List<string> RenderedText(DriverConflictMonitor monitor)
    {
        using var vm = new DriverCleanupViewModel(new FakeDialogService(), monitor);
        var view = new DriverCleanupView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        window.Show();
        window.Measure(new Size(1100, 900));
        window.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1100, 900));
        window.Arrange(new Rect(0, 0, 1100, 900));
        Dispatcher.UIThread.RunJobs();
        return view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.Text!).ToList();
    }
}
