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
}
