using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Proves the harness itself works before anything relies on it (#741). A binding smoke test that
/// cannot fail is worse than no test: it reports green over a broken UI. These check both directions —
/// a good binding is silent, a broken one is caught.
/// </summary>
public class HarnessTests
{
    private sealed class Person
    {
        public string Name { get; set; } = "Ada";
    }

    internal static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 800, Height = 600 };
        window.Show();
        // Binding errors surface while the tree is built and measured, not at construction.
        window.Measure(new Size(800, 600));
        window.Arrange(new Rect(0, 0, 800, 600));
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void TheHeadlessAppStarts_WithTheProductsOwnResources()
    {
        // The real App is what supplies the styles and resources every view reaches for.
        Assert.NotNull(Application.Current);
        Assert.IsType<OpenTabletArtist.App>(Application.Current);
    }

    [AvaloniaFact]
    public void AGoodBinding_ReportsNothing()
    {
        using var errors = BindingErrors.Capture();

        var text = new TextBlock { DataContext = new Person() };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(Person.Name)));
        Show(text);

        Assert.Equal("Ada", text.Text);
        errors.AssertNone("A resolvable binding");
    }

    /// <summary>
    /// The one that matters: with compiled bindings off, a path that no longer resolves is not a build
    /// error. If this test ever stops failing, the sink has stopped seeing binding diagnostics and every
    /// other binding test in this project has quietly become worthless.
    /// </summary>
    [AvaloniaFact]
    public void ABindingToAMissingProperty_IsCaught()
    {
        using var errors = BindingErrors.Capture();

        var text = new TextBlock { DataContext = new Person() };
        text.Bind(TextBlock.TextProperty, new Binding("NoSuchProperty"));
        Show(text);

        Assert.NotEmpty(errors.Messages);
        Assert.Contains(errors.Messages, m => m.Contains("NoSuchProperty", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void ABindingIntoANestedMissingProperty_IsCaught()
    {
        using var errors = BindingErrors.Capture();

        var panel = new StackPanel { Orientation = Orientation.Vertical, DataContext = new Person() };
        var good = new TextBlock();
        good.Bind(TextBlock.TextProperty, new Binding(nameof(Person.Name)));
        var bad = new TextBlock();
        bad.Bind(TextBlock.TextProperty, new Binding("Name.Missing.Deeper"));
        panel.Children.Add(good);
        panel.Children.Add(bad);
        Show(panel);

        Assert.NotEmpty(errors.Messages);
    }
}
