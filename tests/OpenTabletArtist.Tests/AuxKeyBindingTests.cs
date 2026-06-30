using System.Linq;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AuxKeyBindingTests
{
    private static PluginSettingStore StoreAt(string path) =>
        JsonConvert.DeserializeObject<PluginSettingStore>($$"""{"Path":"{{path}}","Enable":true,"Settings":[]}""")!;

    [Fact]
    public void MakeKeyBinding_BuildsKeyBindingStore()
    {
        var store = AuxKeyBinding.MakeKeyBinding("A");

        Assert.NotNull(store);
        Assert.Equal(AuxKeyBinding.KeyBindingPath, store!.Path);
        Assert.Equal("A", store.Settings.First(s => s.Property == "Key").Value!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    public void MakeKeyBinding_UnboundForNone(string? key)
    {
        Assert.Null(AuxKeyBinding.MakeKeyBinding(key)); // null entry = unbound button
    }

    [Fact]
    public void ReadKey_RoundTripsThroughMakeKeyBinding()
    {
        Assert.Equal("F5", AuxKeyBinding.ReadKey(AuxKeyBinding.MakeKeyBinding("F5")));
    }

    [Fact]
    public void ReadKey_NoneForUnboundOrForeignBinding()
    {
        Assert.Equal(AuxKeyBinding.None, AuxKeyBinding.ReadKey(null));                      // unbound
        Assert.Equal(AuxKeyBinding.None, AuxKeyBinding.ReadKey(StoreAt(AuxKeyBinding.KeyBindingPath))); // key binding w/ no Key
        Assert.Equal(AuxKeyBinding.None,
            AuxKeyBinding.ReadKey(StoreAt("OpenTabletDriver.Desktop.Binding.MouseBinding"))); // different binding type
    }

    [Fact]
    public void Options_HaveNoNone_AndMapDigitsToOtdNames()
    {
        // Unbinding is the explicit None binding type, so the key picker carries only real keys.
        Assert.DoesNotContain(AuxKeyBinding.Options, o => o.Value == AuxKeyBinding.None);
        Assert.Contains(AuxKeyBinding.Options, o => o.Value == "A" && o.Display == "A");
        // Digits show as "0".."9" but store as OTD's "D0".."D9".
        Assert.Contains(AuxKeyBinding.Options, o => o.Display == "0" && o.Value == "D0");
        Assert.Contains(AuxKeyBinding.Options, o => o.Display == "F5" && o.Value == "F5");
        Assert.Contains(AuxKeyBinding.Options, o => o.Value == "Space");
    }

    // ── Multi-key combos ───────────────────────────────────────────

    [Fact]
    public void MakeBinding_NoModifiers_IsPlainKeyBinding()
    {
        var store = AuxKeyBinding.MakeBinding(new AuxCombo(false, false, false, "A"));
        Assert.Equal(AuxKeyBinding.KeyBindingPath, store!.Path);
        Assert.Equal("A", store.Settings.First(s => s.Property == "Key").Value!.ToString());
    }

    [Fact]
    public void MakeBinding_WithModifiers_IsMultiKeyBinding_JoinedByPlus()
    {
        var store = AuxKeyBinding.MakeBinding(new AuxCombo(Ctrl: true, Shift: true, Alt: false, "Z"));
        Assert.Equal(AuxKeyBinding.MultiKeyBindingPath, store!.Path);
        Assert.Equal("Control+Shift+Z", store.Settings.First(s => s.Property == "Keys").Value!.ToString());
    }

    [Fact]
    public void MakeBinding_Unbound_WhenNoKey_EvenWithModifiers()
    {
        Assert.Null(AuxKeyBinding.MakeBinding(new AuxCombo(true, true, true, AuxKeyBinding.None)));
    }

    [Fact]
    public void ReadCombo_RoundTripsModifiersAndKey()
    {
        var combo = new AuxCombo(Ctrl: true, Shift: false, Alt: true, "S");
        Assert.Equal(combo, AuxKeyBinding.ReadCombo(AuxKeyBinding.MakeBinding(combo)));
    }

    [Fact]
    public void ReadCombo_PlainKeyBinding_HasNoModifiers()
    {
        Assert.Equal(new AuxCombo(false, false, false, "A"),
            AuxKeyBinding.ReadCombo(AuxKeyBinding.MakeKeyBinding("A")));
    }

    [Fact]
    public void ReadCombo_RecognizesSideSpecificModifiers()
    {
        var store = StoreAt(AuxKeyBinding.MultiKeyBindingPath);
        store.Settings.Add(new PluginSetting("Keys", "LeftControl+Z"));
        Assert.Equal(new AuxCombo(true, false, false, "Z"), AuxKeyBinding.ReadCombo(store));
    }

    [Fact]
    public void ReadCombo_NullForUnmodellableBindings()
    {
        Assert.Equal(AuxCombo.Unbound, AuxKeyBinding.ReadCombo(null));

        var twoKeys = StoreAt(AuxKeyBinding.MultiKeyBindingPath);
        twoKeys.Settings.Add(new PluginSetting("Keys", "A+B")); // two non-modifier keys
        Assert.Null(AuxKeyBinding.ReadCombo(twoKeys));

        var modsOnly = StoreAt(AuxKeyBinding.MultiKeyBindingPath);
        modsOnly.Settings.Add(new PluginSetting("Keys", "Control+Shift")); // no actual key
        Assert.Null(AuxKeyBinding.ReadCombo(modsOnly));

        Assert.Null(AuxKeyBinding.ReadCombo(StoreAt(AuxKeyBinding.MouseBindingPath)));
    }

    // ── Mouse buttons + unified read/write ──────────────────────────

    [Fact]
    public void MakeMouseBinding_BuildsMouseBindingStore()
    {
        var store = AuxKeyBinding.MakeMouseBinding("Right");
        Assert.Equal(AuxKeyBinding.MouseBindingPath, store!.Path);
        Assert.Equal("Right", store.Settings.First(s => s.Property == "Button").Value!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("None")]
    public void MakeMouseBinding_UnboundForNone(string? button)
    {
        Assert.Null(AuxKeyBinding.MakeMouseBinding(button));
    }

    [Fact]
    public void ReadBinding_DispatchesByType()
    {
        Assert.Equal(AuxBinding.Unbound, AuxKeyBinding.ReadBinding(null));

        var key = AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeKeyBinding("A"));
        Assert.Equal(AuxKind.Keyboard, key!.Kind);
        Assert.Equal("A", key.Combo.Key);

        var mouse = AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeMouseBinding("Backward"));
        Assert.Equal(AuxKind.Mouse, mouse!.Kind);
        Assert.Equal("Backward", mouse.MouseButton);

        var scroll = AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeScrollBinding("Up"));
        Assert.Equal(AuxKind.Scroll, scroll!.Kind);
        Assert.Equal("Up", scroll.Scroll);

        // A binding the editor can't model → null (flagged in the UI, not misrepresented).
        Assert.Null(AuxKeyBinding.ReadBinding(StoreAt("OpenTabletDriver.Desktop.Binding.AdaptiveBinding")));
    }

    [Fact]
    public void MakeBinding_UnifiedRoundTrips()
    {
        var mouse = new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, "Middle", AuxKeyBinding.None);
        Assert.Equal(mouse, AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeBinding(mouse)));

        var combo = new AuxBinding(AuxKind.Keyboard, new AuxCombo(true, false, true, "S"),
            AuxKeyBinding.None, AuxKeyBinding.None);
        Assert.Equal(combo, AuxKeyBinding.ReadBinding(AuxKeyBinding.MakeBinding(combo)));
    }

    [Fact]
    public void MouseButtonOptions_HaveNoNone_AndMapBackToBackward()
    {
        Assert.DoesNotContain(AuxKeyBinding.MouseButtonOptions, o => o.Value == AuxKeyBinding.None);
        Assert.Contains(AuxKeyBinding.MouseButtonOptions, o => o.Display == "Back" && o.Value == "Backward");
        Assert.Contains(AuxKeyBinding.MouseButtonOptions, o => o.Value == "Left");
    }

    [Fact]
    public void ReadBinding_ValuelessStoresAreExplicitNone()
    {
        // Unbound, or a typed binding with no value, all read as the None type (Kind = None).
        Assert.Equal(AuxKind.None, AuxKeyBinding.ReadBinding(null)!.Kind);
        Assert.Equal(AuxKind.None, AuxKeyBinding.ReadBinding(StoreAt(AuxKeyBinding.KeyBindingPath))!.Kind);
        Assert.Equal(AuxKind.None, AuxKeyBinding.ReadBinding(StoreAt(AuxKeyBinding.MouseBindingPath))!.Kind);
    }

    [Fact]
    public void MakeBinding_NoneKind_IsUnbound()
    {
        Assert.Null(AuxKeyBinding.MakeBinding(AuxBinding.Unbound));
    }

    // ── Mouse scroll ────────────────────────────────────────────────

    [Theory]
    [InlineData("Up", "Vertical", -120)]
    [InlineData("Down", "Vertical", 120)]
    [InlineData("Left", "Horizontal", -120)]
    [InlineData("Right", "Horizontal", 120)]
    public void MakeScrollBinding_MapsDirectionToSignedAmount(string scroll, string direction, int amount)
    {
        var store = AuxKeyBinding.MakeScrollBinding(scroll);

        Assert.Equal(AuxKeyBinding.MouseScrollBindingPath, store!.Path);
        Assert.Equal(direction, store.Settings.First(s => s.Property == "Direction").Value!.ToString());
        Assert.Equal(amount, store.Settings.First(s => s.Property == "Amount").GetValue<int>());
    }

    [Theory]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("Left")]
    [InlineData("Right")]
    public void ReadScroll_RoundTrips(string scroll)
    {
        Assert.Equal(scroll, AuxKeyBinding.ReadScroll(AuxKeyBinding.MakeScrollBinding(scroll)));
    }

    [Fact]
    public void MakeScrollBinding_UnboundForNone()
    {
        Assert.Null(AuxKeyBinding.MakeScrollBinding("None"));
        Assert.Null(AuxKeyBinding.MakeScrollBinding(null));
    }
}
