using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenTabletArtist.ViewModels;
using OpenTabletArtist.Views;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Profiles;
using OtdInterop;
using Xunit;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// The Display Mapping tab as a laid-out control: what its rotation dropdown offers, and whether its
/// measurements fit (#932).
/// </summary>
///
/// <remarks>
/// <see cref="ViewBindingTests"/> proves this tab's bindings reach something, which is a different
/// question from whether the result fits in the window or can be operated. Both findings these cover
/// were invisible to it: a table wider than the narrowest supported window binds perfectly, and so does
/// a control that is enabled when it should not be.
/// </remarks>
public class MappingTabTests
{
    /// <summary>A tablet with a real digitizer, so the measurement cells carry real strings.</summary>
    private const float WidthMm = 269, HeightMm = 168;

    private static Settings Mapped(string tablet)
    {
        var settings = new Settings { Profiles = new ProfileCollection { new Profile { Tablet = tablet } } };
        settings.Profiles.First().AbsoluteModeSettings = new AbsoluteModeSettings
        {
            Tablet = new AreaSettings { Width = WidthMm, Height = HeightMm, X = WidthMm / 2, Y = HeightMm / 2 },
        };
        return settings;
    }

    /// <summary>MainWindow's page padding. The view is never the full width of the window, so a
    /// layout check that puts it there measures a page 48px wider than the real one.</summary>
    private const double ShellPadding = 24;

    private static (Window Window, TabletDetailView View, TabletDetailViewModel Vm) MappingTab(
        Settings settings, System.Func<Settings, Task<SettingsApplyOutcome>> apply, double width = 1100)
    {
        var vm = new TabletDetailViewModel(
            settings.Profiles.First(), settings,
            applyAction: apply,
            refreshAction: () => Task.FromResult<(Settings?, Profile?)>((settings, settings.Profiles.First())),
            tabletDigitizer: (WidthMm, HeightMm));
        // The view consumes the pending tab when it attaches, so ask before showing it.
        vm.RequestTab(TabletDetailTab.DisplayMapping);

        var view = new TabletDetailView { DataContext = vm };
        var page = new Border { Child = view, Padding = new Thickness(ShellPadding, 0) };
        var window = new Window { Content = page, Width = width, Height = 900 };
        window.Show();
        window.Measure(new Size(width, 900));
        window.Arrange(new Rect(0, 0, width, 900));
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static ComboBox RotationBox(TabletDetailView view) =>
        view.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.ItemsSource is IEnumerable<RotationOption>);

    /// <summary>
    /// The measurements fit at the narrowest window the app allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every cell carries both units now, which made the widest of them about 182px of text in a column
    /// that is ~127px at <c>MainWindow.MinWidth</c> once the page padding is taken off. They were
    /// clipped, and nothing noticed: a binding that resolves to a string too long for its cell is a
    /// perfectly good binding.
    /// </para>
    /// <para>
    /// 800 is that minimum, so it is the worst case rather than an arbitrary narrow one. 1100 is the
    /// control: there is room for one line there, and it must still be one line.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(800)]
    [InlineData(1100)]
    public void TheMeasurementCellsFitTheWindow(double width)
    {
        var settings = Mapped("T");
        var (_, view, _) = MappingTab(settings, _ => Task.FromResult(SettingsApplyOutcome.Live), width);

        // The dual-unit cells, found by the unit they end with rather than by position in the grid:
        // tablet and active-area dimensions, and the two diagonals.
        var cells = view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => (t.Text ?? "").Contains(" in)"))
            .ToList();

        // Named so that a table that stops producing them fails here rather than passing vacuously.
        Assert.Equal(4, cells.Count);

        foreach (var cell in cells)
        {
            // TextLayout is what was actually laid out, so this reads the wrapping rather than assuming
            // it: the widest line against the space the cell was given, and the block of lines against
            // the height the row gave it.
            Assert.True(cell.Bounds.Width + 0.5 >= cell.TextLayout.Width,
                $"at {width}px, '{cell.Text}' has {cell.Bounds.Width:0.#}px for "
                + $"{cell.TextLayout.Width:0.#}px of laid-out text");
            Assert.True(cell.Bounds.Height + 0.5 >= cell.TextLayout.Height,
                $"at {width}px, '{cell.Text}' has {cell.Bounds.Height:0.#}px of height for "
                + $"{cell.TextLayout.Height:0.#}px of laid-out text");
        }

        if (width >= 1100)
        {
            Assert.All(cells, c => Assert.Equal(1, c.TextLayout.TextLines.Count));
        }
        else
        {
            // And the narrow case is really the narrow case: at least one of them has to wrap, or this
            // test would pass just as well against a table that never needed to.
            Assert.Contains(cells, c => c.TextLayout.TextLines.Count > 1);
        }
    }

    /// <summary>The dropdown lists the four angles, and shows the one the profile stores.</summary>
    [AvaloniaFact]
    public void TheRotationDropdownOffersTheFourAngles()
    {
        var settings = Mapped("T");
        settings.Profiles.First().AbsoluteModeSettings!.Tablet!.Rotation = 180;
        var (_, view, _) = MappingTab(settings, _ => Task.FromResult(SettingsApplyOutcome.Live));

        var box = RotationBox(view);

        Assert.Equal(["None", "90°", "180°", "270°"],
            ((IEnumerable<RotationOption>)box.ItemsSource!).Select(o => o.Label));
        Assert.Equal(180, ((RotationOption)box.SelectedItem!).Degrees);
    }

    /// <summary>
    /// The dropdown closes while its apply runs, which is what keeps a second choice from being lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The radio buttons this replaced got that for free: an <c>AsyncRelayCommand</c> reports it cannot
    /// execute while it is running. A two-way property has no such gate, and without one a choice made
    /// during the apply is judged against the rotation the profile had <em>before</em> it — so it looks
    /// like a no-op and is dropped, and the refresh then snaps the dropdown back.
    /// </para>
    /// <para>
    /// <c>RotationSelectionTests</c> covers the view model's half. This is the half that matters to the
    /// artist: that the control on screen is actually unavailable, through the real binding and the real
    /// theme, rather than a flag being true somewhere.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task WhileTheRotationApplyRuns_TheDropdownIsUnavailable()
    {
        var settings = Mapped("T");
        var gate = new TaskCompletionSource<SettingsApplyOutcome>();
        var (_, view, vm) = MappingTab(settings, _ => gate.Task);

        var box = RotationBox(view);
        Assert.True(box.IsEffectivelyEnabled);

        vm.SelectedRotation = ((IEnumerable<RotationOption>)box.ItemsSource!).Single(o => o.Degrees == 90);
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.IsEffectivelyEnabled);

        gate.SetResult(SettingsApplyOutcome.Live);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.True(box.IsEffectivelyEnabled);
    }
}
