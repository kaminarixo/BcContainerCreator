using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace BcContainerCreator.App.Logging;

/// <summary>
/// Serilog-Sink, der Log-Events im Speicher hält und per Event publiziert.
/// Singleton — zentral via DI registriert.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private const int MaxEntries = 5_000;

    public event EventHandler<LogEntry>? LogEmitted;

    public IReadOnlyCollection<LogEntry> Snapshot() => _entries.ToArray();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var sc)
            ? sc.ToString().Trim('"')
            : null;

        var entry = new LogEntry(
            Timestamp: logEvent.Timestamp,
            Level: logEvent.Level,
            Message: logEvent.RenderMessage(),
            SourceContext: sourceContext);

        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
        {
            // Älteste Einträge verwerfen.
        }

        try
        {
            LogEmitted?.Invoke(this, entry);
        }
        catch
        {
            // Subscriber-Fehler dürfen den Logger nicht stoppen.
        }
    }
}
