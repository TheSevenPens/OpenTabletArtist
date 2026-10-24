using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;
using OpenTabletArtist.ViewModels;
using Xunit;
using OtdInterop;

namespace OpenTabletArtist.Tests;

// #1015 through the real TabletDetailViewModel: pen buttons, ExpressKeys and wheel rows are built from the
// profile on every refresh, so the warning follows what is SAVED, whoever saved it, and nothing is rewritten.
// Constructs TabletDetailViewModel (see TabletDetailViewModelTests): shares the display-enumerator seam.
[Collection(DisplayEnumeratorCollection.Name)]
public class PenButtonRowsIntegrationTests
{
    private const string WinInkAbsolute = "VoiDPlugins.OutputMode.WinInkAbsoluteMode";
    private const string NativeAbsolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";

    private static Settings SettingsFor(string outputMode, params PluginSettingStore?[] penButtons)
    {
        var profile = new Profile { Tablet = "T" };
        profile.OutputMode = new PluginSettingStore(outputMode) { Path = outputMode };
        profile.BindingSettings.PenButtons = new PluginSettingStoreCollection();
        foreach (var b in penButtons) profile.BindingSettings.PenButtons.Add(b!);
        return new Settings { Profiles = new ProfileCollection { profile } };
    }

    private static TabletDetailViewModel Open(Settings settings,
        Func<Settings, Task<SettingsApplyOutcome>>? apply = null,
        Func<AuxBinding, string, BindingEditorOptions, Task<AuxBinding?>>? edit = null) =>
        new(settings.Profiles.First(), settings,
            applyAction: apply ?? (_ => Task.FromResult(SettingsApplyOutcome.Live)),
            editBinding: edit);

    // ── Pen buttons ────────────────────────────────────────────────

    [Fact]
    public void PenButtons_UnderWindowsInk_ShowTheThreeChoices_AndWarnOnAMouseClickSetElsewhere()
    {
        // As if OTD's own UI had set button 1 to a right click.
        var settings = SettingsFor(WinInkAbsolute,
            AuxKeyBinding.MakeMouseBinding("Right"), PenButtonBinding.MakeDefault(2), null);

        var vm = Open(settings);

        Assert.True(vm.PenButton1Row!.IsOther);
        Assert.True(vm.PenButton1Row.HasWarning);
        Assert.True(vm.PenButton1Row.CanSwitchToBarrel);
        Assert.True(vm.PenButton2Row!.IsDefaultAction);
        Assert.True(vm.PenButton3Row!.IsDisabled);
        Assert.True(vm.HasPenButtons);
    }

    [Fact]
    public void BuildingTheRows_NeverChangesTheSavedBindings()
    {
        var click = AuxKeyBinding.MakeMouseBinding("Right");
        var scroll = AuxKeyBinding.MakeScrollBinding("Up");
        var settings = SettingsFor(WinInkAbsolute, click, scroll, null);
        var applied = 0;

        _ = Open(settings, apply: _ => { applied++; return Task.FromResult(SettingsApplyOutcome.Live); });

        var saved = settings.Profiles.First().BindingSettings.PenButtons;
        Assert.Same(click, saved[0]);
        Assert.Same(scroll, saved[1]);
        Assert.Null(saved[2]);
        Assert.Equal(0, applied);               // preserve and warn: nothing was submitted either
    }

    [Fact]
    public void SameBindingsUnderMouseOutput_AreFine()
    {
        var settings = SettingsFor(NativeAbsolute, AuxKeyBinding.MakeMouseBinding("Right"));

        var vm = Open(settings);

        Assert.False(vm.PenButton1Row!.HasWarning);
        Assert.True(vm.PenButton1Row.IsShortcut);   // the "shortcut or mouse click" choice
    }

    [Fact]
    public async Task SwitchToBarrel_ReplacesOnlyThatButton_AndSubmitsOnce()
    {
        var keep = AuxKeyBinding.MakeKeyBinding("B");
        var settings = SettingsFor(WinInkAbsolute, AuxKeyBinding.MakeMouseBinding("Right"), keep);
        var submitted = new List<Settings>();
        var vm = Open(settings, apply: s => { submitted.Add(s); return Task.FromResult(SettingsApplyOutcome.Live); });

        await vm.PenButton1Row!.SwitchToBarrelCommand.ExecuteAsync(null);

        var saved = settings.Profiles.First().BindingSettings.PenButtons;
        Assert.Equal(PenButtonAction.Default, PenButtonBinding.Classify(saved[0], 1, true));
        Assert.Same(keep, saved[1]);
        Assert.Single(submitted);
    }

    [Fact]
    public async Task Disable_WritesANullEntry_KeepingTheIndexOfTheOthers()
    {
        var keep = PenButtonBinding.MakeDefault(1);
        var settings = SettingsFor(WinInkAbsolute, keep, PenButtonBinding.MakeDefault(2));
        var vm = Open(settings);

        await vm.PenButton2Row!.DisableCommand.ExecuteAsync(null);

        var saved = settings.Profiles.First().BindingSettings.PenButtons;
        Assert.Equal(2, saved.Count);           // the slot is still there, so button 3's index does not shift
        Assert.Same(keep, saved[0]);
        Assert.Null(saved[1]);
    }

    [Fact]
    public void TwoDefaultActionsUnderWindowsInk_GetTheSharedBarrelHint()
    {
        var settings = SettingsFor(WinInkAbsolute, PenButtonBinding.MakeDefault(1), PenButtonBinding.MakeDefault(2));

        var vm = Open(settings);

        Assert.NotNull(vm.PenButton1Row!.SharedBarrelHint);
        Assert.NotNull(vm.PenButton2Row!.SharedBarrelHint);
    }

    [Fact]
    public async Task ChangingOneOfThem_ClearsTheHintAfterTheRefresh()
    {
        var settings = SettingsFor(WinInkAbsolute, PenButtonBinding.MakeDefault(1), PenButtonBinding.MakeDefault(2));
        var vm = Open(settings, edit: (_, _, _) => Task.FromResult<AuxBinding?>(
            new AuxBinding(AuxKind.Keyboard, new AuxCombo(false, false, true, AuxKeyBinding.None),
                AuxKeyBinding.None, AuxKeyBinding.None)));

        await vm.PenButton2Row!.UseShortcutCommand.ExecuteAsync(null);

        Assert.Null(vm.PenButton1Row!.SharedBarrelHint);
        Assert.True(vm.PenButton2Row!.IsShortcut);
    }

    [Fact]
    public void ReadOnlyHost_ShowsTheWarning_ButOffersNoChanges()
    {
        var settings = SettingsFor(WinInkAbsolute, AuxKeyBinding.MakeMouseBinding("Right"));
        var vm = new TabletDetailViewModel(settings.Profiles.First(), settings); // no apply action

        Assert.True(vm.PenButton1Row!.HasWarning);
        Assert.False(vm.PenButton1Row.SwitchToBarrelCommand.CanExecute(null));
        Assert.False(vm.PenButton1Row.DisableCommand.CanExecute(null));
    }

    // ── ExpressKeys and wheel ──────────────────────────────────────

    private static Settings WithExpressKeys(string outputMode, params PluginSettingStore?[] keys)
    {
        var settings = SettingsFor(outputMode);
        var aux = settings.Profiles.First().BindingSettings.AuxButtons = new PluginSettingStoreCollection();
        foreach (var k in keys) aux.Add(k!);
        return settings;
    }

    [Fact]
    public void ExpressKeys_UnderWindowsInk_WarnOnMouseAndScroll_NotOnAShortcut()
    {
        var click = AuxKeyBinding.MakeMouseBinding("Left");
        var settings = WithExpressKeys(WinInkAbsolute, click, AuxKeyBinding.MakeScrollBinding("Down"),
            AuxKeyBinding.MakeKeyBinding("B"), null);

        var vm = Open(settings);

        Assert.True(vm.AuxButtons[0].HasWarning);
        Assert.True(vm.AuxButtons[1].HasWarning);
        Assert.False(vm.AuxButtons[2].HasWarning);
        Assert.False(vm.AuxButtons[3].HasWarning);
        Assert.Same(click, settings.Profiles.First().BindingSettings.AuxButtons[0]);   // not rewritten
    }

    [Fact]
    public void ExpressKeys_UnderMouseOutput_HaveNoWarning()
    {
        var settings = WithExpressKeys(NativeAbsolute,
            AuxKeyBinding.MakeMouseBinding("Left"), AuxKeyBinding.MakeScrollBinding("Down"));

        var vm = Open(settings);

        Assert.All(vm.AuxButtons, b => Assert.False(b.HasWarning));
    }

    [Fact]
    public void ExpressKey_WithAWindowsInkBinding_OnAPlainTablet_IsWarned()
    {
        var settings = WithExpressKeys(NativeAbsolute, PenSwitchBinding.MakeLegacyBinding(PenSwitchKind.PenButton));

        var vm = Open(settings);

        Assert.True(vm.AuxButtons[0].IsOtherBinding);
        Assert.Equal(BindingSafety.Message(BindingHazard.WindowsInkBindingWithoutWindowsInk), vm.AuxButtons[0].Warning);
    }

    [Theory]
    [InlineData(WinInkAbsolute, false)]
    [InlineData(NativeAbsolute, true)]
    public async Task ExpressKeyEditor_IsToldWhetherMouseAndScrollAreAllowed(string outputMode, bool allowed)
    {
        var settings = WithExpressKeys(outputMode, AuxKeyBinding.MakeKeyBinding("B"));
        BindingEditorOptions? seen = null;
        var vm = Open(settings, edit: (_, _, options) => { seen = options; return Task.FromResult<AuxBinding?>(null); });

        await vm.AuxButtons[0].EditCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.Equal(allowed, seen!.AllowMouseAndScroll);
    }
}
