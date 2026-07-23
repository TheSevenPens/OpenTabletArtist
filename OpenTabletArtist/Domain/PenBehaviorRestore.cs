using System;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>
/// The one-click fix for the artist-pen-behaviour health bundle (#artist-pen-health): re-enable Windows
/// Ink (Windows only), the pen tip, pressure, and tilt on a profile in a single shot. Pure mutation of the
/// profile so it's unit-testable; the session applies + persists the result.
/// </summary>
public static class PenBehaviorRestore
{
    /// <summary>The VoiD Windows Ink absolute output mode (Windows). Mirrors the constant in
    /// <c>TabletDetailViewModel</c>; the recommended output mode for pressure + tilt.</summary>
    public const string WinInkAbsoluteModePath = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";

    /// <summary>Set <paramref name="profile"/> to the artist-recommended pen behaviour. Returns true if it
    /// changed anything (so the caller can skip a no-op apply).</summary>
    public static bool ToRecommended(Profile profile, bool isWindows)
    {
        var changed = false;
        var b = profile.BindingSettings;

        // Windows Ink on (Windows only — off-Windows the native output already delivers pressure/tilt, and
        // there is no Windows Ink mode to switch to).
        if (isWindows)
        {
            var path = profile.OutputMode?.Path ?? "";
            if (!path.Contains("WinInk", StringComparison.OrdinalIgnoreCase))
            {
                profile.OutputMode ??= new PluginSettingStore(WinInkAbsoluteModePath, true);
                profile.OutputMode.Path = WinInkAbsoluteModePath;
                changed = true;
            }
        }

        if (b.DisablePressure) { b.DisablePressure = false; changed = true; }
        if (b.DisableTilt) { b.DisableTilt = false; changed = true; }

        // The pen tip is "disabled" when it has no binding (#493); restore an Adaptive (Tip) default when
        // it's missing so tapping registers again.
        if (b.TipButton?.Path == null)
        {
            b.TipButton = PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.Tip);
            changed = true;
        }

        return changed;
    }
}
