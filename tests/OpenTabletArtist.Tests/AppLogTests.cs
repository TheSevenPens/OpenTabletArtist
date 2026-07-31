using System;
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
    public void Warn_RaisesLineWritten_WithTheFormattedLine()
    {
        string? captured = null;
        void Handler(string l) => captured = l;
        AppLog.LineWritten += Handler;
        try { AppLog.Warn("watch out", new Exception("x")); }
        finally { AppLog.LineWritten -= Handler; }

        Assert.NotNull(captured);
        Assert.Contains("[WARNING] watch out", captured);
        Assert.Contains("Exception: x", captured);
    }
}
