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

    /// <summary>Human-readable form, e.g. "Ctrl + Alt + D1".</summary>
    public string Display => new KeyGesture(Key, Modifiers).ToString();

    /// <summary>Stable string for persistence (parses back via <see cref="TryParse"/>).</summary>
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
