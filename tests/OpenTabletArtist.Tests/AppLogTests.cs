using System;
using System.IO;
using OpenTabletArtist.Services;
using Xunit;

namespace OpenTabletArtist.Tests;

public class AppLogTests
{
    [Fact]
    public void Format_IsTimestampLevelMessage()
    {
        var ts = new DateTime(2026, 7, 31, 15, 41, 58).AddMilliseconds(123);
        Assert.Equal("2026-07-31 15:41:58.123 [WARNING] hello",
            AppLog.Format(ts, AppLogLevel.Warning, "hello", null));
    }

    [Fact]
    public void Format_AppendsExceptionTypeAndMessage()
    {
        var line = AppLog.Format(new DateTime(2026, 1, 1), AppLogLevel.Error, "boom",
            new InvalidOperationException("nope"));
        Assert.Contains("[ERROR] boom", line);
        Assert.Contains("— InvalidOperationException: nope", line);
    }

    [Fact]
    public void Format_WrappedException_UsesTheBaseCauseMessage()
    {
        // I/O and RPC failures are usually wrapped; keep the outer type but surface the inner detail.
        var wrapped = new InvalidOperationException("outer", new IOException("the real cause"));
        var line = AppLog.Format(new DateTime(2026, 1, 1), AppLogLevel.Warning, "op failed", wrapped);
        Assert.Contains("— InvalidOperationException: the real cause", line);
        Assert.DoesNotContain("outer", line);
    }
}
