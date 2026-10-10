using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The writer's output, checked against the StrokeCorpus schema (a copy in Fixtures/) and against the rules a
/// JSON Schema cannot say: a row has as many cells as the file declares columns, only <c>arrived</c> may be
/// null, and the counts a file states are the counts it has. Those are the same checks the corpus's own
/// <c>tools/validate.py</c> makes, so a file that passes here is one the corpus will accept.
/// </summary>
public class StrokeTakeWriterTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "take.schema.json")));

    private static TabletReading Air(double ms, double x, double lean = 0) =>
        new((long)(ms * 1000), x, x * 2, 0, Height: 12, Lean: lean, Azimuth: lean == 0 ? 0 : 135, Twist: 7);

    private static TabletReading Down(double ms, double x, double pressure = 400) =>
        new((long)(ms * 1000), x, x * 2, pressure, Height: 0, Lean: 20, Azimuth: 90, Twist: 7);

    private static readonly TakeDescription Described = new(
        Id: "freeform-ptk-470-20261007-120000",
        Gesture: "freeform",
        Intent: "Draw whatever.",
        Username: "tester",
        Notes: "a note",
        RecordedAt: new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
        Device: new TakeDevice("Wacom PTK-470", "OpenTabletDriver 0.6.5", "1.2.3", "OpenTabletDriver", 1023, "Counts as the tablet reports them."),
        Space: new TabletSpace(15200, 9500, 152.0, 95.0),
        Channels: TakeChannels.All);

    /// <summary>Two strokes with an approach, a departure and a lift between them, then a stop.</summary>
    private static SegmentedTake TwoStrokes(bool keepAirborne = false) => StrokeSegmenter.Segment(
        [
            Air(0, 1), Air(100, 2), Air(200, 3),
            Down(210, 4), Down(211, 5), Down(212, 6),
            Air(213, 7), Air(214, 8), Air(300, 9),
            Down(900, 10), Down(901, 11),
            Air(902, 12), Air(903, 13),
            Down(2000, 14),
        ],
        keepAirborne,
        stoppedAt: 13);

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    private static EvaluationResults Evaluate(JsonNode file) =>
        Schema.Evaluate(JsonSerializer.SerializeToElement(file), new EvaluationOptions { OutputFormat = OutputFormat.List });

    private static void AssertValid(string json)
    {
        var result = Evaluate(Parse(json));
        Assert.True(result.IsValid, "The corpus schema rejected the file:\n" + Describe(result));
        Assert.Empty(SemanticProblems(Parse(json)));
    }

    private static string Describe(EvaluationResults result) =>
        string.Join("\n", (result.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key}: {e.Value}")));

    /// <summary>What tools/validate.py checks that a JSON Schema cannot.</summary>
    private static string[] SemanticProblems(JsonNode file)
    {
        var problems = new System.Collections.Generic.List<string>();
        var columns = file["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();

        void Rows(string where, JsonNode? rows)
        {
            if (rows is null) return;
            var i = 0;
            foreach (var row in rows.AsArray())
            {
                var cells = row!.AsArray();
                if (cells.Count != columns.Length) problems.Add($"{where}[{i}] has {cells.Count} cells for {columns.Length} columns");
                for (var c = 0; c < cells.Count; c++)
                {
                    if (cells[c] is null && !(c < columns.Length && columns[c] == "arrived")) problems.Add($"{where}[{i}][{c}] is null");
                }
                i++;
            }
        }

        var strokes = file["strokes"]!.AsArray();
        if (file["strokeCount"]!.GetValue<int>() != strokes.Count) problems.Add("strokeCount disagrees");

        for (var s = 0; s < strokes.Count; s++)
        {
            var stroke = strokes[s]!;
            Rows($"strokes[{s}].readings", stroke["readings"]);
            Rows($"strokes[{s}].approach", stroke["approach"]);
            Rows($"strokes[{s}].departure", stroke["departure"]);

            if (stroke["readingCount"]!.GetValue<int>() != stroke["readings"]!.AsArray().Count)
            {
                problems.Add($"strokes[{s}].readingCount disagrees");
            }
        }

        Rows("aloft", file["aloft"]);

        return [.. problems];
    }

    [Fact]
    public void ARecordingWithEverythingInItIsAcceptedByTheCorpusSchema()
    {
        AssertValid(StrokeTakeWriter.ToJson(TwoStrokes(keepAirborne: true), Described with
        {
            Counted = new SessionCounts(PacketsFromTheDriver: 14, PacketsOutsideTheCaptureRegion: 0, PointsDelivered: 14),
        }));
    }

    // The optional labels a person can give a recording (StrokeCorpus #23 and #30). Record mode does not write
    // them yet; these say the schema copy in Fixtures/ knows them, so adding them later cannot be refused.
    [Fact]
    public void ARecordingMayCarryANameAndAPen()
    {
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
        file["name"] = "Quick signature";
        file["device"]!["pen"] = "Wacom Pro Pen 2";

        var result = Evaluate(file);
        Assert.True(result.IsValid, "The corpus schema rejected a name and a pen:\n" + Describe(result));
    }

    [Fact]
    public void ButNeverAnEmptyOne()
    {
        // A name or a pen nobody gave is absent, not "": the schema says so (minLength 1). Each is checked in
        // pairs, a real value accepted and an empty one rejected, so the rejection can only be the empty rule:
        // a copy of the schema from before these fields existed rejects BOTH (it does not know the property
        // at all), and a bare "rejects empty" would pass against it for the wrong reason.
        Action<JsonNode, string>[] give =
        [
            (file, value) => file["name"] = value,
            (file, value) => file["device"]!["pen"] = value,
        ];
        foreach (var set in give)
        {
            var real = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
            set(real, "Something typed");
            var accepted = Evaluate(real);
            Assert.True(accepted.IsValid, "A real value was refused: " + Describe(accepted));

            var empty = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
            set(empty, "");
            Assert.False(Evaluate(empty).IsValid, "An empty value was accepted.");
        }
    }

    [Fact]
    public void SoIsOneWithoutTheAirborneRecordAndWithoutTheSessionCounts()
    {
        var json = StrokeTakeWriter.ToJson(TwoStrokes(), Described);

        AssertValid(json);
        Assert.Null(Parse(json)["aloft"]);
        Assert.Null(Parse(json)["whatTheSessionCounted"]);
        Assert.False(Parse(json)["keptEveryAirborneReading"]!.GetValue<bool>());
    }

    [Fact]
    public void ARecordingThatNeverTouchedDownIsStillAFile()
    {
        var take = StrokeSegmenter.Segment([Air(0, 1), Air(1, 2)], keepAirborne: true);
        var json = StrokeTakeWriter.ToJson(take, Described);

        AssertValid(json);
        Assert.Equal(0, Parse(json)["strokeCount"]!.GetValue<int>());
        Assert.Equal(2, Parse(json)["aloft"]!.AsArray().Count);
        Assert.Equal("the recording was stopped before anything was drawn", Parse(json)["endedBy"]!.GetValue<string>());
    }

    [Fact]
    public void ALandingWithNoHoverBeforeItSaysSoInWords()
    {
        var json = StrokeTakeWriter.ToJson(StrokeSegmenter.Segment([Down(0, 1), Down(1, 2)], false), Described);

        AssertValid(json);
        var stroke = Parse(json)["strokes"]![0]!;
        Assert.Equal("the pen was not reported in the air at all", stroke["lastSeenInTheAir"]!.GetValue<string>());
        Assert.Null(stroke["lastSeenInTheAirMs"]);
        Assert.Null(stroke["approach"]);
    }

    [Fact]
    public void ALandingAfterHoverSaysHowLongAgoThePenWasLastSeen()
    {
        var json = StrokeTakeWriter.ToJson(TwoStrokes(), Described);

        var stroke = Parse(json)["strokes"]![0]!;
        Assert.Equal(10.0, stroke["lastSeenInTheAirMs"]!.GetValue<double>());   // 200 ms -> 210 ms
        Assert.Null(stroke["lastSeenInTheAir"]);
    }

    [Fact]
    public void TheFileSaysWhatXAndYAreAndHasNoPlacement()
    {
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));

        Assert.Equal(8, file["formatVersion"]!.GetValue<int>());
        Assert.Equal("tablet", file["coordinates"]!["space"]!.GetValue<string>());
        Assert.Equal("digitizer counts", file["coordinates"]!["units"]!.GetValue<string>());
        Assert.Equal(15200, file["coordinates"]!["maxX"]!.GetValue<double>());
        Assert.Equal(95.0, file["coordinates"]!["heightMm"]!.GetValue<double>());
        Assert.Null(file["placement"]);
    }

    [Fact]
    public void TheColumnsAreOnlyThoseMeasuredAndCellsFollowTheirOrder()
    {
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));

        Assert.Equal(
            ["arrived", "x", "y", "pressure", "height", "lean", "azimuth", "twist"],
            file["columns"]!.AsArray().Select(c => c!.GetValue<string>()));

        // The first reading in contact: arrived 0, x 4, y 8, pressure 400, height 0, lean 20, azimuth 90, twist 7.
        var row = file["strokes"]![0]!["readings"]![0]!.AsArray().Select(c => c!.GetValue<double>());
        Assert.Equal([0.0, 4, 8, 400, 0, 20, 90, 7], row);
    }

    [Fact]
    public void AChannelThatWasNotMeasuredIsLeftOutNotWrittenAsZero()
    {
        var take = StrokeSegmenter.Segment(
            [new TabletReading(0, 5, 6, 100), new TabletReading(1000, 6, 7, 120)], false);

        var json = StrokeTakeWriter.ToJson(take, Described with { Channels = new TakeChannels(Height: false, Tilt: false, Twist: false) });

        AssertValid(json);
        Assert.Equal(["arrived", "x", "y", "pressure"], Parse(json)["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal(4, Parse(json)["strokes"]![0]!["readings"]![1]!.AsArray().Count);
    }

    [Fact]
    public void ClaimingAHeightColumnWithoutHeightsIsAnErrorNotAZero()
    {
        var take = StrokeSegmenter.Segment([new TabletReading(0, 5, 6, 100)], false);

        Assert.Throws<InvalidOperationException>(() => StrokeTakeWriter.ToJson(take, Described));
    }

    [Fact]
    public void ArrivalsAreRebasedToTheFirstContactSoTheApproachIsNegative()
    {
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
        var first = file["strokes"]![0]!;

        Assert.Equal(0, first["readings"]![0]![0]!.GetValue<long>());
        Assert.Equal(1000, first["readings"]![1]![0]!.GetValue<long>());
        var approach = first["approach"]!.AsArray();
        Assert.Equal(-10_000, approach[approach.Count - 1]![0]!.GetValue<long>());   // the hover 10 ms before the landing
        Assert.Equal(690_000, file["strokes"]![1]!["readings"]![0]![0]!.GetValue<long>());   // 900 ms after 210 ms
    }

    [Fact]
    public void TheLedgerInTheFileBalances()
    {
        var take = TwoStrokes();
        var file = Parse(StrokeTakeWriter.ToJson(take, Described));

        var handed = file["readingsHandedToTheRecorder"]!.GetValue<int>();
        var inStrokes = file["strokes"]!.AsArray().Sum(s => s!["readingCount"]!.GetValue<int>());

        Assert.Equal(14, handed);
        Assert.Equal(
            handed,
            inStrokes
            + file["readingsAfterTheRecordingStopped"]!.GetValue<int>()
            + file["readingsAirborneAndNotKept"]!.GetValue<int>()
            + file["readingsAirborneKeptWithAStroke"]!.GetValue<int>()
            + file["readingsDroppedForBeingOffThePad"]!.GetValue<int>());
        Assert.Equal(1, file["readingsAfterTheRecordingStopped"]!.GetValue<int>());
    }

    [Fact]
    public void NumbersAreWrittenWithADotWhateverTheMachineSpeaks()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var take = StrokeSegmenter.Segment([new TabletReading(0, 5.5, 6.25, 100, 3, 1.5, 2.5, 3.5)], false);

            var json = StrokeTakeWriter.ToJson(take, Described);

            AssertValid(json);
            Assert.Contains("5.5, 6.25, 100, 3, 1.5, 2.5, 3.5", json);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void TextPeopleTypeIsReadableInTheFileAndStillValid()
    {
        var json = StrokeTakeWriter.ToJson(TwoStrokes(), Described with
        {
            Username = "Zoë O'Brien",
            Notes = "Pen's \"nib\" is worn <a href='x'>& done</a>",
        });

        AssertValid(json);
        Assert.Contains("Zoë O'Brien", json);
        Assert.DoesNotContain("\\u0027", json);
        Assert.Equal("Pen's \"nib\" is worn <a href='x'>& done</a>", Parse(json)["notes"]!.GetValue<string>());
    }

    [Fact]
    public void TheCorpusSchemaCanFailThisFile()
    {
        // The check above is only worth something if the schema rejects a recording that breaks a rule:
        // a tablet recording has no placement, because there is no desktop to place.
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
        file["placement"] = new JsonObject
        {
            ["units"] = "px", ["note"] = "n", ["scaleX"] = 1, ["scaleY"] = 1, ["originX"] = 0, ["originY"] = 0,
        };

        Assert.False(Evaluate(file).IsValid);
    }

    [Fact]
    public void WritingNeverOverwritesAndTheIdIsTheFileName()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ota-take-{Guid.NewGuid():N}");
        try
        {
            var first = StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "a-take");
            var second = StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "a-take.json");

            Assert.Equal("a-take.json", Path.GetFileName(first));
            Assert.Equal("a-take-2.json", Path.GetFileName(second));
            Assert.Equal("a-take-2", Parse(File.ReadAllText(second))["id"]!.GetValue<string>());
            Assert.Empty(Directory.GetFiles(folder, "*.writing"));
            AssertValid(File.ReadAllText(first));
            Assert.False(File.ReadAllBytes(first).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "no byte order mark");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ARecordingSessionIsAFileTheCorpusAccepts()
    {
        var ms = System.Diagnostics.Stopwatch.Frequency / 1000;
        PenSample Report(double at, double x, double pressure) =>
            new(0, 0, x, x * 2, pressure / 1023, 4, -3, 0, pressure > 0, HoverDistance: pressure > 0 ? 0 : 9,
                Timestamp: 1_000_000 + (long)(at * ms), RawPressure: pressure, HasTilt: true);

        var session = new StrokeRecordingSession(1023);
        foreach (var (at, x, p) in new (double, double, double)[]
                 { (0, 1, 0), (5, 2, 0), (10, 3, 200), (11, 4, 300), (12, 5, 250), (13, 6, 0), (14, 7, 0), (400, 8, 0), (410, 9, 100) })
        {
            session.Add(Report(at, x, p));
        }

        session.Stop();
        session.Add(Report(420, 10, 0));

        var json = StrokeTakeWriter.ToJson(
            session.Segment(keepAirborne: true),
            Described with
            {
                Device = Described.Device with { Conventions = StrokeRecordingSession.Conventions, FullScalePressure = 1023 },
                Channels = session.Channels,
            });

        AssertValid(json);
        var file = Parse(json);
        Assert.Equal(["arrived", "x", "y", "pressure", "height", "lean", "azimuth"], file["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal(2, file["strokeCount"]!.GetValue<int>());
        Assert.Equal("the recording was stopped mid-stroke", file["strokes"]![1]!["endedBy"]!.GetValue<string>());
        Assert.Equal(6, file["aloft"]!.AsArray().Count);   // every airborne report, the one after the stop included
        Assert.Equal(10, file["readingsHandedToTheRecorder"]!.GetValue<int>());
    }

    [Fact]
    public async System.Threading.Tasks.Task SavesThatChooseTheSameNameAtTheSameTimeEndUpWithTheirOwnFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ota-take-{Guid.NewGuid():N}");
        try
        {
            const int writers = 8;
            var ready = new System.Threading.Barrier(writers);

            var paths = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, writers).Select(_ =>
                System.Threading.Tasks.Task.Run(() =>
                {
                    ready.SignalAndWait();
                    return StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "same-name");
                }, TestContext.Current.CancellationToken)));

            Assert.Equal(writers, paths.Distinct().Count());
            Assert.Equal(writers, Directory.GetFiles(folder).Length);   // and no staging or placeholder files besides
            Assert.Empty(Directory.GetFiles(folder, "*.writing"));

            foreach (var path in paths)
            {
                // Each file is whole, valid, and says it is the file it is.
                AssertValid(File.ReadAllText(path));
                Assert.Equal(Path.GetFileNameWithoutExtension(path), Parse(File.ReadAllText(path))["id"]!.GetValue<string>());
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AFailedSaveLeavesNothingBehindAndDoesNotKeepTheName()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ota-take-{Guid.NewGuid():N}");
        try
        {
            var bad = StrokeSegmenter.Segment([new TabletReading(0, double.NaN, 1, 100, 3, 1, 1, 1)], false);

            Assert.Throws<InvalidOperationException>(() => StrokeTakeWriter.Write(bad, Described, folder, "take"));
            Assert.Empty(Directory.GetFiles(folder));

            // The name was released, so the next save gets it.
            Assert.Equal("take.json", Path.GetFileName(StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "take")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AStaleStagingFileFromAnEarlierCrashDoesNotBlockASaveAndIsNotDeleted()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ota-take-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            var stale = Path.Combine(folder, "take.writing");
            File.WriteAllText(stale, "left behind by something that died");

            var path = StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "take");

            AssertValid(File.ReadAllText(path));
            Assert.Equal("left behind by something that died", File.ReadAllText(stale));   // not ours to delete
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AMoveThatFailsLeavesNoStagingFileOrPlaceholder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ota-take-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "take.json"));   // a directory where the file would go

            Assert.ThrowsAny<Exception>(() => StrokeTakeWriter.Write(TwoStrokes(), Described, folder, "take"));

            Assert.Empty(Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANumberThatIsNotFiniteIsRefusedNotWrittenAsABareWord(double bad)
    {
        var fine = new TabletReading(0, 1, 1, 100, 3, 1, 1, 1);

        foreach (var reading in new[]
                 {
                     fine with { X = bad },
                     fine with { Y = bad },
                     fine with { Lean = bad },
                     fine with { Azimuth = bad },
                     fine with { Twist = bad },
                 })
        {
            var take = StrokeSegmenter.Segment([reading], false);

            Assert.Single(take.Strokes);   // it is a stroke reading, so it would be written
            Assert.Throws<InvalidOperationException>(() => StrokeTakeWriter.ToJson(take, Described));
        }

        // Pressure above zero is contact, so infinity is written; NaN and -infinity are not contact and never are.
        Assert.Throws<InvalidOperationException>(() =>
            StrokeTakeWriter.ToJson(StrokeSegmenter.Segment([fine with { Pressure = double.PositiveInfinity }], false), Described));
    }

    [Fact]
    public void TheClocksTextSaysItIsWhenTheApplicationReceivedTheReportNotWhenTheHardwareMeasuredIt()
    {
        var file = Parse(StrokeTakeWriter.ToJson(TwoStrokes(), Described));
        var clocks = file["clocks"]!.GetValue<string>();

        Assert.Contains("not when the hardware measured it", clocks);
        Assert.Contains("airborne record", clocks);   // the origin on a take that never touched down
    }

    [Fact]
    public void ASuggestedNameSaysWhatMadeItAndWhen()
    {
        Assert.Equal(
            "freeform-wacom-ptk-470-20261007-120000",
            StrokeTakeWriter.Suggest("freeform", " Wacom  PTK-470 ", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)));
    }
}
