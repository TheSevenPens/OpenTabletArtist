using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Every command a view names through its page's DataContext exists on that page's view model (#889).
/// </summary>
///
/// <remarks>
/// <para>
/// A binding to a command that does not exist fails at runtime, in a log line nobody reads, and the
/// button simply does nothing. That is how <c>FollowLinkCommand</c> was lost: a method inserted between
/// <c>FollowLink</c>'s doc comment and <c>FollowLink</c> itself took its <c>[RelayCommand]</c> with it, so
/// the generator stopped emitting the command while the XAML went on binding it. Every review link on
/// every health card was dead for twelve days, and nothing failed.
/// </para>
/// <para>
/// Only the <c>…DataContext.XxxCommand</c> form is checked, because only that form says unambiguously
/// which object is meant to carry the command. A bare <c>{Binding XxxCommand}</c> inside a DataTemplate
/// resolves against the item, not the page, and a scan that assumed otherwise would report failures that
/// are not real — which is worse than the gap, since the guard would then be ignored.
/// </para>
/// </remarks>
public class BoundCommandsExistTests
{
    [Fact]
    public void EveryCommandBoundThroughAPageDataContextExists()
    {
        var missing = new List<string>();
        var checkedCount = 0;

        foreach (var view in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml"))
        {
            var viewModel = ViewModelFor(view);
            if (viewModel is null) continue;   // a view with no view model of its own has nothing to check

            foreach (var command in CommandsBoundThroughDataContext(File.ReadAllText(view)))
            {
                checkedCount++;
                if (viewModel.GetProperty(command) is null)
                    missing.Add($"{Path.GetFileName(view)} binds {viewModel.Name}.{command}, which does not exist");
            }
        }

        // If the scan silently stops matching anything, it would pass forever while proving nothing.
        Assert.True(checkedCount > 0, "the scan found no bindings at all, so it is no longer checking anything");
        Assert.Empty(missing);
    }

    /// <summary>
    /// The names in <c>{Binding $parent[…].DataContext.XxxCommand}</c> and <c>{Binding #x.DataContext.Y}</c>.
    /// </summary>
    private static IEnumerable<string> CommandsBoundThroughDataContext(string xaml) =>
        Regex.Matches(xaml, @"DataContext\.(\w+Command)\b")
            .Select(m => m.Groups[1].Value)
            .Distinct();

    /// <summary>The view model a view is named for, or null when there is no such type.</summary>
    private static Type? ViewModelFor(string viewPath)
    {
        var name = Path.GetFileNameWithoutExtension(viewPath);
        if (!name.EndsWith("View", StringComparison.Ordinal)) return null;

        return typeof(OpenTabletArtist.ViewModels.DashboardViewModel).Assembly
            .GetType($"OpenTabletArtist.ViewModels.{name}Model");
    }

    /// <summary>
    /// The repository's Views folder, found from this file rather than from the build output.
    /// </summary>
    /// <remarks>
    /// <c>AppContext.BaseDirectory</c> does not work here: this repository redirects
    /// <c>BaseOutputPath</c> while the app holds <c>bin/</c>, so walking up from the output lands
    /// somewhere else entirely.
    /// </remarks>
    private static string ViewsDirectory([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile)!;            // tests/OpenTabletArtist.Tests
        var repo = Path.GetFullPath(Path.Combine(dir, "..", ".."));
        return Path.Combine(repo, "OpenTabletArtist", "Views");
    }
}
