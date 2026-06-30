using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OtdArtist.Domain;

namespace OtdArtist.Dynamics;

/// <summary>
/// OpenTabletDriver filter implementing the OTD Artist pointer calibration (#127): a 2D
/// affine correction in normalized tablet space, fit from the app's 4-tap calibration. It runs at
/// <see cref="PipelinePosition.PreTransform"/> — before OTD's absolute output transform — so the
/// captured correction composes with whatever area mapping the user has set.
///
/// Order it <em>before</em> the Pen Dynamics filter (also PreTransform): correct the raw position
/// first so position smoothing never smears an uncorrected error. The matrix math is source-shared
/// with the app via <see cref="CalibrationMath"/> (unit-tested), so the daemon and the app agree.
/// </summary>
[PluginName("OTD Artist - Calibration")]
public class CalibrationFilter : IPositionedPipelineElement<IDeviceReport>
{
    // The affine is stored as a System.Numerics.Matrix3x2 (x' = M11·x + M21·y + M31, etc.) in
    // normalized tablet space. Defaults form the identity (no correction). Used for legacy stores and
    // when Model is empty/"Affine".
    [Property("M11"), DefaultPropertyValue(1f)] public float M11 { get; set; } = 1f;
    [Property("M12"), DefaultPropertyValue(0f)] public float M12 { get; set; }
    [Property("M21"), DefaultPropertyValue(0f)] public float M21 { get; set; }
    [Property("M22"), DefaultPropertyValue(1f)] public float M22 { get; set; } = 1f;
    [Property("M31"), DefaultPropertyValue(0f)] public float M31 { get; set; }
    [Property("M32"), DefaultPropertyValue(0f)] public float M32 { get; set; }

    // Correction model: "" / "Affine" → the M-matrix above; "Homography" → Homography (perspective,
    // #195); "Grid" → CalibrationGrid (bilinear offsets, #196). The CSV payloads are parsed once on set.
    [Property("Model")] public string Model { get; set; } = "";

    private Homography? _homography;
    [Property("Homography")]
    public string HomographyData
    {
        get => _homographyData;
        set { _homographyData = value; _homography = Homography.TryParse(value); }
    }
    private string _homographyData = "";

    private CalibrationGrid? _grid;
    [Property("Grid")]
    public string GridData
    {
        get => _gridData;
        set { _gridData = value; _grid = CalibrationGrid.TryParse(value); }
    }
    private string _gridData = "";

    // Identifies the area mapping this calibration was captured against, so the app can warn when it
    // may be stale. Opaque to the filter.
    [Property("Mapping Fingerprint")] public string MappingFingerprint { get; set; } = "";

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;

    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport value)
    {
        var digitizer = Tablet?.Properties?.Specifications?.Digitizer;
        // Need the digitizer maxima to normalize; pass through untouched if they're unknown.
        if (value is ITabletReport report && digitizer is { MaxX: > 0, MaxY: > 0 })
        {
            var norm = CalibrationMath.ToNormalized(report.Position, digitizer.MaxX, digitizer.MaxY);
            Vector2? corrected = Model switch
            {
                "Homography" => _homography?.Project(norm),
                "Grid" => _grid?.Apply(norm),
                _ => ApplyAffine(norm),
            };
            if (corrected is { } c)
                report.Position = CalibrationMath.FromNormalized(c, digitizer.MaxX, digitizer.MaxY);
        }

        Emit?.Invoke(value);
    }

    private Vector2? ApplyAffine(Vector2 norm)
    {
        var m = new Matrix3x2(M11, M12, M21, M22, M31, M32);
        return m.IsIdentity ? null : Vector2.Transform(norm, m);
    }
}
