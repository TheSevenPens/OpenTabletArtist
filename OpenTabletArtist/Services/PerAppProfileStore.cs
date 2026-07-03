using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>One app→snapshot mapping (#167). <see cref="ExePath"/> is the full image path (may be empty
/// for elevated/UWP apps); <see cref="ExeName"/> is the portable fallback match key.</summary>
public record PerAppMapping(string ExePath, string ExeName, string SnapshotName, bool Enabled = true);

/// <summary>The whole per-app config: master enable, an optional default snapshot for unmapped apps
/// (null = the user's on-disk default), and the mappings.</summary>
public record PerAppConfig(bool Enabled, string? DefaultSnapshot, IReadOnlyList<PerAppMapping> Mappings)
{
    public static PerAppConfig Empty { get; } = new(false, null, Array.Empty<PerAppMapping>());
}

/// <summary>
/// Loads/saves the per-app profile config (#167) and resolves a foreground app to a target snapshot.
/// Persistence is injected (real: one <see cref="AppSettings"/> key; tests: in-memory) so the store and
/// its <see cref="Resolve"/> precedence are unit-testable off-disk. Match precedence: exact exe path →
/// exe name → configured default → the user's on-disk default (returned as null).
/// </summary>
public sealed class PerAppProfileStore
{
    private const string Key = "PerAppProfiles";

    private readonly Func<string?> _read;
    private readonly Action<string> _write;

    public PerAppConfig Config { get; private set; }

    public PerAppProfileStore(Func<string?> read, Action<string> write)
    {
        _read = read;
        _write = write;
        Config = Deserialize(_read());
    }

    /// <summary>Backed by a single AppSettings key.</summary>
    public static PerAppProfileStore ForApp() =>
        new(() => AppSettings.Get(Key), v => AppSettings.Set(Key, v));

    /// <summary>The snapshot to apply for <paramref name="app"/>, or null to use the user's on-disk
    /// default. Exact path wins over name; unmapped apps fall back to the configured default snapshot.</summary>
    public string? Resolve(AppIdentity app)
    {
        var byPath = !string.IsNullOrEmpty(app.ExePath)
            ? Config.Mappings.FirstOrDefault(m => m.Enabled &&
                string.Equals(m.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase))
            : null;
        var match = byPath ?? Config.Mappings.FirstOrDefault(m => m.Enabled &&
            string.Equals(m.ExeName, app.ExeName, StringComparison.OrdinalIgnoreCase));
        return match?.SnapshotName ?? Config.DefaultSnapshot;
    }

    // ── Mutations (each persists) ────────────────────────────────────────────────
    public void SetEnabled(bool enabled) => Mutate(Config with { Enabled = enabled });

    public void SetDefaultSnapshot(string? snapshot) => Mutate(Config with { DefaultSnapshot = snapshot });

    /// <summary>Add or replace the mapping for an app (keyed by exe name, case-insensitive).</summary>
    public void Upsert(PerAppMapping mapping)
    {
        var list = Config.Mappings.Where(m =>
            !string.Equals(m.ExeName, mapping.ExeName, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(mapping);
        Mutate(Config with { Mappings = list });
    }

    public void Remove(string exeName) => Mutate(Config with
    {
        Mappings = Config.Mappings.Where(m =>
            !string.Equals(m.ExeName, exeName, StringComparison.OrdinalIgnoreCase)).ToList()
    });

    /// <summary>Rewrite references to a renamed snapshot across the default + all mappings (#167 data
    /// model: keep mappings valid when the user renames a Saved Settings snapshot).</summary>
    public void RenameSnapshotReferences(string oldName, string newName)
    {
        bool touched = false;
        var newDefault = Config.DefaultSnapshot;
        if (string.Equals(newDefault, oldName, StringComparison.Ordinal)) { newDefault = newName; touched = true; }

        var mappings = Config.Mappings.Select(m =>
        {
            if (!string.Equals(m.SnapshotName, oldName, StringComparison.Ordinal)) return m;
            touched = true;
            return m with { SnapshotName = newName };
        }).ToList();

        if (touched) Mutate(Config with { DefaultSnapshot = newDefault, Mappings = mappings });
    }

    private void Mutate(PerAppConfig next)
    {
        Config = next;
        _write(JsonConvert.SerializeObject(Config));
    }

    private static PerAppConfig Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return PerAppConfig.Empty;
        try { return JsonConvert.DeserializeObject<PerAppConfig>(json) ?? PerAppConfig.Empty; }
        catch { return PerAppConfig.Empty; }
    }
}
