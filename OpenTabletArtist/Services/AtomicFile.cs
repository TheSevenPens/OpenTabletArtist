using System;
using System.IO;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>
/// Replaces a file's contents without ever leaving it half-written or missing (#733, #768).
///
/// The naive write — open the destination for truncation and stream into it — destroys the previous
/// contents before it knows the new ones are good. An interruption, a serialization failure, or a disk
/// that fills up part-way then leaves nothing usable behind. Here the content goes to a temporary file
/// in the same directory, is flushed all the way to disk, and only then swapped in, so a failure at any
/// point leaves the previous file exactly where it was.
///
/// The swap keeps what it replaced as a <see cref="BackupSuffix"/> copy. That backup exists only when a
/// previous write completed, so it is by construction the last content known to have been written
/// successfully — which is what makes it worth loading from when the main file turns out to be corrupt.
///
/// Extracted from <see cref="SettingsFileStore"/> so OTA's own preferences get the same treatment; #733
/// asked for all three writers and only covered two.
/// </summary>
public static class AtomicFile
{
    /// <summary>Suffix of the last-known-good copy kept beside a file written through here.</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>
    /// Writes <paramref name="contents"/> as UTF-8 without a byte-order mark, matching what
    /// <c>File.WriteAllText</c> produces so existing files stay byte-identical.
    /// </summary>
    public static void WriteText(string path, string contents) =>
        Write(path, stream =>
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length);
        });

    /// <summary>
    /// Hands <paramref name="writeContent"/> a stream to write the new contents to, then swaps the result
    /// into <paramref name="path"/>. The stream stays open until this method has flushed it, so callers
    /// wrapping it in a <c>StreamWriter</c> must pass <c>leaveOpen: true</c>.
    ///
    /// Throws on failure, having touched nothing at <paramref name="path"/>.
    /// </summary>
    public static void Write(string path, Action<Stream> writeContent)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);

        // Same directory as the destination: a cross-volume temp file would make the swap a copy, which
        // is neither atomic nor guaranteed to succeed.
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writeContent(stream);
                // Force the bytes out of the OS cache before the swap. Without this a crash right after
                // the rename can leave a correctly-named file with no content in it.
                stream.Flush(flushToDisk: true);
            }

            Replace(temp, path);
        }
        catch
        {
            // The destination was never touched — drop the partial temp file and let the caller hear why.
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Swaps <paramref name="temp"/> into <paramref name="path"/>, retaining the previous
    /// contents as the last-known-good backup.</summary>
    private static void Replace(string temp, string path)
    {
        if (!File.Exists(path))
        {
            // Nothing to preserve or replace — first write, or the file was removed behind us.
            File.Move(temp, path);
            return;
        }

        var backup = path + BackupSuffix;
        try
        {
            // Atomic where the platform supports it, and it writes the backup as part of the same
            // operation — the previous contents survive even a crash mid-swap.
            File.Replace(temp, path, backup, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceByMove(temp, path, backup);
        }
        catch (IOException)
        {
            // Some filesystems (notably a few network and FUSE mounts) reject Replace outright. Fall back
            // to a copy-aside + move, which is still strictly safer than truncate-then-write.
            ReplaceByMove(temp, path, backup);
        }
    }

    private static void ReplaceByMove(string temp, string path, string backup)
    {
        File.Copy(path, backup, overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup; the temp name is unique so a leftover can't corrupt anything */ }
    }
}
