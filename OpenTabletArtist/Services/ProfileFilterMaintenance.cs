using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Desktop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Keeps a tablet profile's filter list free of stale stores that this app left behind across
/// renames. Our plugin filters are stored in the profile by their full type name (path); when the
/// app/plugin was renamed (e.g. <c>OtdArtist.Dynamics.DynamicsFilter</c> →
/// <c>OpenTabletArtist.Dynamics.DynamicsFilter</c>), the new code stopped recognizing the old-name
/// store and wrote a fresh one beside it. The daemon has no plugin for the old name, so the orphan
/// is inert — but it lingers in the profile and shows up twice in the Filters/JSON views.
///
/// This removes those orphans (and any exact-duplicate of a current store), keying off our own
/// historical namespaces + class names so third-party filters are never touched. It runs on load
/// (to clean the display) and before every save (a forward guard so a future rename self-heals).
/// </summary>
public static class ProfileFilterMaintenance
{
    /// <summary>The filter type names this app currently writes — the canonical, keep-these paths.</summary>
    private static readonly HashSet<string> CurrentPaths = new(StringComparer.Ordinal)
    {
        PressureCurveProfile.FilterTypeName, // OpenTabletArtist.Dynamics.DynamicsFilter
        HoverProfile.FilterTypeName,         // OpenTabletArtist.Dynamics.HoverFilter
        CalibrationProfile.FilterTypeName,   // OpenTabletArtist.Dynamics.CalibrationFilter
    };

    /// <summary>Class names (last path segment) of filters this app has ever shipped, including
    /// legacy ones (the pre-Dynamics <c>PressureCurveFilter</c>). Used with <see cref="OurNamespaceRoots"/>
    /// to recognize a store as ours so we never delete an unrelated third-party filter.</summary>
    private static readonly HashSet<string> OurClassNames = new(StringComparer.Ordinal)
    {
        "DynamicsFilter", "HoverFilter", "CalibrationFilter", "PressureCurveFilter",
    };

    /// <summary>Namespace prefixes this app's plugin has used across renames. Anything we write has
    /// always lived under one of these — so "ours but not current" == a rename orphan to remove.
    /// Matched case-insensitively (the earliest name was written as both "OtdWindowsHelper" and
    /// "OTDWindowsHelper"). Add a new entry here whenever the app is renamed again.</summary>
    private static readonly string[] OurNamespaceRoots =
    {
        "OpenTabletArtist.",  // current
        "OtdArtist.",         // pre-#200 rename
        "OtdWindowsHelper.",  // earliest name
    };

    /// <summary>Where a filter store's type path came from, for display/diagnostics.</summary>
    public enum FilterOrigin
    {
        /// <summary>A type this app currently writes — expected and live.</summary>
        Current,
        /// <summary>One of ours under an old namespace (a rename orphan): inert, should be cleaned.</summary>
        Legacy,
        /// <summary>Anything else — a third-party / driver-built-in filter we don't manage.</summary>
        Unknown,
    }

    /// <summary>Classifies a filter's type path so the UI can flag a stale leftover ("Legacy")
    /// distinctly from a current OTA filter or an unrelated third-party one.</summary>
    public static FilterOrigin Classify(string? path)
    {
        if (string.IsNullOrEmpty(path)) return FilterOrigin.Unknown;
        if (CurrentPaths.Contains(path!)) return FilterOrigin.Current;
        if (IsOurStaleFilter(path!)) return FilterOrigin.Legacy;
        return FilterOrigin.Unknown;
    }

    /// <summary>Strips rename-orphaned and duplicate OTA filter stores from every profile. Mutates
    /// <paramref name="settings"/> in place. Returns true if anything was removed.</summary>
    public static bool CleanLegacyFilters(Settings? settings)
    {
        if (settings?.Profiles == null) return false;

        bool changed = false;
        foreach (var profile in settings.Profiles)
        {
            var filters = profile?.Filters;
            if (filters == null || filters.Count == 0) continue;

            var keptCurrent = new HashSet<string>(StringComparer.Ordinal);
            // Walk back-to-front so removals don't shift the indices we haven't visited yet.
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                var path = filters[i]?.Path;
                if (string.IsNullOrEmpty(path)) continue;

                if (CurrentPaths.Contains(path))
                {
                    // Keep the first current store of each kind; drop any exact duplicate.
                    if (!keptCurrent.Add(path))
                    {
                        filters.RemoveAt(i);
                        changed = true;
                    }
                    continue;
                }

                if (IsOurStaleFilter(path))
                {
                    filters.RemoveAt(i);
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>True when a path is one of our filters under an old namespace (so it's an orphan from
    /// a rename), and not a current path. Restricted to our own namespaces + class names so a
    /// coincidentally-named third-party filter is never matched.</summary>
    private static bool IsOurStaleFilter(string path)
    {
        var className = path.Split('.').LastOrDefault();
        if (className == null || !OurClassNames.Contains(className)) return false;
        return OurNamespaceRoots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }
}
