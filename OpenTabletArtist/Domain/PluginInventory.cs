using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTabletArtist.Domain;

/// <summary>One installed OTD plugin as shown on the read-only Plugins page.</summary>
public sealed record PluginInfo(string Name, string Version, bool IsActive)
{
    /// <summary>Active = referenced by an enabled output mode or filter in some profile; otherwise
    /// it's installed/loaded but not in use.</summary>
    public string Status => IsActive ? "Active" : "Installed";
}

public static class PluginInventory
{
    /// <summary>True when a plugin type's full name (an OTD <c>PluginSettingStore.Path</c>) belongs to
    /// the assembly with the given base name — i.e. the assembly name is its namespace root. Used to
    /// decide whether an installed plugin DLL is actually referenced by the settings.</summary>
    public static bool PathBelongsToAssembly(string assemblyBaseName, string? typePath)
        => !string.IsNullOrEmpty(assemblyBaseName) && !string.IsNullOrEmpty(typePath)
           && (typePath == assemblyBaseName || typePath!.StartsWith(assemblyBaseName + "."));

    /// <summary>Which DLL in a plugin folder is the plugin itself, for reading a version from.
    ///
    /// The list used to take the first DLL it found, which is only ever right for a single-file plugin.
    /// Windows Ink ships four, and the first alphabetically is Newtonsoft.Json — so the page reported the
    /// plugin as version 13.0.0.0, the JSON library's. Match the folder name instead ("Windows Ink" ->
    /// WindowsInk.dll), ignoring case and anything that is not a letter or digit, since a folder may carry
    /// spaces or punctuation an assembly name cannot. Falls back to the first DLL when nothing matches,
    /// which is no worse than the old behaviour and still right for a single-file plugin.</summary>
    public static string? PluginDll(string folderName, IReadOnlyList<string> dllPaths)
    {
        if (dllPaths.Count == 0) return null;

        static string Key(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var want = Key(folderName);
        return dllPaths.FirstOrDefault(p => Key(Path.GetFileNameWithoutExtension(p)) == want) ?? dllPaths[0];
    }
}
