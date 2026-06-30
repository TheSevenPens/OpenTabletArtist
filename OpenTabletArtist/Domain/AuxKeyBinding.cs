using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
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

/// <summary>
/// Reads/writes a single-key "Key Binding" (OTD's <c>KeyBinding</c>) on a tablet's auxiliary buttons.
/// The simplest mapping: an express key sends one keyboard key. An unbound button is a <c>null</c>
/// entry in the profile's AuxButtons collection (matching how OTD stores "no binding").
/// </summary>
public static class AuxKeyBinding
{
    public const string KeyBindingPath = "OpenTabletDriver.Desktop.Binding.KeyBinding";
    public const string MultiKeyBindingPath = "OpenTabletDriver.Desktop.Binding.MultiKeyBinding";

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
        var store = JsonConvert.DeserializeObject<PluginSettingStore>(
            $$"""{"Path":"{{KeyBindingPath}}","Enable":true,"Settings":[]}""")!;
        store.Settings ??= new ObservableCollection<PluginSetting>();
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

        var store = JsonConvert.DeserializeObject<PluginSettingStore>(
            $$"""{"Path":"{{MultiKeyBindingPath}}","Enable":true,"Settings":[]}""")!;
        store.Settings ??= new ObservableCollection<PluginSetting>();
        store.Settings.Add(new PluginSetting("Keys", string.Join("+", parts)));
        return store;
    }

    /// <summary>The keys offered in the per-button picker — a curated subset of OTD's valid keys
    /// (digits show as "0".."9" but store as OTD's "D0".."D9").</summary>
    public static IReadOnlyList<KeyOption> Options { get; } = BuildOptions();

    private static IReadOnlyList<KeyOption> BuildOptions()
    {
        var list = new List<KeyOption> { new("None", None) };
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
