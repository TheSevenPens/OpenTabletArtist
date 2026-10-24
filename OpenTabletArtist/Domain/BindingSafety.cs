using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>A saved button binding that does not suit the tablet's current output mode (#1015).</summary>
public enum BindingHazard
{
    None,
    /// <summary>A mouse click under Windows Ink. The Windows Ink pointer sends the pen out of range and
    /// injects an OS click, which can interrupt a stroke.</summary>
    MouseClickUnderWindowsInk,
    /// <summary>Scroll under Windows Ink. The Windows Ink pointer cannot scroll, so the button does nothing.</summary>
    ScrollUnderWindowsInk,
    /// <summary>The Windows Ink plugin's own binding on a tablet that is not using Windows Ink; the plugin
    /// has no digitizer to talk to, so the button does nothing.</summary>
    WindowsInkBindingWithoutWindowsInk,
}

/// <summary>
/// Judges a saved binding against the output mode. State-based on purpose: it looks only at what is saved
/// now, so it catches a binding set in OTD's own UI exactly as it catches one set here, and it never
/// changes anything (#1015: preserve and warn).
/// </summary>
public static class BindingSafety
{
    /// <summary>True when the output mode path is one of the Windows Ink plugin's modes.</summary>
    public static bool IsWindowsInkOutput(string? outputModePath) =>
        outputModePath != null && outputModePath.Contains("WinInk", StringComparison.OrdinalIgnoreCase);

    public static BindingHazard Assess(PluginSettingStore? store, bool outputIsWindowsInk)
    {
        var path = store?.Path;
        if (path == null) return BindingHazard.None;

        if (outputIsWindowsInk)
        {
            if (path == AuxKeyBinding.MouseBindingPath) return BindingHazard.MouseClickUnderWindowsInk;
            if (path == AuxKeyBinding.MouseScrollBindingPath) return BindingHazard.ScrollUnderWindowsInk;
            return BindingHazard.None;
        }

        return path == PenSwitchBinding.WindowsInkButtonHandlerPath
            ? BindingHazard.WindowsInkBindingWithoutWindowsInk
            : BindingHazard.None;
    }

    /// <summary>Plain-language explanation for the warning on a button, or null when there is nothing to
    /// say. No plugin jargon (#495); says what happens and what to do.</summary>
    public static string? Message(BindingHazard hazard) => hazard switch
    {
        BindingHazard.MouseClickUnderWindowsInk =>
            "This button sends a mouse click, which can interrupt your stroke in Windows Ink mode.",
        BindingHazard.ScrollUnderWindowsInk =>
            "Scrolling doesn't work in Windows Ink mode, so this button does nothing.",
        BindingHazard.WindowsInkBindingWithoutWindowsInk =>
            "This button uses a Windows Ink action, but this tablet isn't in Windows Ink mode, so it won't work.",
        _ => null,
    };

    /// <summary>A mouse right click: the one mouse binding whose intent has a Windows Ink equivalent
    /// (the Barrel Switch), so the only one offered a one-click fix.</summary>
    public static bool IsRightClick(PluginSettingStore? store) =>
        store?.Path == AuxKeyBinding.MouseBindingPath
        && string.Equals(AuxKeyBinding.ReadMouseButton(store), "Right", StringComparison.OrdinalIgnoreCase);
}
