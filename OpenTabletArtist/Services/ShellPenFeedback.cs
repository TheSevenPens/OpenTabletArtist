using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace OpenTabletArtist.Services;

/// <summary>
/// Turns off the Windows shell's per-window pen/touch <em>visual</em> feedback — the ripple/contact
/// rings drawn on every tap, the press-and-hold ring, barrel/right-tap flashes, etc. For a drawing-tablet
/// app these are just noise on top of our own UI. Applied per-window via <c>SetWindowFeedbackSetting</c>:
/// no OS-wide/registry change, and it only suppresses the <em>visuals</em> — the gestures themselves
/// (e.g. press-and-hold → right-click, which our tablet nodes use) still work, so this is safe to apply
/// app-wide. Disabling a gesture's behaviour is a separate concern (WM_TABLET_QUERYSYSTEMGESTURESTATUS).
/// Windows-only; a no-op elsewhere. (Background: the devnotes "disabling shell pen/touch feedback" note.)
/// </summary>
public static class ShellPenFeedback
{
    // FEEDBACK_TYPE values (winuser.h) — every per-interaction pen/touch feedback the shell can draw.
    private static readonly uint[] AllFeedbackTypes =
    {
        1,  // FEEDBACK_TOUCH_CONTACTVISUALIZATION
        2,  // FEEDBACK_PEN_BARRELVISUALIZATION
        3,  // FEEDBACK_PEN_TAP
        4,  // FEEDBACK_PEN_DOUBLETAP
        5,  // FEEDBACK_PEN_PRESSANDHOLD
        6,  // FEEDBACK_PEN_RIGHTTAP
        7,  // FEEDBACK_TOUCH_TAP
        8,  // FEEDBACK_TOUCH_DOUBLETAP
        9,  // FEEDBACK_TOUCH_PRESSANDHOLD
        10, // FEEDBACK_TOUCH_RIGHTTAP
        11, // FEEDBACK_GESTURE_PRESSANDTAP
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowFeedbackSetting(IntPtr hwnd, uint feedback, uint dwFlags, uint size, ref int configuration);

    /// <summary>Disable all shell pen/touch visual feedback for the window with this HWND.</summary>
    public static void DisableFor(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return;
        int disabled = 0; // BOOL FALSE
        foreach (var feedback in AllFeedbackTypes)
            SetWindowFeedbackSetting(hwnd, feedback, 0, sizeof(int), ref disabled);
    }

    /// <summary>Resolve a window's HWND and disable feedback on it. Call once the window is open (its
    /// platform handle must exist, e.g. from <c>OnOpened</c>). No-op when there's no HWND / off-Windows.</summary>
    public static void DisableFor(TopLevel? topLevel)
    {
        if (topLevel?.TryGetPlatformHandle() is { } handle)
            DisableFor(handle.Handle);
    }

    /// <summary>Disable feedback as soon as the window opens (its HWND exists then). One-liner for
    /// dialogs — subscribe at creation, no need to override <c>OnOpened</c>.</summary>
    public static void DisableOnOpen(Window window)
    {
        window.Opened += (_, _) => DisableFor(window);
    }
}
