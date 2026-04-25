using Serilog.Events;

namespace BcContainerLauncher.App.Logging;

/// <summary>
/// Eine einzelne Log-Zeile — projektiert aus einem Serilog-LogEvent für die UI.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Message,
    string? SourceContext);
