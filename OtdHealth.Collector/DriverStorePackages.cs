using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OtdHealth.Collector;

/// <summary>A third-party driver package staged in the Windows driver store (the <c>oemNN.inf</c> entries
/// <c>pnputil -e</c> lists).</summary>
public sealed record DriverStorePackage(string PublishedName, string OriginalName, string Provider)
{
    /// <summary>One line for people: <c>oem92.inf · wacom_wusd.inf · Wacom Technology</c>.</summary>
    public string Summary => $"{PublishedName} · {OriginalName} · {Provider}";
}

/// <summary>Which driver-store packages a flagged manufacturer driver stands for.</summary>
/// <remarks>
/// OpenTabletDriver's own warning says only that a name appeared somewhere in <c>pnputil -e</c>; it never
/// says which package. This answers that for the driver the user is looking at, from the fields that can be
/// read without elevation and without spawning <c>pnputil</c> (whose labels are translated).
/// </remarks>
public static class DriverStorePackages
{
    /// <summary>OTD's Wacom heuristic (<c>WacomDriverInfoProvider.Heuristics</c>): the word, anywhere, in any
    /// case. Applied here to the provider and INF name, the fields we can read.</summary>
    public static readonly Regex Wacom = new("Wacom", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Published = new(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The third-party packages whose provider or INF name matches <paramref name="heuristic"/>.
    /// Windows' own packages are left out: <c>pnputil -e</c>, which OTD reads, never lists them.</summary>
    public static IReadOnlyList<DriverStorePackage> Matching(IEnumerable<DriverStorePackage> packages, Regex heuristic) =>
        packages
            .Where(p => Published.IsMatch(p.PublishedName)
                        && (heuristic.IsMatch(p.Provider) || heuristic.IsMatch(p.OriginalName)))
            .OrderBy(p => p.PublishedName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Every package in the driver store. Windows only; throws <see cref="ProbeUnavailableException"/>
    /// elsewhere or when the registry cannot be read.</summary>
    public static IReadOnlyList<DriverStorePackage> ReadAll()
    {
        if (!OperatingSystem.IsWindows())
            throw new ProbeUnavailableException("The driver store is a Windows feature.", true);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\DriverDatabase\DriverPackages")
                ?? throw new ProbeUnavailableException("The driver package registry key is missing.");
            var packages = new List<DriverStorePackage>();
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                // A package key is named for its INF; its default value is the published name.
                if (key?.GetValue("") is not string published) continue;
                packages.Add(new(published, key.GetValue("InfName") as string ?? name,
                    key.GetValue("Provider") as string ?? ""));
            }
            return packages;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new ProbeUnavailableException("The driver package registry key could not be read: " + ex.Message);
        }
    }
}
