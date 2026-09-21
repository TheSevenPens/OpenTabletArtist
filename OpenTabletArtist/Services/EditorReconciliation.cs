using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletArtist.ViewModels;
using OtdInterop;

namespace OpenTabletArtist.Services;
/// <summary>Refresh cached editors from the shared workspace, preserving pending local input.</summary>
internal static class EditorReconciliation
{
    public static void Forward(
        OpenTabletDriver.Desktop.Settings? published, IEnumerable<KeyValuePair<string, TabletDetailViewModel>> editors)
    {
        if (published is not { } snapshot) return;

        foreach (var (name, editor) in editors)
        {
            var profile = snapshot.Profiles.FirstOrDefault(p =>
                string.Equals(p.Tablet, name, StringComparison.OrdinalIgnoreCase));
            editor.ReconcileExternalChange(snapshot, profile);
        }
    }
}
