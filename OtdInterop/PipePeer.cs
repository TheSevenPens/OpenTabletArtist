using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OtdInterop;

/// <summary>
/// Which process is on the other end of a connected named pipe — the daemon's PID, for the ownership
/// check and for stopping the one daemon we are talking to rather than every process by that name.
/// </summary>
///
/// <remarks>
/// <para>
/// Windows asks the pipe directly (<c>GetNamedPipeServerProcessId</c>). On macOS, .NET implements a
/// named pipe as a Unix-domain socket, and the <see cref="SafePipeHandle"/> holds that socket's file
/// descriptor, so the kernel can name the peer through <c>getsockopt(LOCAL_PEERPID)</c> (#140). Before
/// this, the lookup was Windows-only and Stop on macOS fell back to killing every daemon (#728).
/// </para>
/// <para>
/// The macOS answer is the last process to use the server's end of the socket. For the daemon that is
/// the daemon itself; a server that passed its accepted socket to another process would be reported as
/// that process, which OTD does not do.
/// </para>
/// <para>
/// Linux has the same shape through <c>SO_PEERCRED</c>; it is not wired up yet, so it reports
/// unsupported and callers keep their existing fallback.
/// </para>
/// </remarks>
internal static class PipePeer
{
    /// <summary>Whether this OS can name the peer at all. Where it can't, callers fall back.</summary>
    internal static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// The server's process ID. False when the OS can't say; on Windows and macOS the error is then in
    /// <see cref="Marshal.GetLastPInvokeError"/> (a Win32 code or an errno), and false with no error set
    /// where the platform is unsupported.
    /// </summary>
    internal static bool TryGetServerProcessId(SafePipeHandle handle, out int pid)
    {
        pid = 0;
        if (OperatingSystem.IsWindows())
        {
            if (!GetNamedPipeServerProcessId(handle, out uint serverPid)) return false;
            pid = checked((int)serverPid);
            return true;
        }
        if (OperatingSystem.IsMacOS()) return TryGetPeerPidMacOS(handle, out pid);
        return false;
    }

    private static bool TryGetPeerPidMacOS(SafePipeHandle handle, out int pid)
    {
        pid = 0;
        bool added = false;
        try
        {
            // Held across the call so the descriptor can't be closed and reused underneath us.
            handle.DangerousAddRef(ref added);
            int fd = checked((int)handle.DangerousGetHandle());
            int value = 0;
            uint size = sizeof(int);
            if (getsockopt(fd, SOL_LOCAL, LOCAL_PEERPID, ref value, ref size) != 0) return false;
            pid = value;
            return pid > 0;
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    // <sys/un.h>: SOL_LOCAL is the socket level for Unix-domain options, LOCAL_PEERPID returns a pid_t.
    private const int SOL_LOCAL = 0;
    private const int LOCAL_PEERPID = 0x002;

    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(int socket, int level, int optionName, ref int optionValue, ref uint optionLen);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
