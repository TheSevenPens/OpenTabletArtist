using CommunityToolkit.Mvvm.ComponentModel;
using OpenTabletArtist.Services;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Advanced → Developer: testing aids. Induce health warnings so the "Needs attention" cards can be
/// reviewed and screenshotted, and reveal the normally-hidden Filters/JSON tabs on a tablet's page.
/// All state lives in the shared <see cref="DeveloperSettings"/> singleton, so Home and any open tablet
/// page react live; the view binds its controls straight to <see cref="Settings"/>.
/// </summary>
public sealed class DeveloperViewModel : ObservableObject
{
    public DeveloperSettings Settings => DeveloperSettings.Instance;
}
