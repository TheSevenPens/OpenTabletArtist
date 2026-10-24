using Avalonia.Input;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

// #1015: the Record button turns a physical key into the key name OTD's keyboard bindings accept.
public class KeyCaptureMapTests
{
    [Theory]
    [InlineData(Key.LeftCtrl, CapturedModifier.Ctrl)]
    [InlineData(Key.RightCtrl, CapturedModifier.Ctrl)]
    [InlineData(Key.LeftShift, CapturedModifier.Shift)]
    [InlineData(Key.RightShift, CapturedModifier.Shift)]
    [InlineData(Key.LeftAlt, CapturedModifier.Alt)]
    [InlineData(Key.RightAlt, CapturedModifier.Alt)]
    [InlineData(Key.A, CapturedModifier.None)]
    [InlineData(Key.Space, CapturedModifier.None)]
    public void ModifierOf_RecognisesBothSidesOfEachModifier(Key key, CapturedModifier expected)
    {
        Assert.Equal(expected, KeyCaptureMap.ModifierOf(key));
    }

    [Theory]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Z, "Z")]
    [InlineData(Key.D0, "D0")]
    [InlineData(Key.D5, "D5")]
    [InlineData(Key.NumPad3, "Keypad3")]
    [InlineData(Key.F1, "F1")]
    [InlineData(Key.F13, "F13")]
    [InlineData(Key.Space, "Space")]
    [InlineData(Key.Return, "Enter")]
    [InlineData(Key.Back, "Backspace")]
    [InlineData(Key.OemTilde, "Grave")]
    [InlineData(Key.OemQuestion, "Slash")]
    [InlineData(Key.OemOpenBrackets, "LeftBracket")]
    [InlineData(Key.PageDown, "PageDown")]
    public void TryMap_GivesOtdsName(Key key, string expected)
    {
        Assert.True(KeyCaptureMap.TryMap(key, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(Key.None)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    [InlineData(Key.LeftCtrl)]
    public void TryMap_RefusesKeysOtdCannotSend(Key key)
    {
        Assert.False(KeyCaptureMap.TryMap(key, out _));
    }

    [Fact]
    public void EveryMappedName_IsAKeyOtdAccepts()
    {
        // The point of the map: whatever it returns must be something the daemon will actually send. Checked
        // against OTD's own key list for the platform, so a typo here cannot record a binding that does nothing.
        if (!OperatingSystem.IsWindows()) return; // OTD's list is per platform; the names above are the Windows set

        var valid = AuxKeyBinding.Options.Select(o => o.Value).ToHashSet();
        var mapped = 0;
        foreach (var key in Enum.GetValues<Key>())
        {
            if (!KeyCaptureMap.TryMap(key, out var name)) continue;
            mapped++;
            Assert.True(valid.Contains(name), $"{key} maps to \"{name}\", which OTD does not accept");
        }
        Assert.True(mapped > 80, $"only {mapped} keys mapped; the map looks truncated");
    }
}
