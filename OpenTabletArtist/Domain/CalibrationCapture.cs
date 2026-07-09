using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTabletArtist.Domain;

/// <summary>
/// A portable, versioned snapshot of one pointer calibration (#484). It carries the raw capture — the
/// <em>inputs</em> a solver needs (the mapping <see cref="Context"/> plus each target ↔ raw-tap pair) — so
/// the same taps can be re-solved with any algorithm without re-tapping, plus the <see cref="Solved"/>
/// model that was actually applied so it can be restored byte-for-byte as a known-good state.
/// Serialized as JSON so captures are human-readable, diffable, and usable as test fixtures.
/// Coordinates: <see cref="CalibrationCapturePoint.TargetDesktopX"/>/Y are virtual-desktop px (unlike the
/// display-relative report), raw is tablet digitizer units — the exact pair the solver consumes.
/// </summary>
public sealed record CalibrationCapture(
    int SchemaVersion,
    string Tablet,
    string CapturedAt,
    string Mode,          // "Corners" or "Grid"
    int Cols,
    int Rows,
    CalibrationCaptureContext Context,
    IReadOnlyList<CalibrationCapturePoint> Points,
    CalibrationSolvedModel? Solved)
{
    /// <summary>Current on-disk schema. Bump when the shape changes incompatibly; readers refuse a
    /// higher version. Older versions stay readable as long as the record deserializes.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Parse a capture, or null if the text isn't a valid capture JSON.</summary>
    public static CalibrationCapture? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<CalibrationCapture>(json, JsonOpts); }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    // ---- Typed views of the stored context/points, in the shapes the solver consumes ----

    [JsonIgnore]
    public TabletDigitizerSpec DigitizerSpec =>
        new(Context.Digitizer.Width, Context.Digitizer.Height, Context.Digitizer.MaxX, Context.Digitizer.MaxY);

    [JsonIgnore]
    public MappingArea InputArea =>
        new(Context.Input.CenterX, Context.Input.CenterY, Context.Input.Width, Context.Input.Height, Context.Input.Rotation);

    [JsonIgnore]
    public MappingArea OutputArea =>
        new(Context.Output.CenterX, Context.Output.CenterY, Context.Output.Width, Context.Output.Height, Context.Output.Rotation);

    [JsonIgnore]
    public IReadOnlyList<Vector2> TargetsDesktop =>
        Points.Select(p => new Vector2(p.TargetDesktopX, p.TargetDesktopY)).ToList();

    [JsonIgnore]
    public IReadOnlyList<Vector2> MeasuredRaw =>
        Points.Select(p => new Vector2(p.RawX, p.RawY)).ToList();

    [JsonIgnore]
    public bool IsGrid => string.Equals(Mode, "Grid", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The mapping the capture was taken against — everything the solver needs beyond the taps.
/// Import is matching-only: a capture only applies to a tablet whose current context matches this.</summary>
public sealed record CalibrationCaptureContext(
    CaptureDigitizer Digitizer,
    CaptureArea Input,
    CaptureArea Output,
    CaptureDisplay Display);

/// <summary>Digitizer geometry (mirrors <see cref="TabletDigitizerSpec"/>). MaxX/MaxY define the raw
/// unit range, so a capture is only meaningful on a tablet with the same range.</summary>
public sealed record CaptureDigitizer(float Width, float Height, float MaxX, float MaxY);

/// <summary>A mapping area, centre-positioned (mirrors <see cref="MappingArea"/>).</summary>
public sealed record CaptureArea(float CenterX, float CenterY, float Width, float Height, float Rotation);

/// <summary>The calibrated display's identity + desktop geometry (to convert desktop ↔ display-relative).</summary>
public sealed record CaptureDisplay(int Number, string Name, int X, int Y, int Width, int Height);

/// <summary>One captured tap: the on-screen target (virtual-desktop px) and the raw tablet coordinate the
/// pen reported there, how many samples were averaged, and the averaged pen tilt (TiltX/TiltY in degrees,
/// NaN when the tablet doesn't report tilt — #481).</summary>
public sealed record CalibrationCapturePoint(
    float TargetDesktopX, float TargetDesktopY, float RawX, float RawY, int Samples,
    float TiltX = float.NaN, float TiltY = float.NaN);

/// <summary>The solved model that was applied when the capture was taken, so it can be restored exactly.
/// Only the field matching <see cref="Model"/> is populated: Affine → <see cref="Transform"/>
/// ("m11,m12,m21,m22,m31,m32"), Homography/Grid → their CSV payloads.</summary>
public sealed record CalibrationSolvedModel(string Model, string Transform, string Homography, string Grid);
