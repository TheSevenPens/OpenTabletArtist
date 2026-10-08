using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenTabletArtist.Domain;

/// <summary>The tablet's digitizer, so a count can become millimetres.</summary>
public sealed record TabletSpace(double MaxX, double MaxY, double WidthMm, double HeightMm);

/// <summary>What made the recording, named by the person who was there plus what the driver can tell us.</summary>
public sealed record TakeDevice(
    string Tablet,
    string Driver,
    string Firmware,
    string Api,
    int FullScalePressure,
    string Conventions);

/// <summary>Which channels this recording measured. A channel that was not measured is left out, never written as zero.</summary>
public readonly record struct TakeChannels(bool Height, bool Tilt, bool Twist)
{
    public static readonly TakeChannels All = new(true, true, true);
}

/// <summary>What the session counted beneath the recorder, where it can say.</summary>
public sealed record SessionCounts(long PacketsFromTheDriver, long PacketsOutsideTheCaptureRegion, long PointsDelivered);

/// <summary>Everything about a recording that the readings do not carry.</summary>
public sealed record TakeDescription(
    string Id,
    string Gesture,
    string Intent,
    string Username,
    string Notes,
    DateTimeOffset RecordedAt,
    TakeDevice Device,
    TabletSpace Space,
    TakeChannels Channels,
    SessionCounts? Counted = null);

/// <summary>
/// Writes a <see cref="SegmentedTake"/> as a <c>stroke-field-guide/take</c> recording, format version 8, with
/// positions in the tablet's own digitizer counts (<c>coordinates.space = "tablet"</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Columns are declared once.</b> <see cref="Columns"/> is the single table that both names a column and
/// produces its cell, and the file's <c>columns</c> array and every row are built from it, so the declared
/// order and the written order cannot disagree. StrokeRecorder learned this the expensive way; the same rule
/// is kept here without sharing the code. The schema in the StrokeCorpus repository is the check that the two
/// writers have not drifted: the tests validate this output against a copy of it.
/// </para>
/// <para>
/// This recording has no pen timestamp (OpenTabletDriver does not give one) and no status word, so it carries
/// no <c>at</c> or <c>status</c> column. It also has no placement, because there is no desktop to place. The
/// <c>arrived</c> column is the host clock, microseconds, rebased to the first reading in contact; approach
/// readings come before that and are therefore negative.
/// </para>
/// </remarks>
public static class StrokeTakeWriter
{
    public const string Format = "stroke-field-guide/take";
    public const int Version = 8;

    public const string Clocks =
        "'arrived' is this application's clock, in microseconds from the take's first reading in contact, "
        + "stamped when each report reached OpenTabletArtist from the OpenTabletDriver daemon. Each report has its "
        + "own value; they are not shared per batch. There is no pen timestamp in this recording: the driver does "
        + "not provide one. Readings before the first contact (an approach) are negative.";

    /// <summary>What a stroke's lack of a hover reading is put down to, when there was none at all.</summary>
    private const string NeverSeen = "the pen was not reported in the air at all";

    /// <summary>What x and y are, as the file says it.</summary>
    private const string Units = "digitizer counts";

    private sealed record Column(string Name, Func<TakeChannels, bool> Carried, Func<TabletReading, long, TakeDevice, string> Cell);

    /// <summary>The known columns in file order; the file carries the ones its channels measured.</summary>
    private static readonly Column[] Columns =
    [
        new("arrived", _ => true, (r, began, _) => Whole(r.ArrivedUs - began)),
        new("x", _ => true, (r, _, _) => Round(r.X, 3)),
        new("y", _ => true, (r, _, _) => Round(r.Y, 3)),
        new("pressure", _ => true, (r, _, _) => Whole((long)Math.Round(r.Pressure))),
        new("height", c => c.Height, (r, _, _) => r.Height is { } h
            ? Whole(h)
            : throw new InvalidOperationException("A reading has no height, but the recording says it measured one.")),
        new("lean", c => c.Tilt, (r, _, _) => Round(r.Lean, 2)),
        new("azimuth", c => c.Tilt, (r, _, _) => Round(r.Azimuth, 2)),
        new("twist", c => c.Twist, (r, _, _) => Round(r.Twist, 2)),
    ];

    /// <summary>The column names a recording with these channels declares, in order.</summary>
    public static IReadOnlyList<string> ColumnNames(TakeChannels channels) =>
        [.. Columns.Where(c => c.Carried(channels)).Select(c => c.Name)];

    /// <summary>Writes the recording to <paramref name="folder"/> without overwriting; returns where it went.</summary>
    public static string Write(SegmentedTake take, TakeDescription description, string folder, string name)
    {
        Directory.CreateDirectory(folder);

        var path = Free(folder, name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
        var partial = path + ".writing";

        // Written beside the target and moved into place, so a failure part way leaves neither file half-made.
        File.WriteAllText(partial, ToJson(take, description with { Id = Path.GetFileNameWithoutExtension(path) }), new UTF8Encoding(false));
        File.Move(partial, path);

        return path;
    }

    private static string Free(string folder, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var path = Path.Combine(folder, name);

        for (var next = 2; File.Exists(path); next++)
        {
            path = Path.Combine(folder, $"{stem}-{next}.json");
        }

        return path;
    }

    /// <summary>A file name nobody has to think about: the gesture, the tablet and the moment.</summary>
    public static string Suggest(string gesture, string tablet, DateTimeOffset at) =>
        string.Join("-", new[] { gesture, Slug(tablet), at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) }
            .Where(p => p.Length > 0));

    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        var dash = false;

        foreach (var letter in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(letter))
            {
                slug.Append(letter);
                dash = false;
            }
            else if (!dash && slug.Length > 0)
            {
                slug.Append('-');
                dash = true;
            }
        }

        return slug.ToString().Trim('-');
    }

    public static string ToJson(SegmentedTake take, TakeDescription d)
    {
        var channels = d.Channels;
        var columns = Columns.Where(c => c.Carried(channels)).ToArray();

        // Every arrival is relative to the first reading in contact, or on a take that never touched down the
        // first airborne one, so the gaps between strokes stay measurable.
        var began = take.Strokes.FirstOrDefault(s => s.Readings.Count > 0)?.Readings[0].ArrivedUs
                    ?? (take.Aloft.Count > 0 ? take.Aloft[0].ArrivedUs : 0);

        using var stream = new MemoryStream();
        // Relaxed escaping: the default writes every apostrophe as ', which is valid and unreadable in the
        // conventions text and in anything a person types. The output is UTF-8, so nothing needs escaping to be safe.
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            json.WriteStartObject();

            json.WriteString("format", Format);
            json.WriteNumber("formatVersion", Version);
            json.WriteString("id", d.Id);
            json.WriteString("gesture", d.Gesture);
            json.WriteString("intent", d.Intent);
            json.WriteString("username", d.Username);
            json.WriteString("notes", d.Notes);
            json.WriteString("recordedAt", d.RecordedAt.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("endedBy", take.EndedBy);
            json.WriteNumber("strokeCount", take.Strokes.Count);
            json.WriteBoolean("keptEveryAirborneReading", take.KeptAirborne);

            json.WriteNumber("readingsHandedToTheRecorder", take.Ledger.Routed);
            json.WriteNumber("readingsDroppedForBeingOffThePad", 0);
            json.WriteNumber("readingsAfterTheRecordingStopped", take.Ledger.AfterTheStop);
            json.WriteNumber("readingsAirborneAndNotKept", take.Ledger.ExcludedAirborne);
            json.WriteNumber("readingsAirborneKeptWithAStroke", take.Ledger.KeptAlongside);

            if (d.Counted is { } counted)
            {
                json.WriteStartObject("whatTheSessionCounted");
                json.WriteNumber("packetsFromTheDriver", counted.PacketsFromTheDriver);
                json.WriteNumber("packetsOutsideTheCaptureRegion", counted.PacketsOutsideTheCaptureRegion);
                json.WriteNumber("pointsDelivered", counted.PointsDelivered);
                json.WriteEndObject();
            }

            json.WriteStartObject("device");
            json.WriteString("tablet", d.Device.Tablet);
            json.WriteString("driver", d.Device.Driver);
            json.WriteString("firmware", d.Device.Firmware);
            json.WriteString("api", d.Device.Api);
            json.WriteNumber("fullScalePressure", d.Device.FullScalePressure);
            json.WriteString("conventions", d.Device.Conventions);
            json.WriteEndObject();

            json.WriteStartObject("coordinates");
            json.WriteString("space", "tablet");
            json.WriteString("units", Units);
            json.WriteNumber("maxX", d.Space.MaxX);
            json.WriteNumber("maxY", d.Space.MaxY);
            json.WriteNumber("widthMm", d.Space.WidthMm);
            json.WriteNumber("heightMm", d.Space.HeightMm);
            json.WriteEndObject();

            json.WriteString("clocks", Clocks);

            json.WriteStartArray("columns");
            foreach (var column in columns) json.WriteStringValue(column.Name);
            json.WriteEndArray();

            json.WriteStartArray("strokes");
            foreach (var stroke in take.Strokes)
            {
                json.WriteStartObject();
                json.WriteString("endedBy", stroke.EndedBy);
                json.WriteNumber("readingCount", stroke.Readings.Count);

                // Why an approach is empty, where it is: a large gap means the pen was out of range, a small
                // one with no approach would be a fault.
                if (stroke.SinceLastSeenUs is { } since)
                {
                    json.WriteNumber("lastSeenInTheAirMs", Math.Round(since / 1000.0, 1));
                }
                else
                {
                    json.WriteString("lastSeenInTheAir", NeverSeen);
                }

                if (stroke.Approach.Count > 0)
                {
                    json.WritePropertyName("approach");
                    json.WriteRawValue(Rows(stroke.Approach, columns, began, d.Device), skipInputValidation: true);
                }

                if (stroke.Departure.Count > 0)
                {
                    json.WritePropertyName("departure");
                    json.WriteRawValue(Rows(stroke.Departure, columns, began, d.Device), skipInputValidation: true);
                }

                json.WritePropertyName("readings");
                json.WriteRawValue(Rows(stroke.Readings, columns, began, d.Device), skipInputValidation: true);

                json.WriteEndObject();
            }

            json.WriteEndArray();

            if (take.KeptAirborne)
            {
                json.WriteString("aloftNote",
                    "Every reading taken with the tip up, unfiltered. Recorded to see what the recorder is choosing "
                    + "to drop. Not evidence about a stroke.");
                json.WritePropertyName("aloft");
                json.WriteRawValue(Rows(take.Aloft, columns, began, d.Device), skipInputValidation: true);
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>One reading to a line: an indenting writer would put every number on its own.</summary>
    private static string Rows(IReadOnlyList<TabletReading> readings, Column[] columns, long began, TakeDevice device)
    {
        if (readings.Count == 0) return "[]";

        var rows = new StringBuilder();
        rows.AppendLine("[");

        for (var i = 0; i < readings.Count; i++)
        {
            rows.Append("        [")
                .Append(string.Join(", ", columns.Select(c => c.Cell(readings[i], began, device))))
                .AppendLine(i == readings.Count - 1 ? "]" : "],");
        }

        return rows.Append("      ]").ToString();
    }

    private static string Round(double value, int places) =>
        Math.Round(value, places).ToString("R", CultureInfo.InvariantCulture);

    private static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);
}
