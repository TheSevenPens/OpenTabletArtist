using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTabletArtist.Services;

/// <summary>
/// The first time OpenTabletArtist sees a given tablet, auto-maps it to the primary display
/// (aspect-locked) when it's otherwise unmapped on a multi-monitor setup — so a freshly-connected
/// tablet doesn't span every monitor out of the box (#362). A persisted "seen" set means it only ever
/// acts once per tablet: a tablet the user later configures (or intentionally leaves spanning all
/// monitors) is never re-mapped. Only touches a connected, app-owned daemon.
/// </summary>
public sealed class TabletAutoMapper : IDisposable
{
    private const string SeenKey = "SeenTablets";
    private static readonly char[] Separator = { '\n' };

    private readonly AppSession _session;
    private readonly SetupActions _setup;
    private readonly HashSet<string> _seen;

    public TabletAutoMapper(AppSession session)
    {
        _session = session;
        _setup = new SetupActions(session, session);
        _seen = LoadSeen();
        _session.DataLoaded += OnDataLoaded;
    }

    private async void OnDataLoaded()
    {
        // Don't rewrite a daemon this app didn't start — its settings/profiles may not match ours.
        if (!_session.IsConnected || _session.IsForeignDaemon) return;

        var firstTime = _session.Profiles
            .Where(p => p.IsDetected && !_seen.Contains(p.Tablet))
            .Select(p => p.Tablet)
            .ToList();
        if (firstTime.Count == 0) return;

        // Record (and persist) as seen BEFORE mapping, so the reload that mapping triggers re-enters
        // here with nothing left to do instead of mapping again.
        foreach (var t in firstTime) _seen.Add(t);
        SaveSeen();

        var unmapped = new HashSet<string>(_setup.DetectedUnmappedTablets(), StringComparer.OrdinalIgnoreCase);
        foreach (var t in firstTime)
        {
            if (!unmapped.Contains(t)) continue;
            try
            {
                if (!await _setup.MapTabletToPrimaryAsync(t))
                    RollbackSeen(t);
            }
            catch
            {
                RollbackSeen(t); // mapping failed — allow retry on a later session (#404)
            }
        }
    }

    private void RollbackSeen(string tablet)
    {
        if (!_seen.Remove(tablet)) return;
        SaveSeen();
    }

    private static HashSet<string> LoadSeen()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = AppSettings.Get(SeenKey);
        if (!string.IsNullOrEmpty(raw))
            foreach (var name in raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
                set.Add(name);
        return set;
    }

    private void SaveSeen() => AppSettings.Set(SeenKey, string.Join('\n', _seen));

    public void Dispose() => _session.DataLoaded -= OnDataLoaded;
}
