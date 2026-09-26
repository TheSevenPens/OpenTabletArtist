using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Configurations;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace OtdHealth.Collector;

public static class FileProbes
{
    // File/Directory.Exists swallow access errors. Only actual absence counts as absent evidence.
    public static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static PluginMetadata? ReadWindowsInk(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ProbeUnavailableException("The daemon plugin directory is unknown.");
        string path = Path.Combine(directory, "Windows Ink", "metadata.json");
        if (!Exists(path)) return null;
        return JsonConvert.DeserializeObject<PluginMetadata>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Windows Ink metadata is null.");
    }

    public static InkObservation WindowsInk(string directory, string expectedVersion)
    {
        var metadata = ReadWindowsInk(directory);
        if (metadata == null) return new(false, false);
        if (!Version.TryParse(expectedVersion, out var version))
            throw new ProbeUnavailableException("An expected OTD version is required to assess plugin compatibility.");
        return new(true, !metadata.IsSupportedBy(version));
    }

    public static IReadOnlySet<string> ConfigurationOverrides(string directory, bool tolerateInvalidFiles = false)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ProbeUnavailableException("The daemon configuration directory is unknown.");
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Exists(directory)) return result;
        var names = new DeviceConfigurationProvider().TabletConfigurations.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) throw new InvalidDataException("The built-in configuration catalog is empty.");
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var name = (string?)JObject.Parse(File.ReadAllText(path))["Name"];
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Configuration has no Name: {path}");
                if (names.Contains(name)) result.Add(name);
            }
            catch (Exception ex) when (tolerateInvalidFiles && ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            { /* The legacy browse page can retain known overrides. Health collection uses strict mode. */ }
        }
        return result;
    }

    public static string DaemonVersion(string executablePath, string siblingAssemblyName = "OpenTabletDriver.Daemon.dll")
    {
        if (string.IsNullOrWhiteSpace(executablePath)) throw new ProbeUnavailableException("The connected daemon executable is unknown.");
        static string Read(string path)
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return (info.ProductVersion ?? info.FileVersion ?? "").Trim().Split('+')[0];
        }
        var version = Read(executablePath);
        if (version.Length > 0) return version;
        var sibling = Path.Combine(Path.GetDirectoryName(executablePath)!, siblingAssemblyName);
        if (Exists(sibling)) version = Read(sibling);
        if (version.Length == 0) throw new ProbeUnavailableException("No daemon version resource is available.");
        return version;
    }
}
