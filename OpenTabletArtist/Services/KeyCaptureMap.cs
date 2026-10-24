using Avalonia.Input;

namespace OpenTabletArtist.Services;

/// <summary>Which of the three held modifiers a physical key is, if any.</summary>
public enum CapturedModifier { None, Ctrl, Shift, Alt }

/// <summary>
/// Turns a physical key press (Avalonia <see cref="Key"/>) into the key name OTD's keyboard bindings accept,
/// for the binding editor's Record button (#1015). Only keys OTD can send are mapped; the Windows key and
/// anything unlisted return false so the editor can say so instead of recording a binding that would not work.
/// </summary>
public static class KeyCaptureMap
{
    public static CapturedModifier ModifierOf(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => CapturedModifier.Ctrl,
        Key.LeftShift or Key.RightShift => CapturedModifier.Shift,
        Key.LeftAlt or Key.RightAlt => CapturedModifier.Alt,
        _ => CapturedModifier.None,
    };

    /// <summary>The OTD key name for a non-modifier key, or false when OTD cannot send it.</summary>
    public static bool TryMap(Key key, out string name)
    {
        name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => "D" + (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => "Keypad" + (char)('0' + (key - Key.NumPad0)),
            >= Key.F1 and <= Key.F24 => key.ToString(),

            Key.Space => "Space",
            Key.Tab => "Tab",
            Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.CapsLock => "CapsLock",
            Key.Scroll => "ScrollLock",
            Key.PrintScreen => "PrintScreen",
            Key.NumLock => "NumberLock",
            Key.Pause => "Pause",

            Key.OemMinus => "Minus",
            Key.OemPlus => "Equal",
            Key.OemOpenBrackets => "LeftBracket",
            Key.OemCloseBrackets => "RightBracket",
            Key.OemPipe => "Backslash",
            Key.OemSemicolon => "Semicolon",
            Key.OemQuotes => "Quote",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.OemTilde => "Grave",

            Key.Divide => "Divide",
            Key.Multiply => "Multiply",
            Key.Subtract => "Subtract",
            Key.Add => "Add",
            Key.Decimal => "Decimal",

            _ => "",
        };
        return name.Length > 0;
    }
}
