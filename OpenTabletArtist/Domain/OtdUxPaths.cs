using System.IO;

namespace OpenTabletArtist.Domain;

/// <summary>
/// Locates OpenTabletDriver's own WPF UX project in the submodule. Pure (no filesystem access) so the
/// resolution is unit-testable; the caller checks whether the directory exists. Only a development
/// tree has these sources — a published build bundles the daemon but not the UX — which is what gates
/// the Daemon page's OTD UX card. Mirrors <see cref="DaemonExePaths"/>, minus the bundled
/// candidate: there is no released layout that ships the UX.
/// </summary>
public static class OtdUxPaths
{
    /// <summary>The UX project's folder name inside the submodule.</summary>
    public const string ProjectFolderName = "OpenTabletDriver.UX.Wpf";

    /// <summary>The UX project as resolved from the app's base directory: dev build tree
    /// <c>&lt;app&gt;/bin/&lt;cfg&gt;/net10.0</c> → up to the repo root → the submodule's UX project.
    /// From any other layout this points at a path that doesn't exist, which is the intended answer.</summary>
    public static string ProjectPath(string baseDir) => Path.GetFullPath(Path.Combine(
        baseDir, "..", "..", "..", "..",
        "external", "OpenTabletDriver", ProjectFolderName));
}
