using System;
using System.IO;

namespace OpenTabletArtist.Services;

/// <summary>
/// Where this application keeps its own data, and the one way to override it (#879).
/// </summary>
///
/// <remarks>
/// <para>
/// Settings and logs both used <c>Environment.GetFolderPath(LocalApplicationData)</c> directly. On
/// Windows that reads the OS known-folder and <b>ignores <c>%LOCALAPPDATA%</c></b>, which the release
/// workflow's packaged-app smoke test had been setting in the belief that it sandboxed the run. The
/// daemon honours that variable, so the isolation looked like it worked — only OTA's own files escaped.
/// </para>
/// <para>
/// The cost was not a stray write. A packaged-app test on a developer machine inherited that machine's
/// configuration, and #880 spent a diagnosis on behaviour that turned out to be the developer's own
/// <c>daemon.userPath</c> rather than the artifact's. The failure mode is a plausible wrong conclusion,
/// which is worse than an error.
/// </para>
/// <para>
/// So there is one override, read once at startup, honoured by everything that writes here.
/// <see cref="OverrideVariable"/> is deliberately specific to this application rather than reusing
/// <c>LOCALAPPDATA</c>: the daemon is a separate process with its own data, and conflating them is how
/// this became confusing in the first place.
/// </para>
/// </remarks>
public static class AppPaths
{
    /// <summary>The environment variable that redirects this application's data directory.</summary>
    public static readonly string OverrideVariable = "OTA_APPDATA";

    /// <summary>
    /// The directory holding this application's settings and logs.
    /// </summary>
    /// <remarks>
    /// Resolved once. A redirect is a decision made before launch — a test host or a smoke step setting
    /// it — not something to change under a running app, where half the files would already be elsewhere.
    /// </remarks>
    public static string LocalAppData { get; } =
        Resolve(Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>
    /// The directory an override value selects, or the user's own when there is none.
    /// </summary>
    /// <remarks>
    /// Separate from the property so the decision can be tested without a process boundary. A blank or
    /// whitespace value is treated as absent rather than as a request to write to the current directory,
    /// because an environment variable that is set but empty is almost always an accident.
    /// </remarks>
    public static string Resolve(string? overrideValue) =>
        string.IsNullOrWhiteSpace(overrideValue)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenTabletArtist")
            : Path.Combine(overrideValue, "OpenTabletArtist");
}
