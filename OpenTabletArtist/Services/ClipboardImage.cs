using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>
/// Copies a raster image to the Windows clipboard as a device-independent bitmap (CF_DIB), which
/// pastes into Paint, Office, browsers, chat apps, etc. Windows-only (a no-op returning false
/// elsewhere). Input is top-down 32-bit BGRA (row stride = width*4); the drawing is opaque, so the
/// alpha byte is ignored by consumers.
/// </summary>
public static class ClipboardImage
{
    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int BI_RGB = 0;
    private const int HeaderSize = 40; // BITMAPINFOHEADER

    /// <summary>Places the image on the clipboard. Returns false on non-Windows, bad input, or a Win32
    /// failure (the caller can surface that however it likes).</summary>
    public static bool CopyBgra(byte[] bgraTopDown, int width, int height)
    {
        if (!OperatingSystem.IsWindows() || width <= 0 || height <= 0) return false;
        int stride = width * 4;
        if (bgraTopDown.Length < (long)stride * height) return false;

        int dataSize = stride * height;
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(HeaderSize + dataSize));
        if (hMem == IntPtr.Zero) return false;

        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return false; }
        try
        {
            var header = new byte[HeaderSize]; // zero-filled: XPelsPerMeter/ClrUsed/… stay 0
            BitConverter.GetBytes(HeaderSize).CopyTo(header, 0);  // biSize
            BitConverter.GetBytes(width).CopyTo(header, 4);       // biWidth
            BitConverter.GetBytes(height).CopyTo(header, 8);      // biHeight (positive → bottom-up)
            BitConverter.GetBytes((short)1).CopyTo(header, 12);   // biPlanes
            BitConverter.GetBytes((short)32).CopyTo(header, 14);  // biBitCount
            BitConverter.GetBytes(BI_RGB).CopyTo(header, 16);     // biCompression
            BitConverter.GetBytes(dataSize).CopyTo(header, 20);   // biSizeImage
            Marshal.Copy(header, 0, ptr, HeaderSize);

            // DIB rows are bottom-up; flip the top-down source.
            IntPtr pixels = ptr + HeaderSize;
            for (int y = 0; y < height; y++)
                Marshal.Copy(bgraTopDown, (height - 1 - y) * stride, pixels + y * stride, stride);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        if (!OpenClipboard(IntPtr.Zero)) { GlobalFree(hMem); return false; }
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_DIB, hMem) == IntPtr.Zero)
            {
                GlobalFree(hMem); // SetClipboardData failed → we still own the handle
                return false;
            }
            return true; // on success the system owns hMem — must NOT free it
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
}
