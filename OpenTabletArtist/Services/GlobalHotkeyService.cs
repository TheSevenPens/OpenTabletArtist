using System;
using System.Runtime.InteropServices;

namespace OpenTabletArtist.Services;

/// <summary>
/// System-wide keyboard hotkeys on Windows via a hidden message-only window + <c>RegisterHotKey</c>
/// (#320). A message-only window is used so <c>WM_HOTKEY</c> is delivered to our own WndProc, which is
/// pumped by the Avalonia UI thread's message loop — so this must be constructed and driven on the UI
/// thread. Registrations survive the main window being minimized to the tray (#72).
///
/// Callers register a chord and get back an opaque id; <see cref="HotkeyPressed"/> fires (on the UI
/// thread) with that id when the chord is pressed. Register returns 0 when the chord is already owned by
/// another application, so the caller can surface a conflict rather than fail silently.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000; // one WM_HOTKEY per press, not one per auto-repeat tick
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    private readonly IntPtr _hwnd;
    private readonly WndProc _wndProc; // held so the delegate isn't collected while native code holds it
    private int _nextId = 1;

    /// <summary>Fired on the UI thread with the id returned by <see cref="TryRegister"/> when its chord fires.</summary>
    public event Action<int>? HotkeyPressed;

    public GlobalHotkeyService()
    {
        _wndProc = WndProcImpl;
        try
        {
            var cls = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = "OTA_Hotkeys_" + Guid.NewGuid().ToString("N"),
            };
            RegisterClassEx(ref cls);
            _hwnd = CreateWindowEx(0, cls.lpszClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE,
                IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        }
        catch
        {
            _hwnd = IntPtr.Zero; // no window → registrations no-op (non-Windows / P/Invoke unavailable)
        }
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((int)wParam);
            return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Register a chord. Returns an id (&gt; 0) on success, or 0 if the chord is unmapped, the
    /// window is unavailable, or another app already owns the chord (conflict).</summary>
    public int TryRegister(HotkeyChord chord)
    {
        if (_hwnd == IntPtr.Zero || !chord.IsRegisterable) return 0;
        int id = _nextId++;
        return RegisterHotKey(_hwnd, id, chord.Win32Modifiers | MOD_NOREPEAT, chord.Win32VirtualKey) ? id : 0;
    }

    public void Unregister(int id)
    {
        if (_hwnd != IntPtr.Zero && id > 0) UnregisterHotKey(_hwnd, id);
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }
}
