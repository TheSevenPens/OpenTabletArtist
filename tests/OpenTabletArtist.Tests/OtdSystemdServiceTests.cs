using System;
using System.Threading.Tasks;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

/// <summary>
/// The OTD systemd user-service control is Linux-only and must degrade gracefully everywhere else — never
/// throwing, reporting inactive, and returning a clear failure for start/stop (#601, #610). These assert the
/// off-Linux contract; on Linux they no-op (the real systemctl behaviour needs a live service to exercise).
/// </summary>
public class OtdSystemdServiceTests
{
    [Fact]
    public void IsActive_False_OffLinux()
    {
        if (OperatingSystem.IsLinux()) return;
        Assert.False(OtdSystemdService.IsActive());
    }

    [Fact]
    public async Task StartAsync_ReportsUnavailable_OffLinux()
    {
        if (OperatingSystem.IsLinux()) return;
        var (ok, error) = await OtdSystemdService.StartAsync();
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task StopAsync_ReportsUnavailable_OffLinux()
    {
        if (OperatingSystem.IsLinux()) return;
        var (ok, error) = await OtdSystemdService.StopAsync();
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
