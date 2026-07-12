using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>The SETTINGS → DEV TOOLS tab: a single toggle that shows/hides the top-level DEVELOPER page.
/// Binds to the shared <see cref="DeveloperSettings"/> singleton — the same store the Developer page and
/// the nav item's visibility read, so checking the box makes the DEVELOPER node appear immediately.</summary>
public sealed class DevToolsViewModel
{
    public DeveloperSettings Settings => DeveloperSettings.Instance;
}
