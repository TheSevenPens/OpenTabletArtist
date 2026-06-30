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
    public void Options_StartWithNone_AndMapDigitsToOtdNames()
    {
        Assert.Equal(AuxKeyBinding.None, AuxKeyBinding.Options[0].Value);
        Assert.Contains(AuxKeyBinding.Options, o => o.Value == "A" && o.Display == "A");
        // Digits show as "0".."9" but store as OTD's "D0".."D9".
        Assert.Contains(AuxKeyBinding.Options, o => o.Display == "0" && o.Value == "D0");
        Assert.Contains(AuxKeyBinding.Options, o => o.Display == "F5" && o.Value == "F5");
        Assert.Contains(AuxKeyBinding.Options, o => o.Value == "Space");
    }
}
