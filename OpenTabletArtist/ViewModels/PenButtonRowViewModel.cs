using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// One pen barrel button on the PEN tab, as the three choices an artist thinks in: Default action, Keyboard
/// shortcut, Disabled (#1015). Tip and eraser keep <see cref="PenSwitchRowViewModel"/>.
///
/// Built only from bindings OTD already ships. It never rewrites a binding it does not model: a button set
/// elsewhere (OTD's own UI) has none of the three ticked, a plain-language line saying what it does, and a warning
/// when that does not suit the output mode. The warning is worked out from the saved binding each time the
/// row is built, so it does not matter where the binding came from.
/// </summary>
public partial class PenButtonRowViewModel : ObservableObject
{
    private readonly int _index;
    private readonly bool _outputIsWindowsInk;
    private readonly Func<int, PluginSettingStore?, Task> _applyAsync;
    private readonly Func<AuxBinding, string, BindingEditorOptions, Task<AuxBinding?>>? _editShortcut;
    private PluginSettingStore? _store;

    public PenButtonRowViewModel(
        int index,
        PluginSettingStore? store,
        bool outputIsWindowsInk,
        bool canEdit,
        Func<int, PluginSettingStore?, Task> applyAsync,
        Func<AuxBinding, string, BindingEditorOptions, Task<AuxBinding?>>? editShortcut)
    {
        _index = index;
        _outputIsWindowsInk = outputIsWindowsInk;
        _applyAsync = applyAsync;
        _editShortcut = editShortcut;
        _canEdit = canEdit;
        SectionLabel = $"BUTTON {index}";
        Load(store);
    }

    public string SectionLabel { get; }

    /// <summary>First row in a card list — kept so the pen tab can hide a leading divider, as the other rows do.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private PenButtonAction _action;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private string? _sharedBarrelHint;

    // Whether the one-click fix applies to what is saved; CanSwitchToBarrel adds whether the host can write.
    private bool _switchPossible;
    public bool CanSwitchToBarrel => _switchPossible && CanEdit;

    // Which choice is saved. The card's "…" menu shows a tick against it; a binding set elsewhere (or one the
    // card cannot model) is none of the three, so no tick.
    public bool IsDefaultAction => Action == PenButtonAction.Default;
    public bool IsShortcut => Action == PenButtonAction.Shortcut;
    public bool IsDisabled => Action == PenButtonAction.Disabled;
    public bool IsOther => Action == PenButtonAction.Other;

    public bool HasWarning => Warning != null;
    public bool HasSharedBarrelHint => SharedBarrelHint != null;

    /// <summary>Adaptive Binding knows Buttons 1 to 3 only, so a fourth button has no Default action.</summary>
    public bool CanUseDefaultAction => PenButtonBinding.CanUseDefaultAction(_index);

    /// <summary>The menu entry that opens the shortcut editor. Under mouse output the editor also offers a
    /// mouse click, so the entry is named for both.</summary>
    public string ShortcutLabel => _outputIsWindowsInk ? "Keyboard shortcut" : "Shortcut or mouse click";

    /// <summary>One line saying what the button does now: "Barrel Switch", "Ctrl + Z", "Disabled", or, for a
    /// binding set elsewhere, "Currently: …" (no choice is ticked for it).</summary>
    [ObservableProperty] private string _stateText = "";

    /// <summary>Who decides what the Default action does, shown on hover so it costs the card no height.
    /// Only the Windows Ink Barrel Switch needs saying: your drawing software reads it through its own stylus settings.</summary>
    [ObservableProperty] private string? _stateTooltip;

    private const string DefaultActionHelp = "Interpreted natively by your drawing software, using its stylus settings.";

    partial void OnActionChanged(PenButtonAction value)
    {
        OnPropertyChanged(nameof(IsDefaultAction));
        OnPropertyChanged(nameof(IsShortcut));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsOther));
    }

    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));
    partial void OnSharedBarrelHintChanged(string? value) => OnPropertyChanged(nameof(HasSharedBarrelHint));

    partial void OnCanEditChanged(bool value)
    {
        UseDefaultActionCommand.NotifyCanExecuteChanged();
        UseShortcutCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        NotifySwitchChanged();
    }

    private void NotifySwitchChanged()
    {
        OnPropertyChanged(nameof(CanSwitchToBarrel));
        SwitchToBarrelCommand.NotifyCanExecuteChanged();
    }

    private void Load(PluginSettingStore? store)
    {
        _store = store;
        Action = PenButtonBinding.Classify(store, _index, _outputIsWindowsInk);
        Summary = PenButtonBinding.Describe(store, _index, _outputIsWindowsInk,
            otherLabel: store?.Path is { } p ? FriendlyName(p) : "Disabled");
        var hazard = BindingSafety.Assess(store, _outputIsWindowsInk);
        StateText = Action == PenButtonAction.Other ? $"Currently: {Summary}" : Summary;
        StateTooltip = Action == PenButtonAction.Default && _outputIsWindowsInk ? DefaultActionHelp : null;
        Warning = BindingSafety.Message(hazard);
        _switchPossible = hazard == BindingHazard.MouseClickUnderWindowsInk
            && BindingSafety.IsRightClick(store) && CanUseDefaultAction;
        NotifySwitchChanged();
    }

    /// <summary>The plugin's own name for a binding the card does not model, else the last part of its path.</summary>
    private static string FriendlyName(string path)
    {
        string? name = null;
        try { name = AppInfo.PluginManager?.GetFriendlyName(path); }
        catch { /* plugin manager not initialised (tests): fall back to the path */ }
        return name ?? path.Split('.').Last();
    }

    /// <summary>The user picked Default action: write the Adaptive Binding for this button.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task UseDefaultAction() => ApplyAsync(PenButtonBinding.MakeDefault(_index));

    /// <summary>The user picked Disabled: a null entry at this button's index.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task Disable() => ApplyAsync(PenButtonBinding.MakeDisabled());

    /// <summary>The one-click fix for a right click under Windows Ink: same intent, the native Barrel Switch.</summary>
    [RelayCommand(CanExecute = nameof(CanSwitchToBarrel))]
    private Task SwitchToBarrel() => ApplyAsync(PenButtonBinding.MakeDefault(_index));

    /// <summary>Open the shortcut editor (the card's "Keyboard shortcut…" entry, which also changes an existing
    /// one). Cancel leaves everything as it was; Save writes the shortcut; Clear (which the editor reports as
    /// Unbound) disables the button.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task UseShortcut()
    {
        if (_editShortcut == null) return;

        var current = PenButtonBinding.ReadShortcut(_store);
        var options = new BindingEditorOptions(AllowMouseAndScroll: !_outputIsWindowsInk);
        var result = await _editShortcut(current, $"Button {_index}", options);

        if (result is not { } binding) return; // cancelled: nothing was changed, so nothing to put back
        await ApplyAsync(AuxKeyBinding.MakeBinding(binding));
    }

    private async Task ApplyAsync(PluginSettingStore? store)
    {
        var before = _store;
        Load(store);
        try { await _applyAsync(_index, store); }
        catch { Load(before); throw; }
    }
}
