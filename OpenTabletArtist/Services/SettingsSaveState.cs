namespace OpenTabletArtist.Services;

/// <summary>Display state for explicit persistence, independent of live application.</summary>
public enum SettingsSaveState
{
    None, Applying, Unsaved, Saving, Saved, Failed, ApplyFailed, Disconnected, ChangedElsewhere, CouldNotCheck,
}
