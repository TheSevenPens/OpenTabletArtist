using System.IO.Pipes;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

// DaemonProbe — a headless smoke test for the OpenTabletDriver daemon connection (#140).
//
// It mirrors OpenTabletArtist.Services.DaemonClient's exact connect + call path:
//   NamedPipeClientStream("OpenTabletDriver.Daemon") -> new JsonRpc(pipe) -> InvokeAsync<T>(method)
// and round-trips GetTablets + GetSettings against whatever daemon is listening on that pipe (the
// bundled OTD.app daemon, or one you built from the submodule). Its purpose is to prove — without the
// GUI — that the .NET named-pipe transport reaches the daemon on the current OS (originally: does macOS
// work). On macOS/Linux .NET maps the "named pipe" onto a Unix-domain socket transparently.
//
// Usage:   dotnet run --project tools/DaemonProbe [pipeName] [connectTimeoutMs]
// Exit:    0 = round-trip OK   2 = couldn't connect   3 = connected but an RPC call failed

string pipeName = args.Length > 0 ? args[0] : "OpenTabletDriver.Daemon";
int timeoutMs = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 5000;

Console.WriteLine($"[probe] connecting to pipe '{pipeName}' (timeout {timeoutMs} ms) ...");

using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
try
{
    await pipe.ConnectAsync(timeoutMs);
}
catch (Exception ex)
{
    Console.WriteLine($"[probe] CONNECT FAILED: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("[probe] Is a daemon running and listening on that pipe? " +
                      "(The daemon binds the pipe only after startup/tablet-detection completes — retry if it just launched.)");
    return 2;
}
Console.WriteLine("[probe] pipe connected. Attaching JsonRpc...");

var rpc = new JsonRpc(pipe);
rpc.StartListening();

try
{
    var tablets = await rpc.InvokeAsync<JArray>("GetTablets");
    Console.WriteLine($"[probe] GetTablets OK -> {tablets.Count} tablet(s)");
    foreach (var tablet in tablets)
        Console.WriteLine($"          - {tablet["Properties"]?["Name"] ?? tablet["Name"] ?? "(unnamed)"}");

    var settings = await rpc.InvokeAsync<JToken>("GetSettings");
    var isNull = settings is null || settings.Type == JTokenType.Null;
    Console.WriteLine($"[probe] GetSettings OK -> {(isNull ? "null (no active settings)" : "settings object returned")}");
    if (!isNull)
    {
        var keys = settings is JObject o ? string.Join(", ", o.Properties().Select(p => p.Name).Take(12)) : "(non-object)";
        Console.WriteLine($"          top-level keys: {keys}");
        if (settings?["Profiles"] is JArray profiles) Console.WriteLine($"          Profiles: {profiles.Count}");
    }
    Console.WriteLine("[probe] ROUND-TRIP SUCCEEDED.");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"[probe] RPC CALL FAILED: {ex.GetType().Name}: {ex.Message}");
    return 3;
}
finally
{
    rpc.Dispose();
}
