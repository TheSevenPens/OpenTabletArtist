using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// View model for the on-screen hotkey picker (<c>HotkeyCaptureDialog</c>), styled after the express-key
/// binding keyboard (#320). Only the keys a global hotkey can actually use are shown — letters, digits,
/// F-keys, and the numpad — so nothing on the board is a dead end. Ctrl / Alt / Shift / Win are held
/// toggles; a chord needs at least one plus a main key (see <see cref="HotkeyChord.IsRegisterable"/>).
/// The physical keyboard also works: pressing the real shortcut fills the board in.
/// </summary>
public partial class HotkeyCaptureViewModel : ObservableObject
{
    /// <summary>The chosen chord on Save, or null on Cancel / Clear.</summary>
    public HotkeyChord? Result { get; private set; }

    public event Action? CloseRequested;

    [ObservableProperty] private bool _ctrl;
    [ObservableProperty] private bool _alt;
    [ObservableProperty] private bool _shift;
    [ObservableProperty] private bool _win;
    [ObservableProperty] private HotkeyKeyCap? _selectedKey;

    public IReadOnlyList<IReadOnlyList<HotkeyKeyCap>> MainRows { get; }
    public IReadOnlyList<IReadOnlyList<HotkeyKeyCap>> NumpadRows { get; }

    private readonly List<HotkeyKeyCap> _allCaps = new();

    public HotkeyCaptureViewModel(HotkeyChord? initial = null)
    {
        HotkeyKeyCap Cap(string display, Key key, double units = 1)
        {
            var cap = new HotkeyKeyCap(display, key, units, PickKeyCommand);
            _allCaps.Add(cap);
            return cap;
        }

        // Function keys, number row, and a QWERTY block — the everyday shortcut keys.
        MainRows = new List<IReadOnlyList<HotkeyKeyCap>>
        {
            Row("F1",Key.F1,"F2",Key.F2,"F3",Key.F3,"F4",Key.F4,"F5",Key.F5,"F6",Key.F6,
                "F7",Key.F7,"F8",Key.F8,"F9",Key.F9,"F10",Key.F10,"F11",Key.F11,"F12",Key.F12),
            Row("1",Key.D1,"2",Key.D2,"3",Key.D3,"4",Key.D4,"5",Key.D5,"6",Key.D6,
                "7",Key.D7,"8",Key.D8,"9",Key.D9,"0",Key.D0),
            Row("Q",Key.Q,"W",Key.W,"E",Key.E,"R",Key.R,"T",Key.T,"Y",Key.Y,
                "U",Key.U,"I",Key.I,"O",Key.O,"P",Key.P),
            Row("A",Key.A,"S",Key.S,"D",Key.D,"F",Key.F,"G",Key.G,"H",Key.H,
                "J",Key.J,"K",Key.K,"L",Key.L),
            Row("Z",Key.Z,"X",Key.X,"C",Key.C,"V",Key.V,"B",Key.B,"N",Key.N,"M",Key.M),
        };

        NumpadRows = new List<IReadOnlyList<HotkeyKeyCap>>
        {
            Row("7",Key.NumPad7,"8",Key.NumPad8,"9",Key.NumPad9),
            Row("4",Key.NumPad4,"5",Key.NumPad5,"6",Key.NumPad6),
            Row("1",Key.NumPad1,"2",Key.NumPad2,"3",Key.NumPad3),
            Row("0",Key.NumPad0),
        };

        if (initial != null)
        {
            Ctrl = initial.Modifiers.HasFlag(KeyModifiers.Control);
            Alt = initial.Modifiers.HasFlag(KeyModifiers.Alt);
            Shift = initial.Modifiers.HasFlag(KeyModifiers.Shift);
            Win = initial.Modifiers.HasFlag(KeyModifiers.Meta);
            SelectKey(_allCaps.FirstOrDefault(c => c.Key == initial.Key));
        }
        UpdateCombo();

        // Local helper: build one keyboard row from (display, key) pairs.
        IReadOnlyList<HotkeyKeyCap> Row(params object[] pairs)
        {
            var row = new List<HotkeyKeyCap>();
            for (int i = 0; i < pairs.Length; i += 2)
                row.Add(Cap((string)pairs[i], (Key)pairs[i + 1]));
            return row;
        }
    }

    private KeyModifiers CurrentModifiers =>
        (Ctrl ? KeyModifiers.Control : 0) | (Alt ? KeyModifiers.Alt : 0) |
        (Shift ? KeyModifiers.Shift : 0) | (Win ? KeyModifiers.Meta : 0);

    /// <summary>Save is enabled only for a registerable chord (a main key plus a modifier).</summary>
    public bool CanSave => SelectedKey != null && CurrentModifiers != KeyModifiers.None;

    partial void OnCtrlChanged(bool value) => Refresh();
    partial void OnAltChanged(bool value) => Refresh();
    partial void OnShiftChanged(bool value) => Refresh();
    partial void OnWinChanged(bool value) => Refresh();

    private void Refresh()
    {
        SaveCommand.NotifyCanExecuteChanged();
        UpdateCombo();
    }

    [RelayCommand]
    private void PickKey(HotkeyKeyCap? cap)
    {
        if (cap == null) return;
        // Clicking the selected key clears it (toggle).
        SelectKey(cap == SelectedKey ? null : cap);
    }

    private void SelectKey(HotkeyKeyCap? cap)
    {
        SelectedKey = cap;
        foreach (var c in _allCaps) c.IsSelected = c == cap;
        Refresh();
    }

    /// <summary>Fill the board from a real key press (physical keyboard). Ignores bare modifier presses.</summary>
    public void CapturePhysical(KeyModifiers modifiers, Key key)
    {
        if (IsModifierKey(key)) return;
        Ctrl = modifiers.HasFlag(KeyModifiers.Control);
        Alt = modifiers.HasFlag(KeyModifiers.Alt);
        Shift = modifiers.HasFlag(KeyModifiers.Shift);
        Win = modifiers.HasFlag(KeyModifiers.Meta);
        SelectKey(_allCaps.FirstOrDefault(c => c.Key == key));
    }

    private static bool IsModifierKey(Key k) => k
        is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedKey != null)
            Result = new HotkeyChord(CurrentModifiers, SelectedKey.Key);
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelDialog()
    {
        Result = null;
        CloseRequested?.Invoke();
    }

    // ── Combo preview (the "CTRL + ALT + E" strip in the footer) ────────────────
    [ObservableProperty] private IReadOnlyList<ComboPart> _comboParts = Array.Empty<ComboPart>();
    [ObservableProperty] private bool _comboEmpty = true;

    private void UpdateCombo()
    {
        var parts = new List<ComboPart>();
        void Add(ComboPart p) { if (parts.Count > 0) parts.Add(ComboPart.Sep()); parts.Add(p); }

        if (Ctrl) Add(ComboPart.Chip("CTRL"));
        if (Alt) Add(ComboPart.Chip("ALT"));
        if (Shift) Add(ComboPart.Chip("SHIFT"));
        if (Win) Add(ComboPart.Chip("WIN"));
        if (SelectedKey is { } k) Add(ComboPart.Chip(k.Display));
        else if (parts.Count > 0) Add(ComboPart.Chip("?", placeholder: true)); // modifiers set, no key yet

        ComboParts = parts;
        ComboEmpty = parts.Count == 0;
    }
}

/// <summary>One cap on the hotkey picker's on-screen keyboard: a display label bound to an Avalonia
/// <see cref="Avalonia.Input.Key"/>. All caps are the same footprint (hotkeys never need a wide key).</summary>
public partial class HotkeyKeyCap : ObservableObject
{
    public string Display { get; }
    public Key Key { get; }
    public double Width { get; }
    public IRelayCommand<HotkeyKeyCap?> PickCommand { get; }

    [ObservableProperty] private bool _isSelected;

    public HotkeyKeyCap(string display, Key key, double widthUnits, IRelayCommand<HotkeyKeyCap?> pickCommand)
    {
        Display = display;
        Key = key;
        Width = widthUnits * 40;
        PickCommand = pickCommand;
    }
}
