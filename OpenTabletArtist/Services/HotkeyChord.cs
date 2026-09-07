using System.Collections.Generic;
using Avalonia.Input;

namespace OpenTabletArtist.Services;

/// <summary>
/// A global-hotkey chord (modifiers + a key), portable across the capture UI (Avalonia
/// <see cref="Key"/>/<see cref="KeyModifiers"/>) and Win32 <c>RegisterHotKey</c> (which needs a modifier
/// mask + a virtual-key code). Serialized as an Avalonia <see cref="KeyGesture"/> string so it round-trips
/// cleanly in <c>AppSettings</c>. (#320)
/// </summary>
public sealed record HotkeyChord(KeyModifiers Modifiers, Key Key)
{
    // Win32 RegisterHotKey modifier flags.
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Human-readable form, e.g. "Ctrl+Alt+9". Built here rather than taken from
    /// <see cref="KeyGesture.ToString"/>, which spells the number row with Avalonia's enum names —
    /// "Ctrl+Alt+D9" for the key printed 9 on the keyboard. Modifier order and key labels match the
    /// capture dialog's chips, so the shortcut you pick is written the same way in the list.
    /// </summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
            parts.Add(KeyLabel(Key));
            return string.Join("+", parts);
        }
    }

    /// <summary>What a key is printed as on the keyboard. Only the two ranges whose enum names differ
    /// from their legend need translating; the numpad keeps a "Num " prefix because the dialog tells it
    /// apart from the number row by column, and a chord on its own has no column.</summary>
    private static string KeyLabel(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (char)('0' + (key - Key.NumPad0)),
        _ => key.ToString(),
    };

    /// <summary>Stable string for persistence — a <see cref="KeyGesture"/> string, because
    /// <see cref="TryParse"/> reads it back with <see cref="KeyGesture.Parse"/>. Deliberately NOT
    /// <see cref="Display"/>: that one is for people and is free to change.</summary>
    public string Serialize() => new KeyGesture(Key, Modifiers).ToString();

    public static bool TryParse(string? text, out HotkeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var g = KeyGesture.Parse(text);
            chord = new HotkeyChord(g.KeyModifiers, g.Key);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Win32 modifier mask for RegisterHotKey.</summary>
    public uint Win32Modifiers
    {
        get
        {
            uint m = 0;
            if (Modifiers.HasFlag(KeyModifiers.Alt)) m |= MOD_ALT;
            if (Modifiers.HasFlag(KeyModifiers.Control)) m |= MOD_CONTROL;
            if (Modifiers.HasFlag(KeyModifiers.Shift)) m |= MOD_SHIFT;
            if (Modifiers.HasFlag(KeyModifiers.Meta)) m |= MOD_WIN;
            return m;
        }
    }

    /// <summary>Win32 virtual-key code, or 0 for a key we can't map (letters, digits, numpad, F-keys are
    /// covered — the realistic hotkey set). A chord needs a mapped key and at least one modifier to be
    /// registerable (a bare key would hijack normal typing).</summary>
    public uint Win32VirtualKey => Key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (Key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (Key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (Key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (Key - Key.F1)),
        _ => 0,
    };

    /// <summary>Registerable = a mappable key with at least one modifier (so it can't clobber typing).</summary>
    public bool IsRegisterable => Win32VirtualKey != 0 && Win32Modifiers != 0;
}
