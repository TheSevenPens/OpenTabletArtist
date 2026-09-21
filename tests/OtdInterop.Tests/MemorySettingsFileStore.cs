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
    public bool TrySave(Settings settings, string path)
    {
        Attempts++;
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
