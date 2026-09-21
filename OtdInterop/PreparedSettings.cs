using OpenTabletDriver.Desktop;

namespace OtdInterop;

/// <summary>A detached snapshot of confirmed live settings.</summary>
public sealed record PreparedSettings(Settings Settings);
