using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Every view in the app is built and laid out once (#741).
///
/// XAML is compiled, so a malformed file fails the build — but a <c>StaticResource</c> that no longer
/// exists, a style selector pointing at a renamed class, a converter that was moved, or a control
/// template referencing a deleted part all fail at <em>load</em>, on the user's machine, the first time
/// that page is opened. CI built the app and ran domain tests without ever constructing a view, so none
/// of that was covered.
///
/// Data-driven on purpose: a view added during the redesign is covered the day it lands, with no test to
/// remember to write.
/// </summary>
public class ViewLoadTests
{
    /// <summary>Every UserControl the app defines that can be constructed without arguments.</summary>
    public static TheoryData<string> ViewTypeNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in Views())
                data.Add(type.FullName!);
            return data;
        }
    }

    private static IEnumerable<Type> Views() =>
        typeof(OpenTabletArtist.App).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(UserControl).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    [AvaloniaTheory]
    [MemberData(nameof(ViewTypeNames))]
    public void EveryView_BuildsAndLaysOut(string typeName)
    {
        var type = typeof(OpenTabletArtist.App).Assembly.GetType(typeName)
                   ?? throw new InvalidOperationException($"No such view type: {typeName}");

        // Construction runs InitializeComponent, which is where a missing resource or a broken template
        // throws; measure/arrange is where the visual tree is actually realised.
        var view = (UserControl)Activator.CreateInstance(type)!;
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        window.Show();
        window.Measure(new Size(1200, 900));
        window.Arrange(new Rect(0, 0, 1200, 900));
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.IsInitialized, $"{typeName} did not finish initialising.");
    }

    /// <summary>
    /// Guards the guard: if the discovery query stops finding views — a namespace move, a base-class
    /// change, a build that didn't produce the app assembly — the theory above would silently run zero
    /// cases and report green over an untested UI.
    /// </summary>
    [AvaloniaFact]
    public void TheViewDiscoveryFindsTheAppsViews()
    {
        var views = Views().ToList();

        Assert.True(views.Count >= 10,
            $"Expected the app to define many views, found {views.Count}: "
            + string.Join(", ", views.Select(v => v.Name)));
    }
}
