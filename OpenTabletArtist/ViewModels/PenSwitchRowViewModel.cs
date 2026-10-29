using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// PenSwitchRowViewModel, one of the tablet editor's row/card types.

public partial class PenSwitchRowViewModel : ObservableObject
{
    private readonly PenSwitchKind _kind;
    private readonly int _penButtonIndex;
    private readonly Func<PenSwitchKind, int, PluginSettingStore, Task> _applyAsync;
    private readonly Func<Task>? _disableAsync;

    /// <param name="disableAsync">Clears the binding, for the Disabled choice of the tip and the eraser.</param>
    public PenSwitchRowViewModel(
        PenSwitchKind kind,
        int penButtonIndex,
        PluginSettingStore? store,
        bool canEdit,
        Func<PenSwitchKind, int, PluginSettingStore, Task> applyAsync,
        Func<Task>? disableAsync = null)
    {
        _kind = kind;
        _penButtonIndex = penButtonIndex;
        _applyAsync = applyAsync;
        _disableAsync = disableAsync;
        SectionLabel = kind switch
        {
            PenSwitchKind.Tip => "PEN TIP",
            PenSwitchKind.Eraser => "ERASER",
            PenSwitchKind.PenButton => $"BUTTON {penButtonIndex}",
            _ => "SWITCH"
        };
        RefreshFromStore(store, canEdit);
    }

    public string SectionLabel { get; }

    /// <summary>First row in the merged Pen Switches card — its separating divider is hidden.</summary>
    public bool IsFirst { get; set; }

    [ObservableProperty] private string _bindingLabel = "None";
    /// <summary>Plain-language name of the current binding for the simplified card's "Currently: …" line
    /// (e.g. "Mouse button", "Windows Ink (legacy)") — no plugin jargon (#pen-inputs-simplify).</summary>
    [ObservableProperty] private string _currentPlainLabel = "None";
    [ObservableProperty] private PenSwitchBindingMode _mode;
    [ObservableProperty] private bool _canEdit;

    private bool _unbound;

    /// <summary>The tip or the eraser with nothing bound: the card's Disabled choice. The card does not call it
    /// "Changed to a custom action"; its Default action is the way back.</summary>
    public bool IsDisabled => _kind is PenSwitchKind.Tip or PenSwitchKind.Eraser && _unbound;

    public bool IsRecommended => Mode == PenSwitchBindingMode.Auto;
    public bool IsNotRecommended => Mode != PenSwitchBindingMode.Auto && !IsDisabled;

    partial void OnModeChanged(PenSwitchBindingMode value)
    {
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(IsNotRecommended));
        SetAutoCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanEditChanged(bool value)
    {
        SetAutoCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFromStore(PluginSettingStore? store, bool canEdit)
    {
        CanEdit = canEdit;
        _unbound = store?.Path == null;
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsNotRecommended));
        Mode = PenSwitchBinding.DetectMode(store, _kind, _penButtonIndex);
        BindingLabel = PenSwitchBinding.GetDisplayLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
        CurrentPlainLabel = PenSwitchBinding.GetPlainCurrentLabel(store, _kind, _penButtonIndex, GetPluginFriendlyName);
    }

    private static string? GetPluginFriendlyName(string? path) =>
        path == null ? null : AppInfo.PluginManager.GetFriendlyName(path);

    // Executable whenever the host can write, even when it is already the lit choice: a RadioButton with a
    // command that cannot run is greyed out, and a lit segment must not look disabled.
    private bool CanSetAuto => CanEdit;

    /// <summary>Default action: write the Adaptive Binding for this switch.</summary>
    [RelayCommand(CanExecute = nameof(CanSetAuto))]
    private Task SetAuto() =>
        _applyAsync(_kind, _penButtonIndex, PenSwitchBinding.MakeAdaptiveBinding(_kind, _penButtonIndex));

    private bool CanDisable => CanEdit && _disableAsync != null;

    /// <summary>Disabled: clear the binding.</summary>
    [RelayCommand(CanExecute = nameof(CanDisable))]
    private Task Disable() => _disableAsync!();
}
