using OpenTabletArtist.Services;
using OtdInterop;

// This diagnostic now inspects a running daemon without switching it or writing its settings.
if (args is not ["--read-only"])
{
    Console.WriteLine("Usage: OtdDaemonSwitchCheck --read-only");
    Console.WriteLine("Inspect the current daemon connection and persistence status. No settings are changed.");
    return 2;
}
using var session = OtdSession.Create(AppLogBridge.Instance, new DaemonLifecycleService());
var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
session.Connected += _ => connected.TrySetResult();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
await session.ConnectAsync(timeout.Token);
try { await connected.Task.WaitAsync(timeout.Token); }
catch (OperationCanceledException) { Console.Error.WriteLine("The daemon did not become ready."); return 1; }
Console.WriteLine(session.ConnectedExecutablePath ?? "Unknown executable");
if (session.Settings is not { } settings)
{
    Console.Error.WriteLine(session.SettingsProblem);
    return 1;
}
Console.WriteLine($"Profiles: {settings.GetCurrent()?.Settings.Profiles.Count ?? 0}");
Console.WriteLine(settings.HasUnsavedChanges ? "Live settings differ from the saved file, or persistence is unknown." : "Live settings match the saved file.");
return 0;
