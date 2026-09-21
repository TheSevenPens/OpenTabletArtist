namespace OtdInterop;

/// <summary>The executable on the current connection. Settings are initialized before this notification.</summary>
public readonly record struct DaemonChange(string? ExecutablePath, int ConnectionId);
