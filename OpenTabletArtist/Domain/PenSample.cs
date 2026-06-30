namespace OpenTabletArtist.Domain;

/// <summary>
/// One pen reading fed to the Test canvas, normalized so the canvas doesn't care which source
/// produced it. <see cref="X"/>/<see cref="Y"/> are 0..1 across the drawing surface;
/// <see cref="Pressure"/> is 0..1; tilt/twist are in degrees. <see cref="RawX"/>/<see cref="RawY"/>
/// carry the source's pre-normalization coordinates (tablet units for Driver input, control DIPs
/// for App input) — shown in the readouts to help debug coordinate mapping.
/// </summary>
public readonly record struct PenSample(
    double X,
    double Y,
    double RawX,
    double RawY,
    double Pressure,
    double TiltX,
    double TiltY,
    double Twist,
    bool IsDown);

/// <summary>Where the Test canvas gets its pen data.</summary>
public enum PenInputSourceKind
{
    /// <summary>OS pointer input (Windows Ink) — what a drawing app actually receives.</summary>
    App,
    /// <summary>The OTD daemon's DeviceReport stream — the driver's view, before the output stage.</summary>
    Driver,
}

/// <summary>How the Test canvas renders a pen reading (mirrors the web tablet tester's modes).</summary>
public enum PenBrushMode
{
    PressureToSize,
    AzimuthToRotation,
    AltitudeToSize,
    TwistToRotation,
    PointerOnly,
}
