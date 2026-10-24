using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

// Moved out of TabletDetailViewModel (#751). Nothing changed but the file it lives in: these
// are top-level types that never touched the editor's fields, so the move is mechanical and
// the editor is that much smaller to read.
//
// ButtonBinding, one of the tablet editor's row/card types.

public partial class ButtonBinding : ObservableObject
{
    private readonly Func<int, AuxBinding, Task>? _applyBinding;
    private readonly Func<AuxBinding, string, Task<AuxBinding?>>? _editBinding;
    private AuxBinding _applied;

    private readonly bool _outputIsWindowsInk;
    private readonly BindingHazard _otherHazard;

    /// <param name="outputIsWindowsInk">Whether the tablet uses Windows Ink, for the warning below.</param>
    /// <param name="otherHazard">The hazard of a binding this editor cannot model (its own store was judged by
    /// the caller, since the row only holds a label for it).</param>
    public ButtonBinding(int index, AuxBinding binding, bool isOtherBinding, string otherLabel,
        bool canEdit, Func<int, AuxBinding, Task>? applyBinding, string? label = null,
        Func<AuxBinding, string, Task<AuxBinding?>>? editBinding = null,
        bool outputIsWindowsInk = false, BindingHazard otherHazard = BindingHazard.None)
    {
        Index = index;
        _label = label;
        IsOtherBinding = isOtherBinding;
        OtherLabel = otherLabel;
        CanEdit = canEdit;
        _applyBinding = applyBinding;
        _editBinding = editBinding;
        _applied = binding;
        _outputIsWindowsInk = outputIsWindowsInk;
        _otherHazard = otherHazard;
    }

    /// <summary>A plain sentence when what this button is set to does not suit the output mode (a mouse click
    /// or scroll under Windows Ink, #1015), else null. Worked out from the saved binding, so it is there
    /// whoever set it. The binding is never changed for the user: Edit and Clear are theirs to use.</summary>
    public string? Warning => BindingSafety.Message(Hazard);
    public bool HasWarning => Warning != null;

    private BindingHazard Hazard
    {
        get
        {
            if (IsOtherBinding && !_applied.IsBound) return _otherHazard;
            if (!_outputIsWindowsInk) return BindingHazard.None;
            return _applied.Kind switch
            {
                AuxKind.Mouse when _applied.IsBound => BindingHazard.MouseClickUnderWindowsInk,
                AuxKind.Scroll when _applied.IsBound => BindingHazard.ScrollUnderWindowsInk,
                _ => BindingHazard.None,
            };
        }
    }

    private void NotifyWarningChanged()
    {
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(HasWarning));
    }

    public int Index { get; }
    private readonly string? _label;
    /// <summary>Row title. Defaults to "Button N"; wheel rows pass a custom label (the direction).</summary>
    public string Label => _label ?? $"Button {Index}";

    /// <summary>Read-only summary shown on the card: the friendly name of a binding this editor can't
    /// model, else "Ctrl + Z" / "Left click" / "Scroll up" / "Do nothing".</summary>
    public string Summary => IsOtherBinding && !_applied.IsBound ? OtherLabel : AuxKeyBinding.Describe(_applied);

    /// <summary>False disables the Edit button (read-only host, or button mapping suspended).</summary>
    public bool CanEdit { get; }

    /// <summary>True when this button already holds a binding this editor can't model (Windows Ink, an
    /// adaptive binding, or a multi-key macro) — the summary shows its friendly name until it's replaced.
    /// Settable only by <see cref="Clear"/>, which is the one thing that can remove such a binding: the
    /// modal editor cannot, because it hands back Unbound and this row already holds Unbound, so its
    /// "no change" check swallows it.</summary>
    public bool IsOtherBinding { get; private set; }
    public string OtherLabel { get; private set; }

    /// <summary>True while the physical button is held down — highlights the card live.</summary>
    [ObservableProperty] private bool _isPressed;

    /// <summary>Open the modal editor and apply the result — a binding, or <see cref="AuxBinding.Unbound"/>
    /// from Clear. Cancel (null) leaves the binding untouched. Nothing is applied until the dialog
    /// returns, so there's no inline apply-on-change to loop.</summary>
    // CanExecute, not just the guard below: the action is a named menu item now rather than a button the
    // view could disable itself, so a read-only host has to grey it out from here.
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task Edit()
    {
        if (_editBinding == null || !CanEdit) return;
        var result = await _editBinding(_applied, Label);
        if (result is not { } binding) return; // cancelled
        if (binding == _applied) return;        // no change
        _applied = binding;
        OnPropertyChanged(nameof(Summary));
        NotifyWarningChanged();
        ClearCommand.NotifyCanExecuteChanged();
        if (_applyBinding != null) await _applyBinding(Index, binding);
    }

    /// <summary>Something is mapped here, so there is something to clear. An unmodellable binding counts:
    /// it is exactly the case the modal editor cannot undo.</summary>
    private bool CanClear => CanEdit && (_applied.IsBound || IsOtherBinding);

    /// <summary>Unmap this row without opening the editor (#wheel-row-actions). Applies the same Unbound
    /// the dialog's Clear does, in one step rather than two.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task Clear()
    {
        _applied = AuxBinding.Unbound;
        IsOtherBinding = false;
        OtherLabel = "";
        OnPropertyChanged(nameof(Summary));
        NotifyWarningChanged();
        ClearCommand.NotifyCanExecuteChanged();
        if (_applyBinding != null) await _applyBinding(Index, _applied);
    }
}
