namespace OtdHealth.Collector.Tests;

internal static class TestPipeName
{
    // On macOS, .NET puts CoreFxPipe_<name> beneath an already long temporary directory.
    // Leave room within the Unix-domain socket limit while retaining 64 bits of uniqueness.
    internal static string Create() => "hc-" + Guid.NewGuid().ToString("N")[..16];
}
