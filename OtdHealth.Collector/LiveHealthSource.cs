using Newtonsoft.Json.Linq;
using OpenTabletDriver.Configurations;
using OtdInterop;

namespace OtdHealth.Collector;

public static class LiveHealthSource
{
    public static HealthSources Create(DiagnosticsConnection connection) => new()
    {
        Connect = async ct => { await connection.ConnectAsync(ct).ConfigureAwait(false); return true; },
        Version = _ =>
        {
            if (!OperatingSystem.IsWindows()) throw new ProbeUnavailableException("OTD does not expose a version RPC and this platform cannot identify the pipe's server executable.", true);
            return Task.FromResult(FileProbes.DaemonVersion(connection.ServerExecutablePath() ?? ""));
        },
        Profiles = async ct =>
        {
            var settings = await connection.GetSettingsAsync(ct).ConfigureAwait(false);
            var tablets = await connection.GetTabletsAsync(ct).ConfigureAwait(false);
            var names = tablets.Select(t => (string?)t["Properties"]?["Name"]).ToHashSet(StringComparer.Ordinal);
            if (names.Contains(null)) throw new InvalidDataException("A detected tablet has no configuration name.");
            if (settings?.Profiles == null) throw new InvalidDataException("The daemon returned no settings profiles.");
            if (names.Any(name => !settings.Profiles.Any(p => p.Tablet == name)))
                throw new InvalidDataException("Detected tablets and settings disagree; retry after daemon detection finishes.");
            return ProfileInspector.Identify(settings.Profiles.Select(p => (p, names.Contains(p.Tablet))));
        },
        ConfigurationDirectory = async ct => PathFrom(await connection.GetApplicationInfoAsync(ct).ConfigureAwait(false), "ConfigurationDirectory"),
        PluginDirectory = async ct => PathFrom(await connection.GetApplicationInfoAsync(ct).ConfigureAwait(false), "PluginDirectory"),
        Conflicts = async ct =>
        {
            var log = await connection.GetCurrentLogAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("The daemon returned a null log.");
            var drivers = log.Select(ConflictingDriverParser.TryParse).Where(d => d != null && !d.IsSelfMatch).ToArray();
            return new(drivers.Length > 0, drivers.Any(d => d!.Blocking));
        },
        MacOSAccess = async ct =>
        {
            var devices = await connection.GetDevicesAsync(ct).ConfigureAwait(false);
            var ids = new DeviceConfigurationProvider().TabletConfigurations
                .SelectMany(c => c.DigitizerIdentifiers.Concat(c.AuxiliaryDeviceIdentifiers ?? []))
                .Select(i => (i.VendorID, i.ProductID)).ToHashSet();
            foreach (var d in devices)
            {
                if (d["VendorID"]?.Value<int>() is not { } v || d["ProductID"]?.Value<int>() is not { } p)
                    throw new InvalidDataException("An enumerated device has no vendor/product ID.");
                if (ids.Contains((v, p))) return true;
            }
            return false;
        },
    };

    private static string PathFrom(JObject info, string key) => (string?)info[key]
        ?? throw new ProbeUnavailableException($"The daemon did not return {key}.");
}
