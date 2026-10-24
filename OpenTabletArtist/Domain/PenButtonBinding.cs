using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>What a pen barrel button does, in the three terms the card offers (#1015). <see cref="Other"/>
/// is anything else: a binding set elsewhere (OTD's own UI), a mouse click or scroll, a macro, or a plugin
/// binding. It has no radio of its own and is never rewritten unless the user picks something.</summary>
public enum PenButtonAction
{
    /// <summary>The pen's native behavior: OTD's Adaptive Binding for this button.</summary>
    Default,
    /// <summary>A keyboard shortcut held while the button is held (OTD's Key / Multi-Key Binding).</summary>
    Shortcut,
    /// <summary>Nothing bound. OTD's own representation is a null entry at the button's index.</summary>
    Disabled,
    Other,
}

/// <summary>Reads and writes the three pen-button actions. Built entirely from bindings OTD already ships.</summary>
public static class PenButtonBinding
{
    /// <summary>Adaptive Binding only knows Buttons 1 to 3; a pen with more has no Default action to offer.</summary>
    public const int MaxDefaultActionButton = 3;

    public static bool CanUseDefaultAction(int penButtonIndex) =>
        penButtonIndex >= 1 && penButtonIndex <= MaxDefaultActionButton;

    /// <param name="outputIsWindowsInk">Under Windows Ink only a keyboard binding is the "shortcut" choice: a
    /// mouse click or scroll is the hazard BindingSafety warns about, so it stays <see cref="PenButtonAction.Other"/>.
    /// Under mouse output the shortcut editor also offers those, so they count as the choice.</param>
    public static PenButtonAction Classify(PluginSettingStore? store, int penButtonIndex, bool outputIsWindowsInk)
    {
        if (store?.Path == null) return PenButtonAction.Disabled;

        if (PenSwitchBinding.DetectMode(store, PenSwitchKind.PenButton, penButtonIndex) == PenSwitchBindingMode.Auto)
            return PenButtonAction.Default;

        var binding = AuxKeyBinding.ReadBinding(store);
        if (binding is not { IsBound: true }) return PenButtonAction.Other;
        return binding.Kind == AuxKind.Keyboard || !outputIsWindowsInk ? PenButtonAction.Shortcut : PenButtonAction.Other;
    }

    /// <summary>What the shortcut editor should open on: the saved keyboard / mouse / scroll binding, or
    /// <see cref="AuxBinding.Unbound"/> when the store is something it does not model.</summary>
    public static AuxBinding ReadShortcut(PluginSettingStore? store) =>
        AuxKeyBinding.ReadBinding(store) ?? AuxBinding.Unbound;

    public static PluginSettingStore MakeDefault(int penButtonIndex) =>
        PenSwitchBinding.MakeAdaptiveBinding(PenSwitchKind.PenButton, penButtonIndex);

    /// <summary>Disabled is a null entry, the same representation OTD pads every button list with and that
    /// ExpressKeys already use for "unbound".</summary>
    public static PluginSettingStore? MakeDisabled() => null;

    /// <summary>A short plain-language name for what a button does now ("Barrel Switch", "Ctrl + Z", "Disabled"),
    /// for the summary line. <paramref name="otherLabel"/> names a binding the card cannot model.</summary>
    public static string Describe(PluginSettingStore? store, int penButtonIndex, bool outputIsWindowsInk, string otherLabel)
    {
        switch (Classify(store, penButtonIndex, outputIsWindowsInk))
        {
            case PenButtonAction.Default:
                // Under mouse output the Adaptive Binding is a plain mouse button: 1 right, 2 middle, 3 nothing.
                return outputIsWindowsInk ? "Barrel Switch" : penButtonIndex switch
                {
                    1 => "Right click",
                    2 => "Middle click",
                    _ => "No action",
                };
            case PenButtonAction.Shortcut:
                return AuxKeyBinding.Describe(ReadShortcut(store));
            case PenButtonAction.Disabled:
                return "Disabled";
            default:
                return AuxKeyBinding.ReadBinding(store) is { IsBound: true } b ? AuxKeyBinding.Describe(b) : otherLabel;
        }
    }
}
