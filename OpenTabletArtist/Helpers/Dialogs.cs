using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Domain;
using OpenTabletArtist.Views;

namespace OpenTabletArtist.Helpers;

public enum UnsavedSettingsChoice { Cancel, Save, Continue }

public static class Dialogs
{
    public static async Task<UnsavedSettingsChoice> ShowUnsavedSettingsAsync()
    {
        var parent = GetMainWindow();
        if (parent is null) return UnsavedSettingsChoice.Cancel;
        if (parent is MainWindow main) main.BringToFront();
        var choice = UnsavedSettingsChoice.Cancel;
        // Escape is Cancel, the choice that changes nothing. Enter is deliberately nothing: both other
        // choices act, and neither should happen on a stray keypress.
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var discard = new Button { Content = "Continue without saving" };
        var save = new Button { Content = "Save" };
        var dialog = new AppWindow
        {
            Title = "Unsaved OTD daemon settings",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Save your current settings before continuing? Unsaved changes may be lost when the OTD daemon restarts or another preset is loaded. Continuing does not undo changes already running on the OTD daemon.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8, Children = { cancel, discard, save },
                    },
                },
            },
        };
        cancel.Click += (_, _) => dialog.Close();
        discard.Click += (_, _) => { choice = UnsavedSettingsChoice.Continue; dialog.Close(); };
        save.Click += (_, _) => { choice = UnsavedSettingsChoice.Save; dialog.Close(); };
        await dialog.ShowDialog(parent);
        return choice;
    }

    public static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task ShowMessageAsync(string title, string message, Window? parent = null)
    {
        parent ??= GetMainWindow();

        var copyBtn = new Button { Content = "Copy", Padding = new Thickness(18, 8), FontSize = 13 };
        // Enter and Escape both close it: there is nothing to decide, only to acknowledge.
        var okBtn = new Button { Content = "OK", Padding = new Thickness(24, 8), FontSize = 13, IsDefault = true, IsCancel = true };

        var dialog = new AppWindow
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    // Selectable so the text can also be picked out by hand; the Copy button grabs it all.
                    new SelectableTextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 20),
                        FontSize = 13
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { copyBtn, okBtn }
                    }
                }
            }
        };

        okBtn.Click += (_, _) => dialog.Close();
        // Copy the title + message so it's easy to paste into a bug report / chat. Uses Avalonia's
        // cross-platform clipboard (works on macOS, unlike the Win32-only ClipboardText service).
        copyBtn.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(copyBtn)?.Clipboard is { } clipboard)
            {
                try
                {
                    await clipboard.SetTextAsync($"{title}\n\n{message}");
                    // Icon + label confirmation instead of a font checkmark (#551).
                    var check = Application.Current?.TryFindResource("IconCheckCircle", out var g) == true
                        ? g as Geometry : null;
                    // #593: the check-circle icon conveys success, not a green color.
                    var ok = Application.Current?.TryFindResource("TextPrimaryBrush", out var b) == true
                        && b is IBrush ib ? ib : Brushes.Gray;
                    copyBtn.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        Children =
                        {
                            new PathIcon { Data = check, Width = 13, Height = 13, Foreground = ok,
                                           VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = "Copied", VerticalAlignment = VerticalAlignment.Center },
                        },
                    };
                }
                catch { /* best-effort */ }
            }
        };

        if (parent != null)
            await dialog.ShowDialog(parent);
    }

    public static async Task<bool> ShowConfirmAsync(string title, string message, Window? parent = null)
    {
        parent ??= GetMainWindow();
        bool result = false;

        var dialog = new AppWindow
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 20),
                        FontSize = 13
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            // Windows convention: the affirmative action goes first/left (#502).
                            // Escape answers No. Enter is not Yes: these ask before deleting things.
                            new Button { Content = "Yes", Padding = new Thickness(24, 8), FontSize = 13 },
                            new Button { Content = "No", Padding = new Thickness(24, 8), FontSize = 13, IsCancel = true }
                        }
                    }
                }
            }
        };

        var panel = (StackPanel)dialog.Content;
        var btnPanel = (StackPanel)panel.Children[1];
        var yesBtn = (Button)btnPanel.Children[0];
        var noBtn = (Button)btnPanel.Children[1];

        noBtn.Click += (_, _) => { result = false; dialog.Close(); };
        yesBtn.Click += (_, _) => { result = true; dialog.Close(); };

        if (parent != null)
            await dialog.ShowDialog(parent);

        return result;
    }

    public static async Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "", Window? parent = null)
    {
        parent ??= GetMainWindow();
        string? result = null;

        var textBox = new TextBox
        {
            Text = defaultValue,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 20)
        };

        var dialog = new AppWindow
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = prompt,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13
                    },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            // Windows convention: the affirmative action goes first/left (#502).
                            // Enter is OK and Escape is Cancel, so a name can be typed and confirmed without the mouse.
                            new Button { Content = "OK", Padding = new Thickness(24, 8), FontSize = 13, IsDefault = true },
                            new Button { Content = "Cancel", Padding = new Thickness(24, 8), FontSize = 13, IsCancel = true }
                        }
                    }
                }
            }
        };

        var panel = (StackPanel)dialog.Content;
        var btnPanel = (StackPanel)panel.Children[2];
        var okBtn = (Button)btnPanel.Children[0];
        var cancelBtn = (Button)btnPanel.Children[1];

        cancelBtn.Click += (_, _) => dialog.Close();
        okBtn.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        // Open ready to type over: the box has focus and the current text is selected, as in a file rename.
        dialog.Opened += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        if (parent != null)
            await dialog.ShowDialog(parent);

        return result;
    }

}
