using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;

namespace OpenTabletArtist.Domain;

/// <summary>
/// A stable content fingerprint of an OTD <see cref="Profile"/>, used to tell when the daemon's
/// settings were changed by something other than us — notably the OpenTabletDriver UX editing the
/// same app-owned daemon. The daemon pushes no "settings changed" event on a successful apply
/// (<c>SetSettings</c> only fires <c>Resynchronize</c> on its failure path), so an open editor can't
/// be notified; instead it re-pulls and compares fingerprints to decide whether to reconcile.
///
/// This is a value comparison, not a canonical serialization: two profiles with identical field
/// values produce the same fingerprint, and any difference (mapping, bindings, dynamics, output mode,
/// filters, …) changes it. Returns "" when the profile can't be serialized — callers treat "" as
/// "unknown" and skip detection rather than risk a false positive.
/// </summary>
public static class ProfileFingerprint
{
    // Formatting.None for a deterministic string; ignore any reference loops the ViewModel base
    // (INotifyPropertyChanged) might introduce so serialization can't throw on well-formed profiles.
    private static readonly JsonSerializerSettings Options = new()
    {
        Formatting = Formatting.None,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    /// <summary>Fingerprint of <paramref name="profile"/>, or "" if it's null or can't be serialized.</summary>
    public static string Compute(Profile? profile)
    {
        if (profile == null) return "";
        try
        {
            var json = JsonConvert.SerializeObject(profile, Options);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash);
        }
        catch
        {
            return "";
        }
    }
}
