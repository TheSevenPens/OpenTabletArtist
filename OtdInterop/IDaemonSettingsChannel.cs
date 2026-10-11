using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;

namespace OtdInterop;
/// <summary>Internal raw settings access. Bind captures a channel so writes cannot migrate.</summary>
internal interface IDaemonSettingsChannel
{
    // Zero means disconnected; a new connection always has a new number.
    int Incarnation { get; }
    IDaemonSettingsBinding Bind();
}
internal interface IDaemonSettingsBinding
{
    // Zero means disconnected; a new connection always has a new number.
    int Incarnation { get; }
    Task<Settings?> GetSettingsAsync();
    Task<bool> SetSettingsAsync(Settings settings);
    /// <summary>The tablets the daemon has detected, as it returns them (TabletReference[]). Empty when there is no transport.</summary>
    Task<JArray> GetTabletsAsync();
}
