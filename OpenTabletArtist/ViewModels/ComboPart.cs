namespace OpenTabletArtist.ViewModels;

/// <summary>One element of the footer combo preview — a key/modifier chip, or a "+" separator between
/// them. <see cref="IsPlaceholder"/> marks the "no key yet" chip when only modifiers are set. Shared by the
/// hotkey-capture and binding-editor dialogs, which both build a combo preview from these (#617).</summary>
public sealed class ComboPart
{
    public string Text { get; }
    public bool IsSeparator { get; }
    public bool IsPlaceholder { get; }
    public bool IsChip => !IsSeparator;

    private ComboPart(string text, bool separator, bool placeholder)
    {
        Text = text;
        IsSeparator = separator;
        IsPlaceholder = placeholder;
    }

    public static ComboPart Chip(string text, bool placeholder = false) => new(text, false, placeholder);
    public static ComboPart Sep() => new("+", true, false);
}
