using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenTabletArtist.Views;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The top nav does not move when the tablet switcher appears beside it.
/// </summary>
///
/// <remarks>
/// <para>
/// The nav row is Auto-height with the wordmarks aligned to its BOTTOM, and the switcher shows only on
/// the pages that have a tablet to pick. The switcher is a pixel taller than the wordmarks, so the row
/// grew by a pixel on those pages and the whole nav rode down with it — small enough to read as the
/// window twitching rather than as a layout change, and easy to reintroduce without noticing.
/// </para>
/// <para>
/// The fix reserves the taller of the two on every page (<c>NavRowHeight</c>), which is a constant that
/// has to keep matching the switcher. This is what stops it drifting: it measures the real control rather
/// than trusting the number, so padding or a font change inside the switcher fails here instead of
/// quietly restoring the twitch.
/// </para>
/// </remarks>
public class NavRowStaysPutTests
{
    [AvaloniaFact]
    public void ShowingTheTabletSwitcher_DoesNotMoveTheWordmarks()
    {
        var (withoutY, withoutHeight) = MeasureNav(withSwitcher: false);
        var (withY, withHeight) = MeasureNav(withSwitcher: true);

        Assert.Equal(withoutY, withY);
        Assert.Equal(withoutHeight, withHeight);
    }

    /// <summary>The reserved height is still the switcher's, so reserving it still hides the difference.</summary>
    /// <remarks>
    /// Separate from the test above because it fails for a different reason. That one says the nav moved;
    /// this one says why it is about to — the switcher outgrew the space held for it.
    /// </remarks>
    [AvaloniaFact]
    public void TheReservedHeight_IsStillTallEnoughForTheSwitcher()
    {
        var reserved = (double)Application.Current!.FindResource("NavRowHeight")!;

        // In a StackPanel, not as the window's content: a stretched child reports the window's height,
        // which is what this test measured first time and why it failed on its own harness.
        var switcher = new TabletSwitcherBar { Tablets = new[] { "Wacom One" } };
        var host = new StackPanel { Orientation = Orientation.Vertical };
        host.Children.Add(switcher);

        var w = new Window { Content = host, Width = 1200, Height = 700 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var measured = switcher.Bounds.Height;
        w.Close();

        Assert.True(measured <= reserved,
            $"the switcher is {measured} tall but only {reserved} is reserved, so it will push the nav down");
    }

    /// <summary>The nav row as MainWindow builds it: bottom-aligned wordmarks, the switcher beside them.</summary>
    private static (double WordmarkY, double NavHeight) MeasureNav(bool withSwitcher)
    {
        var wordmarks = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        foreach (var label in new[] { "home", "tablet", "pen", "settings", "advanced" })
            wordmarks.Children.Add(new RadioButton
            {
                Content = label,
                GroupName = "Wordmarks",
                Theme = (ControlTheme?)Application.Current!.FindResource("WordmarkNav"),
                Margin = new Thickness(0, 3, 22, 1),
            });

        var cluster = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Top,
        };
        cluster.Children.Add(new TabletSwitcherBar
        {
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = withSwitcher,
            Tablets = new[] { "Wacom One" },
        });

        var nav = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24, 34, 20, 0),
            MinHeight = (double)Application.Current!.FindResource("NavRowHeight")!,
        };
        Grid.SetColumn(wordmarks, 0);
        Grid.SetColumn(cluster, 2);
        nav.Children.Add(wordmarks);
        nav.Children.Add(cluster);

        var outer = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var border = new Border { Child = nav };
        Grid.SetRow(border, 0);
        outer.Children.Add(border);

        var w = new Window { Content = outer, Width = 1200, Height = 700 };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var result = (wordmarks.Bounds.Y, nav.Bounds.Height);
        w.Close();
        return result;
    }
}
