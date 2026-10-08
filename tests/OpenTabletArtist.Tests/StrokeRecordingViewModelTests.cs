using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;

namespace OpenTabletArtist.Tests;

public class StrokeRecordingViewModelTests : IDisposable
{
    private static readonly long Ms = Stopwatch.Frequency / 1000;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ota-rec-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static readonly StrokeRecordingContext Ptk470 = new(
        "Wacom PTK-470", "OpenTabletDriver 0.6.5.0", 1023, new TabletSpace(15200, 9500, 152.0, 95.0));

    private StrokeRecordingViewModel NewVm(Func<StrokeRecordingContext?>? context = null, Action<string>? reveal = null) =>
        new(context ?? (() => Ptk470), () => _folder, () => new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), reveal);

    private static PenSample Report(double ms, double pressure = 0, double x = 100) =>
        new(0, 0, x, x * 2, pressure / 1023, 4, -3, 0, pressure > 0, HoverDistance: pressure > 0 ? 0 : 9,
            Timestamp: 1_000_000 + (long)(ms * Ms), RawPressure: pressure, HasTilt: true);

    /// <summary>Hover, one stroke, hover, a second stroke.</summary>
    private static void Draw(StrokeRecordingViewModel vm)
    {
        foreach (var (at, p, x) in new (double, double, double)[]
                 { (0, 0, 1), (5, 0, 2), (10, 200, 3), (11, 300, 4), (12, 250, 5), (13, 0, 6), (14, 0, 7), (500, 0, 8), (510, 100, 9), (511, 120, 10) })
        {
            vm.Add(Report(at, p, x));
        }
    }

    // ---- the tablet and driver ----

    private static JArray Tablets(string name = "Wacom PTK-470", double maxPressure = 1023) => new()
    {
        new JObject
        {
            ["Properties"] = new JObject
            {
                ["Name"] = name,
                ["Specifications"] = new JObject
                {
                    ["Digitizer"] = new JObject { ["Width"] = 152.0, ["Height"] = 95.0, ["MaxX"] = 15200, ["MaxY"] = 9500 },
                    ["Pen"] = new JObject { ["MaxPressure"] = maxPressure },
                },
            },
        },
    };

    [Fact]
    public void TheContextComesFromTheTabletsSpecificationsAndNamesTheDriver()
    {
        var context = StrokeRecordingContext.From(Tablets(), "wacom ptk-470", " 0.6.5.0 ");

        Assert.NotNull(context);
        Assert.Equal("Wacom PTK-470", context!.Tablet);
        Assert.Equal("OpenTabletDriver 0.6.5.0", context.Driver);
        Assert.Equal(1023, context.FullScalePressure);
        Assert.Equal(new TabletSpace(15200, 9500, 152.0, 95.0), context.Space);
    }

    [Fact]
    public void AnUnknownDaemonVersionStillNamesTheDriver()
    {
        Assert.Equal("OpenTabletDriver", StrokeRecordingContext.From(Tablets(), "Wacom PTK-470", "")!.Driver);
    }

    [Theory]
    [InlineData("Some other tablet", 1023)]
    [InlineData("Wacom PTK-470", 0)]
    public void ATabletWhoseUnitsCannotBeStatedIsNotRecorded(string asked, double maxPressure)
    {
        Assert.Null(StrokeRecordingContext.From(Tablets(maxPressure: maxPressure), asked, "1"));
        Assert.Null(StrokeRecordingContext.From(null, "Wacom PTK-470", "1"));
        Assert.Null(StrokeRecordingContext.From(Tablets(), null, "1"));
    }

    // ---- the flow ----

    [Fact]
    public void WithoutATabletRecordingDoesNotStartAndSaysWhy()
    {
        var vm = NewVm(() => null);

        vm.StartCommand.Execute(null);

        Assert.True(vm.IsIdle);
        Assert.True(vm.HasError);
    }

    [Fact]
    public void ReportsAreIgnoredUnlessRecording()
    {
        var vm = NewVm();
        vm.Add(Report(0, 100));
        vm.StartCommand.Execute(null);
        vm.StopCommand.Execute(null);
        vm.Add(Report(1, 100));

        Assert.True(vm.IsReview);
        Assert.Contains("0 strokes", vm.LedgerText);
        Assert.Contains("0 reports", vm.LedgerText);
    }

    [Fact]
    public void WhileRecordingTheClockSaysHowFarItHasGot()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.Tick();

        Assert.True(vm.IsRecording);
        Assert.Contains("2 strokes", vm.StatusText);
        Assert.Contains("10 reports", vm.StatusText);
    }

    [Fact]
    public void StoppingShowsWhereEveryReportWent()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);

        Assert.True(vm.IsReview);
        Assert.Contains("2 strokes", vm.LedgerText);
        Assert.Contains("10 reports: 5 in strokes", vm.LedgerText);
        Assert.DoesNotContain("DOES NOT BALANCE", vm.LedgerText);
    }

    [Fact]
    public void KeepingAirborneAtReviewChangesWhatTheLedgerSaysNotWhatWasRecorded()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);
        Assert.Contains("0 hovering kept as the airborne record", vm.LedgerText);

        vm.KeepAirborne = true;

        Assert.Contains("5 hovering kept as the airborne record", vm.LedgerText);
        Assert.Contains("0 hovering left out", vm.LedgerText);
    }

    [Fact]
    public void SavingWritesAFileAndGoesBackToIdleShowingWhereItWent()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);
        vm.Firmware = " 1.2.3 ";
        vm.Username = "tester";
        vm.Notes = "hello";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsIdle);
        Assert.False(vm.HasError);
        Assert.True(vm.HasSaved);
        Assert.Equal(_folder, Path.GetDirectoryName(vm.SavedPath));
        Assert.Equal("freeform-wacom-ptk-470-20261007-120000.json", Path.GetFileName(vm.SavedPath));

        var file = JsonNode.Parse(File.ReadAllText(vm.SavedPath))!;
        Assert.Equal(8, file["formatVersion"]!.GetValue<int>());
        Assert.Equal("tablet", file["coordinates"]!["space"]!.GetValue<string>());
        Assert.Equal(15200, file["coordinates"]!["maxX"]!.GetValue<double>());
        Assert.Equal("Wacom PTK-470", file["device"]!["tablet"]!.GetValue<string>());
        Assert.Equal("OpenTabletDriver 0.6.5.0", file["device"]!["driver"]!.GetValue<string>());
        Assert.Equal("1.2.3", file["device"]!["firmware"]!.GetValue<string>());
        Assert.Equal(1023, file["device"]!["fullScalePressure"]!.GetValue<int>());
        Assert.Equal("tester", file["username"]!.GetValue<string>());
        Assert.Equal("hello", file["notes"]!.GetValue<string>());
        Assert.Equal(2, file["strokeCount"]!.GetValue<int>());
        Assert.Equal(["arrived", "x", "y", "pressure", "height", "lean", "azimuth"], file["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
    }

    [Fact]
    public void TheTabletIsTheOneItWasWhenRecordingStarted()
    {
        var current = Ptk470;
        var vm = NewVm(() => current);
        vm.StartCommand.Execute(null);
        Draw(vm);
        current = current with { Tablet = "Wacom PTK-670" };
        vm.StopCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        Assert.Equal("Wacom PTK-470", JsonNode.Parse(File.ReadAllText(vm.SavedPath))!["device"]!["tablet"]!.GetValue<string>());
    }

    [Fact]
    public void ARecordingWithNothingDrawnIsNotSavedUnlessTheHoveringIsKept()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        vm.Add(Report(0, 0, 1));
        vm.Add(Report(5, 0, 2));
        vm.StopCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsReview);
        Assert.True(vm.HasError);
        Assert.False(Directory.Exists(_folder));

        vm.KeepAirborne = true;
        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsIdle);
        Assert.True(vm.HasSaved);
    }

    [Fact]
    public void AFileThatCannotBeWrittenLeavesTheRecordingWaitingToBeSavedAgain()
    {
        File.WriteAllText(_folder, "this is a file where the folder should be");   // Dispose deletes directories only
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);

        try
        {
            vm.SaveCommand.Execute(null);

            Assert.True(vm.IsReview);
            Assert.StartsWith("Couldn't save the recording", vm.ErrorText);
            Assert.False(vm.HasSaved);
        }
        finally { File.Delete(_folder); }
    }

    [Fact]
    public void DiscardingThrowsTheRecordingAway()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);

        vm.DiscardCommand.Execute(null);

        Assert.True(vm.IsIdle);
        Assert.Equal("", vm.LedgerText);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void StartingAgainClearsTheLastSavedFile()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.True(vm.HasSaved);

        vm.StartCommand.Execute(null);

        Assert.False(vm.HasSaved);
        Assert.True(vm.IsRecording);
    }

    [Fact]
    public void ARecordingThatReachesTheLimitStopsItselfAndSaysSoInItsNotes()
    {
        var vm = NewVm();
        vm.StartCommand.Execute(null);
        vm.Add(Report(0, 100));
        for (var i = 0; i < StrokeRecordingSession.MaxReadings; i++) vm.Add(Report(1, 0, 2));

        vm.Tick();

        Assert.True(vm.IsReview);

        vm.SaveCommand.Execute(null);

        Assert.Contains("report limit", JsonNode.Parse(File.ReadAllText(vm.SavedPath))!["notes"]!.GetValue<string>());
    }

    [Fact]
    public void ShowFolderOpensTheFolderTheFileIsIn()
    {
        string? revealed = null;
        var vm = NewVm(reveal: path => revealed = path);
        vm.StartCommand.Execute(null);
        Draw(vm);
        vm.StopCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        vm.RevealFolderCommand.Execute(null);

        Assert.Equal(_folder, revealed);
    }
}
