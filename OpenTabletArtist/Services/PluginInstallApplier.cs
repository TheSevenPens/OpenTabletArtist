using System.Threading.Tasks;

namespace OpenTabletArtist.Services;

/// <summary>Applies a plugin install/update outcome to the daemon (#385).</summary>
public static class PluginInstallApplier
{
    public static async Task ApplyAsync(AppSession session, PluginInstallOutcome outcome)
    {
        switch (outcome)
        {
            case PluginInstallOutcome.Installed:
                await session.Daemon.LoadPluginsAsync();
                break;
            case PluginInstallOutcome.Updated:
                await session.RestartDaemonCommand.ExecuteAsync(null);
                break;
        }
    }
}
