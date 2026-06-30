using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection;

namespace OpenTabletArtist.Domain;

/// <summary>A keyboard key offered in the express-key picker, with a friendly label.</summary>
public sealed record KeyOption(string Display, string Value);

/// <summary>
/// Reads/writes a single-key "Key Binding" (OTD's <c>KeyBinding</c>) on a tablet's auxiliary buttons.
/// The simplest mapping: an express key sends one keyboard key. An unbound button is a <c>null</c>
/// entry in the profile's AuxButtons collection (matching how OTD stores "no binding").
/// </summary>
public static class AuxKeyBinding
{
    public const string KeyBindingPath = "OpenTabletDriver.Desktop.Binding.KeyBinding";

    /// <summary>Picker sentinel + OTD's own key name for "no key": maps to an unbound button.</summary>
    public const string None = "None";

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
