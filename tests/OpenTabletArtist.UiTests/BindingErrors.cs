using System.Text;
using Avalonia.Logging;

namespace OpenTabletArtist.UiTests;

/// <summary>
/// Captures Avalonia's binding diagnostics while a view is built and laid out (#741).
///
/// The app sets <c>AvaloniaUseCompiledBindingsByDefault=false</c>, so a binding path that no longer
/// resolves — a renamed property, a retyped DataContext, a view model split in two — is not a compile
/// error. It is a warning written to the log sink at runtime and an empty spot on screen. That is the
/// failure mode a refactor produces, and the one CI could not see.
/// </summary>
public sealed class BindingErrors : ILogSink, IDisposable
{
    private readonly ILogSink? _previous;
    private readonly List<string> _messages = [];
    private readonly object _lock = new();

    private BindingErrors()
    {
        _previous = Logger.Sink;
        Logger.Sink = this;
    }

    /// <summary>Starts capturing. Dispose (or <c>using</c>) restores the previous sink.</summary>
    public static BindingErrors Capture() => new();

    /// <summary>Everything Avalonia reported against <see cref="LogArea.Binding"/>, worst first.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_lock) return _messages.ToArray(); }
    }

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area == LogArea.Binding;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Record(level, area, source, messageTemplate, []);

    public void Log<T0>(LogEventLevel level, string area, object? source, string messageTemplate,
        T0 pv0) => Record(level, area, source, messageTemplate, [pv0]);

    public void Log<T0, T1>(LogEventLevel level, string area, object? source, string messageTemplate,
        T0 pv0, T1 pv1) => Record(level, area, source, messageTemplate, [pv0, pv1]);

    public void Log<T0, T1, T2>(LogEventLevel level, string area, object? source, string messageTemplate,
        T0 pv0, T1 pv1, T2 pv2) => Record(level, area, source, messageTemplate, [pv0, pv1, pv2]);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues) => Record(level, area, source, messageTemplate, propertyValues);

    private void Record(LogEventLevel level, string area, object? source, string template,
        object?[] values)
    {
        if (!IsEnabled(level, area)) return;

        var text = new StringBuilder(template);
        // Avalonia's templates use positional {} holes; substitute in order so the message names the
        // actual property and type rather than a row of empty braces.
        foreach (var value in values)
        {
            var hole = text.ToString().IndexOf('{');
            if (hole < 0) break;
            var close = text.ToString().IndexOf('}', hole);
            if (close < 0) break;
            text.Remove(hole, close - hole + 1).Insert(hole, value?.ToString() ?? "null");
        }

        lock (_lock)
            _messages.Add($"{level} [{source?.GetType().Name ?? "?"}] {text}");
    }

    /// <summary>Fails with every binding problem listed, not just the first — one broken DataContext
    /// usually breaks several bindings, and fixing them one test run at a time is miserable.</summary>
    public void AssertNone(string what)
    {
        var messages = Messages;
        if (messages.Count == 0) return;

        Assert.Fail($"{what} produced {messages.Count} binding error(s):{Environment.NewLine}  "
                    + string.Join(Environment.NewLine + "  ", messages));
    }

    public void Dispose() => Logger.Sink = _previous;
}
