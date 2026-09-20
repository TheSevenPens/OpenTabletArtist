using System;
using System.IO;
using System.Runtime.Versioning;

namespace OtdInterop.Tests;

/// <summary>
/// Makes a path genuinely impossible to write for the life of a scope, the way that actually works on the
/// current OS.
///
/// On Windows the read-only attribute is enough. On Unix it is not: permission to replace or delete a file
/// comes from the containing <em>directory</em>, not the file, so an atomic swap onto a read-only file
/// succeeds — which is why the first version of these tests passed on Windows and failed on both other
/// lanes. Removing write permission from the directory is what stops the write there, and it still allows
/// reading the file back, which the durability tests need.
///
/// Shared rather than private to <c>SettingsFileStoreTests</c> because #768 gave OTA's preferences the same
/// atomic write, and with it the same trap: a read-only-attribute test that silently stops testing anything
/// on two of the three CI lanes.
/// </summary>
internal static class UnwritablePath
{
    public static IDisposable For(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            return new Restore(() => File.SetAttributes(path, FileAttributes.Normal));
        }

        return ForDirectory(path);
    }

    /// <summary>
    /// Separate and attributed because CA1416 — which this repo treats as an error — cannot see the
    /// <c>OperatingSystem.IsWindows()</c> guard from inside the restore lambda, only from the call site.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private static IDisposable ForDirectory(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var original = File.GetUnixFileMode(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restore(() => File.SetUnixFileMode(dir, original));
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _undo;
        public Restore(Action undo) => _undo = undo;
        public void Dispose() => _undo();
    }
}
