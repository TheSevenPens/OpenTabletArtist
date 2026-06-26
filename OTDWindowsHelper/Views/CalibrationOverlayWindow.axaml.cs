using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using OtdWindowsHelper.Domain;
using OtdWindowsHelper.ViewModels;

namespace OtdWindowsHelper.Views;

/// <summary>
/// Full-display overlay for the 4-tap pointer calibration (#127). Covers the mapped display, draws
/// the targets + a live pen dot (driven by <see cref="CalibrationViewModel"/>), and starts/stops the
/// daemon pen stream with its own lifetime. The targets/dot are positioned in code from the VM's
/// normalized coordinates so DPI scaling doesn't distort them.
/// </summary>
public partial class CalibrationOverlayWindow : Window
{
    private readonly CalibrationViewModel? _vm;
    private readonly DisplayInfo? _display;
    private readonly List<Ellipse> _targets = new();
    private Ellipse? _liveDot;

    // Parameterless ctor for the Avalonia designer/loader.
    public CalibrationOverlayWindow()
    {
        InitializeComponent();
    }

    public CalibrationOverlayWindow(CalibrationViewModel vm, DisplayInfo display)
    {
        InitializeComponent();
        _vm = vm;
        _display = display;
        DataContext = vm;

        vm.CloseRequested += OnCloseRequested;
        vm.PropertyChanged += (_, _) => UpdateVisuals();
        Surface.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) BuildAndLayout(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) vm.CancelCommand.Execute(null); };
    }

    private void OnCloseRequested() => Close();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceOnDisplay();
        BuildAndLayout();
        _ = _vm?.StartAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm != null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _ = _vm.StopAsync();
        }
    }

    // Match the Avalonia screen to the target display by position, then size the window to it (DIPs).
    private void PlaceOnDisplay()
    {
        if (_display is not { } display) return;
        foreach (var s in Screens.All)
        {
            if (Math.Abs(s.Bounds.X - display.X) <= 2 && Math.Abs(s.Bounds.Y - display.Y) <= 2)
            {
                Position = s.Bounds.Position;
                Width = s.Bounds.Width / s.Scaling;
                Height = s.Bounds.Height / s.Scaling;
                return;
            }
        }
        Position = new PixelPoint(display.X, display.Y);
        Width = display.Width;
        Height = display.Height;
    }

    private void BuildAndLayout()
    {
        if (_vm == null) return;
        if (_targets.Count == 0)
        {
            foreach (var _ in _vm.Targets)
            {
                var ring = new Ellipse
                {
                    Width = 40, Height = 40, StrokeThickness = 3,
                    Stroke = Brushes.White, Fill = Brushes.Transparent, IsHitTestVisible = false,
                };
                _targets.Add(ring);
                Surface.Children.Add(ring);
            }
            _liveDot = new Ellipse { Width = 14, Height = 14, Fill = Brushes.OrangeRed, IsHitTestVisible = false };
            Surface.Children.Add(_liveDot);
        }
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_vm == null) return;
        double w = Surface.Bounds.Width, h = Surface.Bounds.Height;

        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _vm.Targets[i];
            var el = _targets[i];
            Canvas.SetLeft(el, t.X * w - el.Width / 2);
            Canvas.SetTop(el, t.Y * h - el.Height / 2);
            bool active = i == _vm.CurrentTarget && _vm.IsCapturing;
            el.Stroke = i < _vm.CapturedCount ? Brushes.LimeGreen
                      : active ? Brushes.DeepSkyBlue : Brushes.White;
            el.StrokeThickness = active ? 5 : 3;
        }

        if (_liveDot != null)
        {
            _liveDot.IsVisible = _vm.LiveDotVisible;
            Canvas.SetLeft(_liveDot, _vm.LiveDotX * w - _liveDot.Width / 2);
            Canvas.SetTop(_liveDot, _vm.LiveDotY * h - _liveDot.Height / 2);
        }
    }
}
