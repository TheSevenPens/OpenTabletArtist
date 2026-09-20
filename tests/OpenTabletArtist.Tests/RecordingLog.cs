using System;
using System.Collections.Generic;
using OtdInterop;

namespace OpenTabletArtist.Tests;

/// <summary>Keeps what the library logged, for tests whose subject is that something was reported.</summary>
internal sealed class RecordingLog : IOtdLog
{
    public List<string> Warnings { get; } = new();

    public void Warn(string message, Exception? error = null) =>
        Warnings.Add(error == null ? message : $"{message} :: {error.Message}");

    public void Info(string message) { }

    public void Debug(string message, Exception? error = null) { }
}
