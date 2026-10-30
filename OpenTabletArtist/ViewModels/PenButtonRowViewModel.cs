using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// One pen barrel button on the PEN tab's buttons tab, as the three choices an artist thinks in: Default action, Keyboard
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
    private readonly int _buttonCount;
    private readonly bool _outputIsWindowsInk;
    private readonly Func<int, PluginSettingStore?, Task> _applyAsync;
    private readonly Func<AuxBinding, string, BindingEditorOptions, Task<AuxBinding?>>? _editShortcut;
    private PluginSettingStore? _store;

    /// <param name="buttonCount">How many barrel buttons the pen has, so a card can say where its button is
    /// (closest to the tip, middle, farthest). Defaults to three, the most any pen has.</param>
    public PenButtonRowViewModel(
        int index,
        PluginSettingStore? store,
        bool outputIsWindowsInk,
        bool canEdit,
        Func<int, PluginSettingStore?, Task> applyAsync,
        Func<AuxBinding, string, BindingEditorOptions, Task<AuxBinding?>>? editShortcut,
        int buttonCount = 3)
    {
        _index = index;
        _buttonCount = Math.Max(buttonCount, index);
        _outputIsWindowsInk = outputIsWindowsInk;
        _applyAsync = applyAsync;
        _editShortcut = editShortcut;
        _canEdit = canEdit;
        SectionLabel = $"BUTTON {index}";
        Load(store);
    }

    public string SectionLabel { get; }

    /// <summary>The button's number, as drawn on the pen diagram and on its card.</summary>
    public int Number => _index;

    /// <summary>Where the button sits on the pen, in words: buttons are numbered from the tip. Null for a pen
    /// with a single button, where there is nothing to tell apart.</summary>
    public string? PositionHint => _buttonCount <= 1 ? null
        : _index == 1 ? "closest to the tip"
        : _index == _buttonCount ? "farthest from the tip"
        : "middle";

    /// <summary>First row in a card list — kept so the pen tab can hide a leading divider, as the other rows do.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private PenButtonAction _action;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private string? _sharedBarrelHint;

    /// <summary>Which other buttons do the same thing, in two or three words: "same as 2", "same as 1 and 3".
    /// Null when this button shares its action with none (#1045). The card shows it as a tag, and the number on
    /// the card and on the pen takes one colour for every button in the group.</summary>
    [ObservableProperty] private string? _sameAsLabel;

    public bool IsSharedBarrel => SameAsLabel != null;

    // Whether the one-click fix applies to what is saved; CanSwitchToBarrel adds whether the host can write.
    private bool _switchPossible;
    public bool CanSwitchToBarrel => _switchPossible && CanEdit;

    // Which choice is saved. The card's three segments show it; a binding set elsewhere (or one the card
    // cannot model) is none of the three, so none is lit.
    public bool IsDefaultAction => Action == PenButtonAction.Default;
    public bool IsShortcut => Action == PenButtonAction.Shortcut;
    public bool IsDisabled => Action == PenButtonAction.Disabled;
    public bool IsOther => Action == PenButtonAction.Other;

    /// <summary>Whether the card leads with the action's name in large type (#1046). The segment that is lit
    /// already says "Default action" or "Disabled", and under Windows Ink the Default action is always the
    /// Barrel Switch, so a title there only repeats it. It stays where it says something the segments do not:
    /// a binding set elsewhere ("Currently: ..."), and the Default action outside Windows Ink, where it is a
    /// particular mouse click.</summary>
    public bool ShowTitle => Action == PenButtonAction.Other || (Action == PenButtonAction.Default && !_outputIsWindowsInk);

    public bool HasWarning => Warning != null;
    public bool HasSharedBarrelHint => SharedBarrelHint != null;

    /// <summary>Adaptive Binding knows Buttons 1 to 3 only, so a fourth button has no Default action.</summary>
    public bool CanUseDefaultAction => PenButtonBinding.CanUseDefaultAction(_index);

    /// <summary>The segment that opens the shortcut editor. Under mouse output the editor also offers a mouse
    /// click, so the segment is named for both.</summary>
    public string ShortcutLabel => _outputIsWindowsInk ? "Keyboard shortcut" : "Shortcut or mouse click";

    /// <summary>What the button does now: "Barrel Switch", "Ctrl + Z", "Disabled", or, for a binding set
    /// elsewhere, "Currently: …" (none of the segments is lit for it).</summary>
    [ObservableProperty] private string _stateText = "";

    /// <summary>Who decides what the Default action does, under Windows Ink: your drawing software reads the
    /// Barrel Switch through its own stylus settings. Shown on the card under the Default action; null where
    /// there is nothing to add (mouse output, where the action is simply a mouse click).</summary>
    [ObservableProperty] private string? _defaultDetail;

    /// <summary>The saved shortcut as separate pieces, each drawn as a keycap: <c>Ctrl</c>, <c>Z</c>.</summary>
    [ObservableProperty] private IReadOnlyList<string> _shortcutParts = [];

    /// <summary>The same pieces with a "+" between them, as the card draws them: keycap, plus, keycap.</summary>
    public IReadOnlyList<KeyPart> ShortcutKeys => ShortcutParts
        .SelectMany((part, i) => i == 0
            ? new[] { new KeyPart(part, false) }
            : new[] { new KeyPart("+", true), new KeyPart(part, false) })
        .ToList();

    partial void OnShortcutPartsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(ShortcutKeys));

    /// <summary>The note under a recorded shortcut: how long its keys stay down.</summary>
    public string ShortcutNote => "Held while you hold the button.";

    private const string DefaultActionHelp = "Right-click or context menu. Your drawing software decides what it does.";

    partial void OnActionChanged(PenButtonAction value)
    {
        OnPropertyChanged(nameof(IsDefaultAction));
        OnPropertyChanged(nameof(IsShortcut));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsOther));
        OnPropertyChanged(nameof(ShowTitle));
    }

    partial void OnWarningChanged(string? value) => OnPropertyChanged(nameof(HasWarning));
    partial void OnSharedBarrelHintChanged(string? value) => OnPropertyChanged(nameof(HasSharedBarrelHint));
    partial void OnSameAsLabelChanged(string? value) => OnPropertyChanged(nameof(IsSharedBarrel));

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
        DefaultDetail = Action == PenButtonAction.Default && _outputIsWindowsInk ? DefaultActionHelp : null;
        ShortcutParts = Action == PenButtonAction.Shortcut ? AuxKeyBinding.DescribeParts(PenButtonBinding.ReadShortcut(store)) : [];
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

    /// <summary>Open the shortcut editor (the Keyboard shortcut segment, or Change beside a recorded shortcut).
    /// Cancel leaves everything as it was; Save writes the shortcut; Clear (which the editor reports as
    /// Unbound) disables the button.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task UseShortcut()
    {
        if (_editShortcut == null) { RevertSelection(); return; }

        var current = PenButtonBinding.ReadShortcut(_store);
        var options = new BindingEditorOptions(AllowMouseAndScroll: !_outputIsWindowsInk);
        var result = await _editShortcut(current, $"Button {_index}", options);

        if (result is not { } binding) { RevertSelection(); return; } // cancelled
        await ApplyAsync(AuxKeyBinding.MakeBinding(binding));
    }

    /// <summary>A segment the user clicked has already drawn itself lit; put the card back to what is saved so
    /// a cancelled choice does not look like a change. (The segments are one-way: the saved binding decides
    /// which is lit, and a click is only a command.)</summary>
    private void RevertSelection()
    {
        OnPropertyChanged(nameof(IsDefaultAction));
        OnPropertyChanged(nameof(IsShortcut));
        OnPropertyChanged(nameof(IsDisabled));
    }

    private async Task ApplyAsync(PluginSettingStore? store)
    {
        var before = _store;
        Load(store);
        try { await _applyAsync(_index, store); }
        catch { Load(before); throw; }
    }
}

/// <summary>One piece of a recorded shortcut as the card draws it: a keycap, or the "+" between two of them.</summary>
public sealed record KeyPart(string Text, bool IsSeparator)
{
    public bool IsKey => !IsSeparator;
}
