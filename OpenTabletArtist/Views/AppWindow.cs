using System;
using Avalonia.Controls;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Views;

/// <summary>
/// A <see cref="Window"/> that turns off Windows' shell pen/touch feedback (the tap ripple rings, etc.)
/// on open — so every window built on it is free of that noise without repeating the call. Used for the
/// programmatically-built dialogs; XAML-defined dialogs subscribe via <c>ShellPenFeedback.DisableOnOpen</c>.
/// </summary>
public class AppWindow : Window
{
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ShellPenFeedback.DisableFor(this);
    }
}
