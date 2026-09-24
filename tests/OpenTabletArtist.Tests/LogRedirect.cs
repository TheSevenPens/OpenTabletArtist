using System;
using System.IO;
using System.Runtime.CompilerServices;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.Tests;

/// <summary>
/// Sends this assembly's log lines somewhere that is not the user's diagnostics file (#834).
/// </summary>
///
/// <remarks>
/// <para>
/// The suites used to append to <c>%LOCALAPPDATA%\OpenTabletArtist\logs\app.log</c> — the file a user is
/// asked for when they report a problem. That is not untidiness: the log rolls at 1 MB and keeps one
/// previous generation, so a few runs between reproducing a bug and collecting the log can push the real
/// evidence out of both. It also wrote <c>[ERROR]</c> lines about failures that never happened to anyone.
/// </para>
/// <para>
/// A module initializer because it has to win a race it cannot see: <c>AppLog</c> writes as soon as
/// anything in the app touches it, and a fixture that ran per-class or per-collection would be too late
/// for whatever logged first. This runs before any code in this assembly does.
/// </para>
/// <para>
/// One directory per process, so parallel runs of the two suites cannot interleave into one file, and
/// nothing has to be cleaned up between runs.
/// </para>
/// </remarks>
internal static class LogRedirect
{
    [ModuleInitializer]
    internal static void SendLogsToTemp()
    {
        // Before AppLog is touched at all, not merely before it is used. AppLog builds its log directory
        // from AppPaths.LocalAppData in a static field, and that path is resolved once — so the first
        // mention of AppLog decides where OTA's data lives for the whole run. Module initializer order
        // within an assembly is unspecified, which is why this is a call and not a second attribute
        // (#947).
        TestAppDataRoot.Ensure();

        AppLog.RedirectTo(Path.Combine(
            Path.GetTempPath(), "ota-tests", $"{Environment.ProcessId}-{Guid.NewGuid():N}"));
    }
}
