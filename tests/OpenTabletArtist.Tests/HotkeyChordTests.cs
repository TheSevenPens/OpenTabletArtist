using Avalonia.Input;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class HotkeyChordTests
{
    [Fact]
    public void Win32VirtualKey_MapsCommonRanges()
    {
        Assert.Equal(0x41u, new HotkeyChord(KeyModifiers.Control, Key.A).Win32VirtualKey);   // 'A'
        Assert.Equal(0x5Au, new HotkeyChord(KeyModifiers.Control, Key.Z).Win32VirtualKey);   // 'Z'
        Assert.Equal(0x31u, new HotkeyChord(KeyModifiers.Control, Key.D1).Win32VirtualKey);  // '1'
        Assert.Equal(0x60u, new HotkeyChord(KeyModifiers.Control, Key.NumPad0).Win32VirtualKey);
        Assert.Equal(0x70u, new HotkeyChord(KeyModifiers.Control, Key.F1).Win32VirtualKey);
    }

    [Fact]
    public void Win32Modifiers_CombinesFlags()
    {
        // MOD_ALT(1) | MOD_CONTROL(2) | MOD_SHIFT(4) | MOD_WIN(8)
        Assert.Equal(0x3u, new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, Key.D1).Win32Modifiers);
        Assert.Equal(0xFu, new HotkeyChord(
            KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta, Key.A).Win32Modifiers);
    }

    [Fact]
    public void IsRegisterable_RequiresModifierAndMappedKey()
    {
        Assert.True(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, Key.D1).IsRegisterable);
        Assert.False(new HotkeyChord(KeyModifiers.None, Key.D1).IsRegisterable);          // no modifier
        Assert.False(new HotkeyChord(KeyModifiers.Control, Key.Space).IsRegisterable);    // unmapped key
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var chord = new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, Key.D1);
        Assert.True(HotkeyChord.TryParse(chord.Serialize(), out var parsed));
        Assert.Equal(chord.Win32VirtualKey, parsed!.Win32VirtualKey);
        Assert.Equal(chord.Win32Modifiers, parsed.Win32Modifiers);
    }

    [Fact]
    public void TryParse_Garbage_ReturnsFalse()
    {
        Assert.False(HotkeyChord.TryParse("", out _));
        Assert.False(HotkeyChord.TryParse("not a chord!!", out _));
    }

    /// <summary>The number row is printed 0-9, not D0-D9 — KeyGesture's spelling used to leak to the
    /// Hotkeys list as "Ctrl+Alt+D9".</summary>
    [Theory]
    [InlineData(Key.D9, "Ctrl+Alt+9")]
    [InlineData(Key.D0, "Ctrl+Alt+0")]
    [InlineData(Key.A, "Ctrl+Alt+A")]
    [InlineData(Key.F5, "Ctrl+Alt+F5")]
    [InlineData(Key.NumPad9, "Ctrl+Alt+Num 9")]
    public void Display_UsesTheLabelPrintedOnTheKey(Key key, string expected)
    {
        var chord = new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, key);

        Assert.Equal(expected, chord.Display);
    }

    /// <summary>Same modifier order as the capture dialog's chips, so a chord reads the same way in
    /// both places.</summary>
    [Fact]
    public void Display_OrdersModifiersCtrlAltShiftWin()
    {
        var chord = new HotkeyChord(
            KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control, Key.D1);

        Assert.Equal("Ctrl+Alt+Shift+Win+1", chord.Display);
    }

    /// <summary>Display is for people and Serialize is for the settings file; decoupling them is the
    /// whole point, so a friendlier Display must not have changed what is written to disk.</summary>
    [Fact]
    public void Serialize_IsUnaffectedByTheDisplayFormat()
    {
        var chord = new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, Key.D9);

        Assert.Equal("Ctrl+Alt+D9", chord.Serialize());
        Assert.Equal("Ctrl+Alt+9", chord.Display);

        // ...and what was already on disk still reads back.
        Assert.True(HotkeyChord.TryParse("Ctrl+Alt+D9", out var parsed));
        Assert.Equal(Key.D9, parsed!.Key);
    }
}
