using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTabletArtist.Services;

/// <summary>
/// Copies plain text to the Windows clipboard as CF_UNICODETEXT (Win32; the app is Windows-only). A tiny
/// sibling to <see cref="ClipboardImage"/>, used by the calibration report's "Copy" action (#460) so a
/// view-model can put text on the clipboard without needing a visual/TopLevel.
/// </summary>
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static bool TrySet(string? text)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(text)) return false;

        var buffer = Encoding.Unicode.GetBytes(text + '\0');
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)buffer.Length);
        if (hMem == IntPtr.Zero) return false;

        var ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return false; }
        try { Marshal.Copy(buffer, 0, ptr, buffer.Length); }
        finally { GlobalUnlock(hMem); }

        if (!OpenClipboard(IntPtr.Zero)) { GlobalFree(hMem); return false; }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            {
                GlobalFree(hMem); // SetClipboardData failed → we still own the handle
                return false;
            }
            return true; // on success the clipboard owns hMem
        }
        finally { CloseClipboard(); }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
}
