using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenTabletArtist.ViewModels;

/// <summary>
/// Landing page for the Tablets group (shown when its header is clicked / no tablet is selected).
/// When tablets exist it prompts to pick one from the sidebar; otherwise it states there are none.
/// <see cref="HasTablets"/> is kept in sync by the shell as tablets come and go.
/// </summary>
public partial class TabletsOverviewViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasTablets;
}
