using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using OpenTabletArtist.ViewModels;

namespace OpenTabletArtist.Views;

public partial class AboutView : UserControl
{
    // "Drawing Tablet Discord" is a plain (accent + underline) Run so it aligns exactly to the text
    // baseline. Avalonia has no inline hyperlink, and an embedded InlineUIContainer button won't
    // baseline-align in flowing text — so we make just that run clickable by hit-testing the pointer
    // against its character range in the paragraph.
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private (int start, int length)? _linkRange;

    public AboutView()
    {
        InitializeComponent();
    }

    // Character range of the DiscordLink run within the paragraph. TextLayout indexes the same inline
    // texts we sum here, so the offsets line up. Cached after the first lookup (the inlines are static).
    private (int start, int length) LinkRange()
    {
        if (_linkRange is { } cached) return cached;
        int start = 0;
        if (HelpText.Inlines is { } inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline == DiscordLink)
                    return (_linkRange = (start, (DiscordLink.Text ?? string.Empty).Length)).Value;
                if (inline is Run run) start += run.Text?.Length ?? 0;
            }
        }
        return (_linkRange = (0, 0)).Value;
    }

    private bool IsOverLink(Point p)
    {
        var (start, length) = LinkRange();
        if (length == 0) return false;
        var hit = HelpText.TextLayout.HitTestPoint(p);
        if (!hit.IsInside) return false;
        int pos = hit.CharacterHit.FirstCharacterIndex;
        return pos >= start && pos < start + length;
    }

    private void OnHelpPointerMoved(object? sender, PointerEventArgs e)
        => HelpText.Cursor = IsOverLink(e.GetPosition(HelpText)) ? HandCursor : Cursor.Default;

    private void OnHelpPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(HelpText).Properties.IsLeftButtonPressed) return;
        if (IsOverLink(e.GetPosition(HelpText)) && DataContext is AboutViewModel vm)
            vm.OpenUrlCommand.Execute(vm.HelpDiscordUrl);
    }
}
