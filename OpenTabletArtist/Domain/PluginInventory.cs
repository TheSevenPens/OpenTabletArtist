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
}
