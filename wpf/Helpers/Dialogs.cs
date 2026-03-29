using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace TabletDriverUX.Helpers;

public static class Dialogs
{
    public static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task ShowMessageAsync(string title, string message, Window? parent = null)
    {
        parent ??= GetMainWindow();
        var dialog = new Window
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
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Padding = new Thickness(24, 8),
                        FontSize = 13
                    }
                }
            }
        };

        var panel = (StackPanel)dialog.Content;
        var btn = (Button)panel.Children[1];
        btn.Click += (_, _) => dialog.Close();

        if (parent != null)
            await dialog.ShowDialog(parent);
    }

    public static async Task<bool> ShowConfirmAsync(string title, string message, Window? parent = null)
    {
        parent ??= GetMainWindow();
        bool result = false;

        var dialog = new Window
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
                            new Button { Content = "No", Padding = new Thickness(24, 8), FontSize = 13 },
                            new Button { Content = "Yes", Padding = new Thickness(24, 8), FontSize = 13 }
                        }
                    }
                }
            }
        };

        var panel = (StackPanel)dialog.Content;
        var btnPanel = (StackPanel)panel.Children[1];
        var noBtn = (Button)btnPanel.Children[0];
        var yesBtn = (Button)btnPanel.Children[1];

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

        var dialog = new Window
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
                            new Button { Content = "Cancel", Padding = new Thickness(24, 8), FontSize = 13 },
                            new Button { Content = "OK", Padding = new Thickness(24, 8), FontSize = 13 }
                        }
                    }
                }
            }
        };

        var panel = (StackPanel)dialog.Content;
        var btnPanel = (StackPanel)panel.Children[2];
        var cancelBtn = (Button)btnPanel.Children[0];
        var okBtn = (Button)btnPanel.Children[1];

        cancelBtn.Click += (_, _) => dialog.Close();
        okBtn.Click += (_, _) => { result = textBox.Text; dialog.Close(); };

        if (parent != null)
            await dialog.ShowDialog(parent);

        return result;
    }
}
