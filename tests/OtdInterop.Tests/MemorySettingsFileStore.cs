using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OtdInterop;

namespace OtdInterop.Tests;

internal sealed class MemorySettingsFileStore : ISettingsFileStore
{
    public Settings? Saved { get; set; }
    public bool SaveSucceeds { get; set; } = true;
    public int Attempts { get; private set; }
    public SettingsFileSnapshot ReadPrimary(string path) =>
        new(Saved is null ? null : SettingsCodec.Clone(Saved),
            Saved is null ? "missing" : JsonConvert.SerializeObject(Saved));
    public void Save(Settings settings, string path) => TrySave(settings, path);
    /// <summary>
    /// Blocks inside the write, for the one kind of work a close cannot cancel.
    /// </summary>
    /// <remarks>
    /// Cancelling the session's lifetime unblocks everything waiting on the daemon, so an unresponsive
    /// daemon cannot stall a close. A synchronous file write can: it takes no token, and a settings file
    /// held open by something else is an ordinary way for one to sit there.
    /// </remarks>
    public ManualResetEventSlim? BlockInsideSave { get; set; }

    public bool TrySave(Settings settings, string path)
    {
        Attempts++;
        BlockInsideSave?.Wait();
        if (!SaveSucceeds) return false;
        Saved = SettingsCodec.Clone(settings);
        return true;
    }
    public bool TryLoad(string path, out Settings? settings)
    {
        settings = Saved is null ? null : SettingsCodec.Clone(Saved);
        return settings is not null;
    }
}
