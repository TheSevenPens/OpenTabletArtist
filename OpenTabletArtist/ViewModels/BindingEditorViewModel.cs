using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the modal binding editor (<c>BindingEditorDialog</c>): one tab per binding type
/// (Keyboard / Mouse / Scroll). Edits a copy of the binding and only produces a <see cref="Result"/>
/// on Save/Clear — nothing is applied until then, which is why the old inline apply-on-change hazards
/// (transient-null re-apply loops) can't occur here.
/// </summary>
public partial class BindingEditorViewModel : ObservableObject
{
    public const int KeyboardTab = 0, MouseTab = 1, ScrollTab = 2;

    public string Title { get; }

    /// <summary>The chosen binding on Save, <see cref="AuxBinding.Unbound"/> on Clear, or null on Cancel.</summary>
    public AuxBinding? Result { get; private set; }

    public event Action? CloseRequested;

    public IReadOnlyList<KeyOption> KeyOptions => AuxKeyBinding.Options;
    public IReadOnlyList<KeyOption> MouseButtonOptions => AuxKeyBinding.MouseButtonOptions;
    public IReadOnlyList<KeyOption> ScrollOptions => AuxKeyBinding.ScrollOptions;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _alt;
    [ObservableProperty] private string? _selectedKey;
    [ObservableProperty] private string? _selectedMouseButton;
    [ObservableProperty] private string? _selectedScroll;

    public BindingEditorViewModel(AuxBinding initial, string title)
    {
        Title = title;
        switch (initial.Kind)
        {
            case AuxKind.Keyboard:
                SelectedTabIndex = KeyboardTab;
                Ctrl = initial.Combo.Ctrl;
                Shift = initial.Combo.Shift;
                Alt = initial.Combo.Alt;
                SelectedKey = initial.Combo.IsBound ? initial.Combo.Key : null;
                break;
            case AuxKind.Mouse:
                SelectedTabIndex = MouseTab;
                SelectedMouseButton = initial.MouseButton == AuxKeyBinding.None ? null : initial.MouseButton;
                break;
            case AuxKind.Scroll:
                SelectedTabIndex = ScrollTab;
                SelectedScroll = initial.Scroll == AuxKeyBinding.None ? null : initial.Scroll;
                break;
            default:
                SelectedTabIndex = KeyboardTab;   // an unbound button opens on Keyboard
                break;
        }
    }

    /// <summary>Save is enabled only when the active tab has a complete selection.</summary>
    public bool CanSave => SelectedTabIndex switch
    {
        KeyboardTab => !string.IsNullOrEmpty(SelectedKey),
        MouseTab => !string.IsNullOrEmpty(SelectedMouseButton),
        ScrollTab => !string.IsNullOrEmpty(SelectedScroll),
        _ => false,
    };

    partial void OnSelectedTabIndexChanged(int value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedKeyChanged(string? value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedMouseButtonChanged(string? value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedScrollChanged(string? value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Result = SelectedTabIndex switch
        {
            KeyboardTab => new AuxBinding(AuxKind.Keyboard, new AuxCombo(Ctrl, Shift, Alt, SelectedKey!),
                                          AuxKeyBinding.None, AuxKeyBinding.None),
            MouseTab => new AuxBinding(AuxKind.Mouse, AuxCombo.Unbound, SelectedMouseButton!, AuxKeyBinding.None),
            ScrollTab => new AuxBinding(AuxKind.Scroll, AuxCombo.Unbound, AuxKeyBinding.None, SelectedScroll!),
            _ => AuxBinding.Unbound,
        };
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        Result = AuxBinding.Unbound;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelDialog()
    {
        Result = null;
        CloseRequested?.Invoke();
    }
}
