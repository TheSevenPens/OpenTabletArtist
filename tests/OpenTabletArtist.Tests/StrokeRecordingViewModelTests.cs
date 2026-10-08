using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        TestReports.Tablet, "OpenTabletDriver 0.6.5.0", 1023, new TabletSpace(15200, 9500, 152.0, 95.0));

    /// <summary>The view model, the tap it attached to the stream (which is what the daemon thread would call), and the
    /// review dialog it asked for, which stays "open" until a test closes it.</summary>
    private sealed class Rig
    {
        public StrokeRecordingViewModel Vm = null!;
        public Action<JObject, PenSample>? Tap;
        public readonly List<TaskCompletionSource> Reviews = new();
        public readonly List<string> Problems = new();
        public bool ReviewFails;

        /// <summary>A report as the stream delivers it: if nothing is attached, it reaches nobody.</summary>
        public void Feed(PenSample s, string tablet = TestReports.Tablet, double maxPressure = 1023) =>
            Tap?.Invoke(TestReports.Json(s, tablet: tablet, maxPressure: maxPressure), s);

        /// <summary>The person closes the dialog, however they did it.</summary>
        public void CloseDialog() => Reviews[^1].SetResult();
    }

    private Rig NewRig(Func<StrokeRecordingContext?>? context = null, Action<string>? reveal = null, string account = "account-name")
    {
        var rig = new Rig();
        rig.Vm = new StrokeRecordingViewModel(
            context ?? (() => Ptk470),
            tap => rig.Tap = tap,
            showReview: _ =>
            {
                var opened = new TaskCompletionSource();
                rig.Reviews.Add(opened);
                return rig.ReviewFails ? Task.FromException(new InvalidOperationException("no window")) : opened.Task;
            },
            showProblem: message =>
            {
                rig.Problems.Add(message);
                return Task.CompletedTask;
            },
            folder: () => _folder,
            now: () => new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
            reveal: reveal,
            accountName: () => account);
        return rig;
    }

    /// <summary>The number on the ledger line that says <paramref name="what"/>.</summary>
    private static int Of(string ledger, string what)
    {
        var line = ledger.Split('\n').Single(l => l.Contains(what));
        return int.Parse(line.TrimStart().Split(' ')[0], NumberStyles.AllowThousands, CultureInfo.CurrentCulture);
    }

    private static PenSample Report(double ms, double pressure = 0, double x = 100) =>
        new(0, 0, x, x * 2, pressure / 1023, 4, -3, 0, pressure > 0, HoverDistance: pressure > 0 ? 0 : 9,
            Timestamp: 1_000_000 + (long)(ms * Ms), RawPressure: pressure, HasTilt: true);

    /// <summary>Hover, one stroke, hover, a second stroke.</summary>
    private static void Draw(Rig rig)
    {
        foreach (var (at, p, x) in new (double, double, double)[]
                 { (0, 0, 1), (5, 0, 2), (10, 200, 3), (11, 300, 4), (12, 250, 5), (13, 0, 6), (14, 0, 7), (500, 0, 8), (510, 100, 9), (511, 120, 10) })
        {
            rig.Feed(Report(at, p, x));
        }
    }

    private static JsonNode Saved(StrokeRecordingViewModel vm) => JsonNode.Parse(File.ReadAllText(vm.SavedPath))!;

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
        var rig = NewRig(() => null);

        rig.Vm.StartCommand.Execute(null);

        Assert.True(rig.Vm.IsIdle);
        Assert.True(rig.Vm.HasError);
        Assert.Null(rig.Tap);
    }

    [Fact]
    public void RecordingAttachesToTheStreamAndStoppingDetachesIt()
    {
        var rig = NewRig();
        Assert.Null(rig.Tap);

        rig.Vm.StartCommand.Execute(null);
        Assert.NotNull(rig.Tap);

        rig.Vm.StopCommand.Execute(null);
        Assert.Null(rig.Tap);
    }

    [Fact]
    public void ReportsOutsideARecordingReachNoRecording()
    {
        var rig = NewRig();
        rig.Feed(Report(0, 100));
        rig.Vm.StartCommand.Execute(null);
        rig.Vm.StopCommand.Execute(null);
        rig.Feed(Report(1, 100));

        Assert.True(rig.Vm.IsReview);
        Assert.Contains("0 strokes", rig.Vm.LedgerText);
        Assert.Equal(0, Of(rig.Vm.LedgerText, "reports in total"));
    }

    [Fact]
    public void AReportInFlightWhenStopIsPressedIsCountedAfterTheStopNotLost()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 100));
        var inFlight = rig.Tap!;   // the receive thread had already picked the tap up

        rig.Vm.StopCommand.Execute(null);
        inFlight(TestReports.Json(Report(1, 100)), Report(1, 100));
        rig.Vm.KeepAirborne = true;   // re-reads the recording

        Assert.Equal(2, Of(rig.Vm.LedgerText, "reports in total"));
        Assert.Equal(1, Of(rig.Vm.LedgerText, "after the stop"));
        Assert.DoesNotContain("DOES NOT BALANCE", rig.Vm.LedgerText);
    }

    [Fact]
    public void WhileRecordingTheClockSaysHowFarItHasGot()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.Tick();

        Assert.True(rig.Vm.IsRecording);
        Assert.Contains("2 strokes", rig.Vm.StatusText);
        Assert.Contains("10 reports", rig.Vm.StatusText);
    }

    [Fact]
    public void StoppingShowsWhereEveryReportWent()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);

        Assert.True(rig.Vm.IsReview);
        Assert.Contains("2 strokes", rig.Vm.LedgerText);
        Assert.Equal(10, Of(rig.Vm.LedgerText, "reports in total"));
        Assert.Equal(5, Of(rig.Vm.LedgerText, "in strokes"));
        Assert.DoesNotContain("DOES NOT BALANCE", rig.Vm.LedgerText);
        Assert.DoesNotContain("Not recorded", rig.Vm.LedgerText);
    }

    [Fact]
    public void KeepingAirborneAtReviewChangesWhatTheLedgerSaysNotWhatWasRecorded()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        Assert.Equal(0, Of(rig.Vm.LedgerText, "kept as the airborne record"));

        rig.Vm.KeepAirborne = true;

        Assert.Equal(5, Of(rig.Vm.LedgerText, "kept as the airborne record"));
        Assert.Equal(0, Of(rig.Vm.LedgerText, "left out"));
    }

    [Fact]
    public void SavingWritesAFileAndGoesBackToIdleShowingWhereItWent()
    {
        var rig = NewRig();
        var vm = rig.Vm;
        vm.StartCommand.Execute(null);
        Draw(rig);
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

        var file = Saved(vm);
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
        var rig = NewRig(() => current);
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        current = current with { Tablet = "Wacom PTK-670" };
        rig.Vm.StopCommand.Execute(null);

        rig.Vm.SaveCommand.Execute(null);

        Assert.Equal("Wacom PTK-470", Saved(rig.Vm)["device"]!["tablet"]!.GetValue<string>());
    }

    [Fact]
    public void AnotherTabletsReportsAreLeftOutAndTheFileSaysSo()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 200, 1));
        rig.Feed(Report(1, 4096, 2), tablet: "Wacom PTK-670", maxPressure: 8191);
        rig.Feed(Report(2, 4096, 3), tablet: "Wacom PTK-670", maxPressure: 8191);
        rig.Feed(Report(3, 210, 4));
        rig.Vm.StopCommand.Execute(null);

        Assert.Equal(2, Of(rig.Vm.LedgerText, "reports in total"));
        Assert.Contains("Not recorded:", rig.Vm.LedgerText);
        Assert.Equal(2, Of(rig.Vm.LedgerText, "from other devices"));

        rig.Vm.SaveCommand.Execute(null);
        var file = Saved(rig.Vm);

        Assert.Contains("2 reports from other devices were ignored", file["notes"]!.GetValue<string>());
        Assert.Equal(2, file["readingsHandedToTheRecorder"]!.GetValue<int>());
        var pressures = file["strokes"]![0]!["readings"]!.AsArray().Select(r => r![3]!.GetValue<double>());
        Assert.All(pressures, p => Assert.True(p < 1023));
    }

    [Fact]
    public void AMouseReportIsLeftOutAndNotWrittenAsAHoveringPen()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 200, 1));
        var mouse = Report(1, 0, 2);
        rig.Tap!(TestReports.Json(mouse, pressure: false, tilt: false), mouse);
        rig.Feed(Report(2, 210, 3));
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);

        var file = Saved(rig.Vm);

        Assert.Contains("1 reports from this tablet were not complete pen measurements", file["notes"]!.GetValue<string>());
        Assert.Equal(["arrived", "x", "y", "pressure", "height", "lean", "azimuth"], file["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
    }

    [Fact]
    public void WhenTheTabletsSpecificationsChangeTheRecordingEndsAndSaysWhy()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 200, 1));
        rig.Feed(Report(1, 200, 2), maxPressure: 8191);   // the same tablet, now on a different pressure scale
        rig.Feed(Report(2, 200, 3));

        rig.Vm.Tick();

        Assert.True(rig.Vm.IsReview);
        Assert.Contains("specifications changed", rig.Vm.ErrorText);
        Assert.Null(rig.Tap);

        rig.Vm.SaveCommand.Execute(null);   // what was recorded before the change is still a recording

        Assert.Contains("specifications changed", Saved(rig.Vm)["notes"]!.GetValue<string>());
        Assert.Equal(1, Saved(rig.Vm)["readingsHandedToTheRecorder"]!.GetValue<int>());
    }

    [Fact]
    public void ARecordingWithNothingDrawnIsNotSavedUnlessTheHoveringIsKept()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 0, 1));
        rig.Feed(Report(5, 0, 2));
        rig.Vm.StopCommand.Execute(null);

        rig.Vm.SaveCommand.Execute(null);

        Assert.True(rig.Vm.IsReview);
        Assert.True(rig.Vm.HasError);
        Assert.False(Directory.Exists(_folder));

        rig.Vm.KeepAirborne = true;
        rig.Vm.SaveCommand.Execute(null);

        Assert.True(rig.Vm.IsIdle);
        Assert.True(rig.Vm.HasSaved);
    }

    [Fact]
    public void AFileThatCannotBeWrittenLeavesTheRecordingWaitingToBeSavedAgain()
    {
        File.WriteAllText(_folder, "this is a file where the folder should be");   // Dispose deletes directories only
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);

        try
        {
            rig.Vm.SaveCommand.Execute(null);

            Assert.True(rig.Vm.IsReview);
            Assert.StartsWith("Couldn't save the recording", rig.Vm.ErrorText);
            Assert.False(rig.Vm.HasSaved);
        }
        finally { File.Delete(_folder); }
    }

    [Fact]
    public void DiscardingThrowsTheRecordingAwayAndDetaches()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);

        rig.Vm.DiscardCommand.Execute(null);

        Assert.True(rig.Vm.IsIdle);
        Assert.Equal("", rig.Vm.LedgerText);
        Assert.Null(rig.Tap);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void StartingAgainClearsTheLastSavedFile()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);
        Assert.True(rig.Vm.HasSaved);

        rig.Vm.StartCommand.Execute(null);

        Assert.False(rig.Vm.HasSaved);
        Assert.True(rig.Vm.IsRecording);
    }

    [Fact]
    public void ARecordingThatReachesTheLimitStopsItselfAndSaysHowManyWereDroppedInItsNotes()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        var s = Report(0, 100);
        var json = TestReports.Json(s);   // one report, offered over and over: building 600,000 would only slow the test
        for (var i = 0; i < StrokeRecordingSession.MaxReadings + 7; i++) rig.Tap!(json, s);

        rig.Vm.Tick();

        Assert.True(rig.Vm.IsReview);
        Assert.Equal(7, Of(rig.Vm.LedgerText, "dropped at the limit"));

        rig.Vm.SaveCommand.Execute(null);

        Assert.Contains("7 later reports were dropped", Saved(rig.Vm)["notes"]!.GetValue<string>());
    }

    // ---- the review dialog ----

    [Fact]
    public void StoppingOpensTheReviewDialogOnce()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Assert.Empty(rig.Reviews);   // recording itself shows nothing but the clock

        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.StopCommand.Execute(null);   // a second press does nothing

        Assert.Single(rig.Reviews);
        Assert.True(rig.Vm.IsReview);
    }

    [Fact]
    public void ARecordingThatEndsItselfOpensTheDialogToo()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        rig.Feed(Report(0, 200, 1));
        rig.Feed(Report(1, 200, 2), maxPressure: 8191);   // the tablet's specifications changed

        rig.Vm.Tick();

        Assert.Single(rig.Reviews);
        Assert.Contains("specifications changed", rig.Vm.ErrorText);   // set before the dialog opens, so it is there to read
    }

    [Fact]
    public void ClosingTheDialogWithoutSavingLeavesTheScribblePageAsItWas()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);

        rig.CloseDialog();   // the dialog has already asked, and been told yes

        Assert.True(rig.Vm.IsIdle);
        Assert.Equal("", rig.Vm.LedgerText);
        Assert.Equal("", rig.Vm.StatusText);
        Assert.False(rig.Vm.HasSaved);
        Assert.False(rig.Vm.HasError);
        Assert.Null(rig.Tap);
        Assert.False(Directory.Exists(_folder));   // and nothing was written
    }

    [Fact]
    public void AfterSavingTheDialogClosesAndNothingOfTheRecordingStaysBehind()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);
        var saved = rig.Vm.SavedPath;

        Assert.True(rig.Vm.HasSaved);   // the dialog's closing screen has the file name to show
        Assert.True(File.Exists(saved));

        rig.CloseDialog();

        Assert.True(rig.Vm.IsIdle);
        Assert.False(rig.Vm.HasSaved);   // the Scribble page never sees it
        Assert.Equal("", rig.Vm.StatusText);
        Assert.True(File.Exists(saved));   // closing the dialog does not undo the save
    }

    [Fact]
    public void ADialogThatCannotBeShownDiscardsTheRecordingAndSaysSo()
    {
        var rig = NewRig();
        rig.ReviewFails = true;
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);

        rig.Vm.StopCommand.Execute(null);

        Assert.True(rig.Vm.IsIdle);
        Assert.Single(rig.Problems);
        Assert.Contains("discarded", rig.Problems[0]);
        Assert.Contains("no window", rig.Problems[0]);
    }

    [Fact]
    public void WithoutATabletTheProblemIsSaidInAMessageNotOnThePage()
    {
        var rig = NewRig(() => null);

        rig.Vm.StartCommand.Execute(null);

        Assert.Single(rig.Problems);
        Assert.Contains("No tablet to record", rig.Problems[0]);
        Assert.Empty(rig.Reviews);
    }

    [Fact]
    public void TheNameStartsAsTheAccountNameAndIsWrittenIfNobodyChangesIt()
    {
        var rig = NewRig(account: "  seven ");   // stray spaces are not part of a name
        Assert.Equal("seven", rig.Vm.Username);

        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);

        Assert.Equal("seven", Saved(rig.Vm)["username"]!.GetValue<string>());
    }

    [Fact]
    public void ANameTheyTypeReplacesTheAccountNameAndIsKeptForTheNextRecording()
    {
        var rig = NewRig(account: "seven");
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.Username = "TheSevenPens";
        rig.Vm.SaveCommand.Execute(null);
        Assert.Equal("TheSevenPens", Saved(rig.Vm)["username"]!.GetValue<string>());
        rig.CloseDialog();

        Assert.Equal("TheSevenPens", rig.Vm.Username);   // the same person on the same desk: not reset to the account name
    }

    [Fact]
    public void AClearedNameStaysClearedRatherThanComingBack()
    {
        var rig = NewRig(account: "seven");
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.Username = "";
        rig.Vm.SaveCommand.Execute(null);

        Assert.Equal("", Saved(rig.Vm)["username"]!.GetValue<string>());   // they chose to publish without one
    }

    [Fact]
    public void WithNothingSuppliedTheNameIsTheSignedInAccountsShortName()
    {
        var vm = new StrokeRecordingViewModel(() => Ptk470, _ => { });

        Assert.Equal(Environment.UserName.Trim(), vm.Username);
    }

    [Fact]
    public void AnAccountNameThatCannotBeReadLeavesTheFieldEmptyToTypeIn()
    {
        Assert.Equal("", NewRig(account: "").Vm.Username);
    }

    [Fact]
    public void TheTabletIsShownFromWhenRecordingStartsAndGoneWhenItIsDismissed()
    {
        var current = Ptk470;
        var rig = NewRig(() => current);
        Assert.Equal("", rig.Vm.Tablet);

        rig.Vm.StartCommand.Execute(null);
        Assert.Equal("Wacom PTK-470", rig.Vm.Tablet);

        current = current with { Tablet = "Wacom PTK-670" };   // the active tablet changes while recording
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        Assert.Equal("Wacom PTK-470", rig.Vm.Tablet);          // what the dialog shows is what is written

        rig.CloseDialog();
        Assert.Equal("", rig.Vm.Tablet);
    }

    [Fact]
    public void ARecordingCanBeStartedAgainAfterTheDialogIsClosed()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);
        rig.CloseDialog();

        rig.Vm.StartCommand.Execute(null);

        Assert.True(rig.Vm.IsRecording);
        Assert.NotNull(rig.Tap);
    }

    [Fact]
    public void TheResultsAreOneLinePerPlaceAReportCanGo()
    {
        var rig = NewRig();
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);

        var lines = rig.Vm.LedgerText.Split('\n');

        Assert.StartsWith("2 strokes over", lines[0]);
        Assert.Equal(
            ["reports in total", "in strokes", "hovering, kept with a stroke", "hovering, kept as the airborne record", "hovering, left out", "after the stop"],
            lines.Skip(2).Select(l => l.TrimStart().Split("  ", 2)[1].Trim()));
        // The parts add up to the whole, which is what the lined-up numbers are for.
        Assert.Equal(
            Of(rig.Vm.LedgerText, "reports in total"),
            lines.Skip(3).Sum(l => int.Parse(l.Trim().Split(' ')[0], CultureInfo.CurrentCulture)));
    }

    [Fact]
    public void ShowFolderOpensTheFolderTheFileIsIn()
    {
        string? revealed = null;
        var rig = NewRig(reveal: path => revealed = path);
        rig.Vm.StartCommand.Execute(null);
        Draw(rig);
        rig.Vm.StopCommand.Execute(null);
        rig.Vm.SaveCommand.Execute(null);

        rig.Vm.RevealFolderCommand.Execute(null);

        Assert.Equal(_folder, revealed);
    }
}
