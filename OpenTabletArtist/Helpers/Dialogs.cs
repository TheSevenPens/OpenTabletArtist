using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Helpers;

public static class Dialogs
{
    public static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>A pickable running application: one entry per windowed process (#167).</summary>
    private sealed record ProcessChoice(string Display, AppIdentity Identity);

    /// <summary>Lists running windowed processes and returns the picked one's identity, or null. (#167)</summary>
    public static async Task<AppIdentity?> ShowProcessPickerAsync(Window? parent = null)
    {
        parent ??= GetMainWindow();
        if (parent == null) return null;

        var choices = RunningWindowedApps();
        AppIdentity? result = null;

        var list = new ListBox
        {
            ItemsSource = choices,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ProcessChoice.Display)),
            Height = 320,
            Margin = new Thickness(0, 0, 0, 16),
        };
        var addBtn = new Button { Content = "Add", Padding = new Thickness(24, 8), FontSize = 13, IsEnabled = false };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Thickness(24, 8), FontSize = 13 };

        var dialog = new Window
        {
            Title = "Add application",
            Width = 460,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Pick a running application to give its own preset:",
                        FontSize = 13, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap,
                    },
                    list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, addBtn },
                    },
                },
            },
        };

        list.SelectionChanged += (_, _) => addBtn.IsEnabled = list.SelectedItem is ProcessChoice;
        void Commit()
        {
            if (list.SelectedItem is ProcessChoice c) { result = c.Identity; dialog.Close(); }
        }
        list.DoubleTapped += (_, _) => Commit();
        addBtn.Click += (_, _) => Commit();
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(parent);
        return result;
    }

    private static System.Collections.Generic.List<ProcessChoice> RunningWindowedApps()
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choices = new System.Collections.Generic.List<ProcessChoice>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                var name = p.ProcessName;
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { /* elevated/UWP */ }
                var exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                choices.Add(new ProcessChoice($"{exe}  —  {p.MainWindowTitle}", new AppIdentity(path, exe)));
            }
            catch { /* process exited mid-enumeration */ }
            finally { p.Dispose(); }
        }
        return choices.OrderBy(c => c.Display, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
