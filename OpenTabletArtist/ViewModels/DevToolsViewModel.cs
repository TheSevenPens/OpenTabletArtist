using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The SETTINGS → DEV TOOLS tab: a single toggle that shows/hides the DEVELOPER tab in Settings.
/// Binds to the shared <see cref="DeveloperSettings"/> singleton — the same store the Developer tab and its
/// visibility read, so checking the box makes the DEVELOPER tab appear immediately (#572).</summary>
public sealed class DevToolsViewModel
{
    public DeveloperSettings Settings => DeveloperSettings.Instance;
}
