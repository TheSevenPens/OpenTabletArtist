using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.ViewModels;
using OtdInterop;

namespace OpenTabletArtist.Services;

/// <summary>
/// Hands one publication to every open editor (#910).
/// </summary>
///
/// <remarks>
/// <para>
/// Its own type so the forwarding can be tested. The settings and the stamp must be the pair that was
/// actually published together: an editor quotes the stamp back when the artist accepts what it is
/// showing, and the session honours an acceptance whose stamp is the one it is publishing. Hand over an
/// older snapshot under a newer stamp and the acceptance is honoured while the artist is looking at
/// something the session has already moved past — which is the substitution the stamp exists to prevent,
/// arranged by the caller instead of by the session.
/// </para>
/// <para>
/// Taking the publication as an argument is the point. The caller reads it once; nothing in here can go
/// back for a second opinion, so there is no second read for a publication to land between.
/// </para>
/// </remarks>
internal static class EditorReconciliation
{
    /// <summary>
    /// Gives each editor the profile for its own tablet out of <paramref name="published"/>, stamped with
    /// that publication's stamp.
    /// </summary>
    /// <param name="published">
    /// What the session is publishing, read once by the caller. Null when nothing has been loaded, in
    /// which case there is nothing to reconcile against and every editor is left alone.
    /// </param>
    /// <param name="editors">The open editors, by tablet name.</param>
    public static void Forward(
        PreparedSettings? published, IEnumerable<KeyValuePair<string, TabletDetailViewModel>> editors)
    {
        if (published is not { } snapshot) return;

        foreach (var (name, editor) in editors)
        {
            var profile = snapshot.Settings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase));
            editor.ReconcileExternalChange(snapshot.Settings, profile, snapshot.Stamp);
        }
    }
}
