using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>A keyboard key offered in the express-key picker, with a friendly label.</summary>
public sealed record KeyOption(string Display, string Value);

/// <summary>A keystroke an express key can send: optional Ctrl/Shift/Alt modifiers plus one key.
/// No modifiers maps to a plain Key Binding; with modifiers, a Multi-Key Binding.</summary>
public sealed record AuxCombo(bool Ctrl, bool Shift, bool Alt, string Key)
{
    public static readonly AuxCombo Unbound = new(false, false, false, AuxKeyBinding.None);
    public bool IsBound => !string.IsNullOrEmpty(Key) && Key != AuxKeyBinding.None;
}

/// <summary>The kind of action an express key performs. <see cref="None"/> is an explicit "do
/// nothing" so an intentionally-unbound button reads clearly in the UI.</summary>
public enum AuxKind { None, Keyboard, Mouse, Scroll }

/// <summary>A full express-key binding the editor can model: nothing, a keyboard <see cref="AuxCombo"/>,
/// a mouse button, or a scroll direction. Carries each value so the UI keeps every editor's state
/// while switching type.</summary>
public sealed record AuxBinding(AuxKind Kind, AuxCombo Combo, string MouseButton, string Scroll)
{
    public static readonly AuxBinding Unbound =
        new(AuxKind.None, AuxCombo.Unbound, AuxKeyBinding.None, AuxKeyBinding.None);

    public bool IsBound => Kind switch
    {
        AuxKind.Keyboard => Combo.IsBound,
        AuxKind.Mouse => IsSet(MouseButton),
        AuxKind.Scroll => IsSet(Scroll),
        _ => false,
    };

    private static bool IsSet(string v) => !string.IsNullOrEmpty(v) && v != AuxKeyBinding.None;
}

/// <summary>
/// Reads/writes a single-key "Key Binding" (OTD's <c>KeyBinding</c>) on a tablet's auxiliary buttons.
/// The simplest mapping: an express key sends one keyboard key. An unbound button is a <c>null</c>
/// entry in the profile's AuxButtons collection (matching how OTD stores "no binding").
/// </summary>
public static class AuxKeyBinding
{
    public const string KeyBindingPath = "OpenTabletDriver.Desktop.Binding.KeyBinding";
    public const string MultiKeyBindingPath = "OpenTabletDriver.Desktop.Binding.MultiKeyBinding";
    public const string MouseBindingPath = "OpenTabletDriver.Desktop.Binding.MouseBinding";
    public const string MouseScrollBindingPath = "OpenTabletDriver.Desktop.Binding.MouseScrollBinding";

    // One wheel tick (Windows/Linux). Negative scrolls up/left, positive down/right.
    private const int ScrollTick = 120;
    private const string Up = "Up", Down = "Down", Left = "Left", Right = "Right";

    /// <summary>Picker sentinel + OTD's own key name for "no key": maps to an unbound button.</summary>
    public const string None = "None";

    // Modifier key names OTD accepts; we recognize the Left/Right variants when reading but always
    // write the side-agnostic canonical form.
    private static readonly string[] CtrlNames = { "Control", "LeftControl", "RightControl" };
    private static readonly string[] ShiftNames = { "Shift", "LeftShift", "RightShift" };
    private static readonly string[] AltNames = { "Alt", "LeftAlt", "RightAlt" };

    /// <summary>The assigned key for an aux store, or <see cref="None"/> when unbound or the store is
    /// some other (non single-key) binding.</summary>
    public static string ReadKey(PluginSettingStore? store)
    {
        if (store?.Path != KeyBindingPath) return None;
        var key = store.Settings?.FirstOrDefault(s => s.Property == "Key")?.Value?.ToString();
        return string.IsNullOrEmpty(key) ? None : key;
    }

    /// <summary>A <c>KeyBinding</c> store for <paramref name="key"/>, or null (unbound) for
    /// <see cref="None"/>/empty. Built by path since the app doesn't reference the binding type.</summary>
    public static PluginSettingStore? MakeKeyBinding(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == None) return null;
        var store = NewStore(KeyBindingPath);
        store.Settings.Add(new PluginSetting("Key", key));
        return store;
    }

    /// <summary>Parse an aux store into a (modifiers + key) combo. Returns null when the binding is
    /// something this editor doesn't model — a different binding type, or a multi-key macro that
    /// isn't "modifiers + one key" — so the UI can flag it instead of misrepresenting it.</summary>
    public static AuxCombo? ReadCombo(PluginSettingStore? store)
    {
        if (store?.Path == null) return AuxCombo.Unbound; // unbound button
        if (store.Path == KeyBindingPath)
            return new AuxCombo(false, false, false, ReadKey(store));
        if (store.Path == MultiKeyBindingPath)
        {
            var keysStr = store.Settings?.FirstOrDefault(s => s.Property == "Keys")?.Value?.ToString();
            if (string.IsNullOrEmpty(keysStr)) return AuxCombo.Unbound;

            bool ctrl = false, shift = false, alt = false;
            string? mainKey = null;
            foreach (var seg in keysStr.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (CtrlNames.Contains(seg)) ctrl = true;
                else if (ShiftNames.Contains(seg)) shift = true;
                else if (AltNames.Contains(seg)) alt = true;
                else if (mainKey == null) mainKey = seg;
                else return null; // more than one non-modifier key — beyond our simple model
            }
            return mainKey == null ? null : new AuxCombo(ctrl, shift, alt, mainKey);
        }
        return null; // some other binding type (mouse, scroll, …)
    }

    /// <summary>Build the right store for a combo: a Key Binding when there are no modifiers, a
    /// Multi-Key Binding when there are, or null (unbound) when there's no key.</summary>
    public static PluginSettingStore? MakeBinding(AuxCombo combo)
    {
        if (!combo.IsBound) return null;
        if (!combo.Ctrl && !combo.Shift && !combo.Alt) return MakeKeyBinding(combo.Key);

        var parts = new List<string>();
        if (combo.Ctrl) parts.Add("Control");
        if (combo.Shift) parts.Add("Shift");
        if (combo.Alt) parts.Add("Alt");
        parts.Add(combo.Key);

        var store = NewStore(MultiKeyBindingPath);
        store.Settings.Add(new PluginSetting("Keys", string.Join("+", parts)));
        return store;
    }

    /// <summary>The mouse button assigned to an aux store, or "None" when unbound / not a mouse binding.</summary>
    public static string ReadMouseButton(PluginSettingStore? store)
    {
        if (store?.Path != MouseBindingPath) return None;
        var b = store.Settings?.FirstOrDefault(s => s.Property == "Button")?.Value?.ToString();
        return string.IsNullOrEmpty(b) ? None : b;
    }

    /// <summary>A Mouse Button Binding store, or null (unbound) for "None"/empty.</summary>
    public static PluginSettingStore? MakeMouseBinding(string? button)
    {
        if (string.IsNullOrEmpty(button) || button == None) return null;
        var store = NewStore(MouseBindingPath);
        store.Settings.Add(new PluginSetting("Button", button));
        return store;
    }

    /// <summary>The scroll direction (Up/Down/Left/Right) for an aux store, or "None" when unbound /
    /// not a scroll binding.</summary>
    public static string ReadScroll(PluginSettingStore? store)
    {
        if (store?.Path != MouseScrollBindingPath) return None;
        var direction = store.Settings?.FirstOrDefault(s => s.Property == "Direction")?.Value?.ToString();
        var amount = store.Settings?.FirstOrDefault(s => s.Property == "Amount") is { HasValue: true } s
            ? s.GetValue<int>() : ScrollTick;
        var horizontal = string.Equals(direction, "Horizontal", StringComparison.OrdinalIgnoreCase);
        return horizontal ? (amount < 0 ? Left : Right) : (amount < 0 ? Up : Down);
    }

    /// <summary>A Mouse Scroll Binding store for a direction, or null (unbound) for "None".</summary>
    public static PluginSettingStore? MakeScrollBinding(string? scroll)
    {
        if (string.IsNullOrEmpty(scroll) || scroll == None) return null;
        var horizontal = scroll is Left or Right;
        var amount = scroll is Up or Left ? -ScrollTick : ScrollTick;
        var store = NewStore(MouseScrollBindingPath);
        store.Settings.Add(new PluginSetting("Direction", horizontal ? "Horizontal" : "Vertical"));
        store.Settings.Add(new PluginSetting("Amount", amount));
        store.Settings.Add(new PluginSetting("Interval", 300)); // OTD's default auto-repeat interval
        return store;
    }

    /// <summary>Parse any aux store into the editor's unified model, or null when it's a binding the
    /// editor can't represent (adaptive, Windows Ink, or a multi-key macro). An unbound or
    /// value-less store reads as <see cref="AuxBinding.Unbound"/> (the explicit None type).</summary>
    public static AuxBinding? ReadBinding(PluginSettingStore? store)
    {
        if (store?.Path == null) return AuxBinding.Unbound;
        if (store.Path == MouseBindingPath)
        {
            var button = ReadMouseButton(store);
            return button == None ? AuxBinding.Unbound
                : new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, button, None);
        }
        if (store.Path == MouseScrollBindingPath)
            return new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, None, ReadScroll(store));
        var combo = ReadCombo(store);
        if (combo == null) return null;                       // a binding the editor can't model
        return combo.IsBound ? new AuxBinding(AuxKind.Keyboard, combo, None, None) : AuxBinding.Unbound;
    }

    /// <summary>Build the store for a unified binding (keyboard / mouse button / scroll), or null
    /// (unbound) for the None type.</summary>
    public static PluginSettingStore? MakeBinding(AuxBinding binding) => binding.Kind switch
    {
        AuxKind.Keyboard => MakeBinding(binding.Combo),
        AuxKind.Mouse => MakeMouseBinding(binding.MouseButton),
        AuxKind.Scroll => MakeScrollBinding(binding.Scroll),
        _ => null,
    };

    /// <summary>Human-readable summary of a binding for the read-only card: "Ctrl + Z", "Left click",
    /// "Scroll up", or "Unbound".</summary>
    public static string Describe(AuxBinding binding) => binding.Kind switch
    {
        AuxKind.Keyboard => binding.Combo.IsBound ? DescribeCombo(binding.Combo) : "Unbound",
        AuxKind.Mouse => IsBoundValue(binding.MouseButton) ? DescribeMouse(binding.MouseButton) : "Unbound",
        AuxKind.Scroll => IsBoundValue(binding.Scroll) ? $"Scroll {binding.Scroll.ToLowerInvariant()}" : "Unbound",
        _ => "Unbound",
    };

    private static bool IsBoundValue(string v) => !string.IsNullOrEmpty(v) && v != None;

    private static string DescribeCombo(AuxCombo c)
    {
        var parts = new List<string>();
        if (c.Ctrl) parts.Add("Ctrl");
        if (c.Shift) parts.Add("Shift");
        if (c.Alt) parts.Add("Alt");
        parts.Add(KeyDisplay(c.Key));
        return string.Join(" + ", parts);
    }

    /// <summary>The friendly display name for a stored key value (e.g. "D0" → "0"), from the picker list.</summary>
    private static string KeyDisplay(string value) => Options.FirstOrDefault(o => o.Value == value)?.Display ?? value;

    private static string DescribeMouse(string button) => button switch
    {
        "Left" => "Left click",
        "Right" => "Right click",
        "Middle" => "Middle click",
        "Backward" => "Back button",
        "Forward" => "Forward button",
        _ => button,
    };

    /// <summary>Scroll directions offered in the picker. No "None" — unbinding is the None type.</summary>
    public static IReadOnlyList<KeyOption> ScrollOptions { get; } = new List<KeyOption>
    {
        new("Up", Up),
        new("Down", Down),
        new("Left", Left),
        new("Right", Right),
    };

    /// <summary>Mouse buttons offered in the picker (OTD's MouseButton enum names; "Back" reads nicer
    /// than the stored "Backward"). No "None" — unbinding is the explicit None binding type.</summary>
    public static IReadOnlyList<KeyOption> MouseButtonOptions { get; } = new List<KeyOption>
    {
        new("Left", "Left"),
        new("Right", "Right"),
        new("Middle", "Middle"),
        new("Back", "Backward"),
        new("Forward", "Forward"),
    };

    private static PluginSettingStore NewStore(string path)
    {
        var store = JsonConvert.DeserializeObject<PluginSettingStore>(
            $$"""{"Path":"{{path}}","Enable":true,"Settings":[]}""")!;
        store.Settings ??= new ObservableCollection<PluginSetting>();
        return store;
    }

    /// <summary>Every key the daemon accepts on this platform, straight from OTD's own validated key
    /// set (Windows → the virtual-keyboard key map), so the picker never drifts from what actually
    /// works. Digits show as "0".."9" but store as OTD's "D0".."D9"; other keys keep OTD's name.
    /// Falls back to a curated subset if OTD's list is unavailable (tests / non-desktop).</summary>
    public static IReadOnlyList<KeyOption> Options { get; } = BuildOptions();

    private static IReadOnlyList<KeyOption> BuildOptions()
    {
        try
        {
            var valid = KeyBinding.ValidKeys;
            if (valid != null)
            {
                var list = valid
                    .Where(k => !string.IsNullOrEmpty(k) && k != None)
                    .Select(k => new KeyOption(KeyDisplayName(k), k))
                    .ToList();
                if (list.Count > 0) return list;
            }
        }
        catch { /* OTD list unavailable — fall back to the curated subset below */ }
        return CuratedOptions();
    }

    /// <summary>Friendly label for a stored key name: bare digit for "D0".."D9", else OTD's own name.</summary>
    private static string KeyDisplayName(string value) =>
        value.Length == 2 && value[0] == 'D' && char.IsDigit(value[1]) ? value[1].ToString() : value;

    private static IReadOnlyList<KeyOption> CuratedOptions()
    {
        var list = new List<KeyOption>();
        for (char c = 'A'; c <= 'Z'; c++) list.Add(new(c.ToString(), c.ToString()));
        for (int d = 0; d <= 9; d++) list.Add(new(d.ToString(), $"D{d}"));
        for (int f = 1; f <= 12; f++) list.Add(new($"F{f}", $"F{f}"));
        foreach (var k in new[] { "Space", "Enter", "Tab", "Escape", "Backspace", "Delete",
                                  "Insert", "Home", "End", "PageUp", "PageDown",
                                  "Up", "Down", "Left", "Right" })
            list.Add(new(k, k));
        return list;
    }
}
