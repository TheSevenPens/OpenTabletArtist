using System;
using OtdInterop;

namespace OtdInterop.Tests;

/// <summary>
/// Swallows what the library logs, for tests that are not about logging.
///
/// Deliberately not a recorder. A test that cares what was logged should say so by using its own
/// recording implementation, so that "nothing was logged" is never an assertion made by accident.
/// </summary>
internal sealed class NullOtdLog : IOtdLog
{
    public static readonly NullOtdLog Instance = new();

    private NullOtdLog() { }

    public void Warn(string message, Exception? error = null) { }

    public void Info(string message) { }

    public void Debug(string message, Exception? error = null) { }
}
