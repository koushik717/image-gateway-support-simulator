namespace ImageGatewaySupportSimulator.Diagnostics;

public enum LogLevel
{
    Info,
    Success,
    Error
}

public class LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string Message { get; init; }
    public string? CorrelationId { get; init; }

    public override string ToString()
    {
        var time = Timestamp.ToString("HH:mm:ss");
        var correlation = CorrelationId is null ? "" : $"[{CorrelationId}] ";
        return $"{time} {correlation}{Level.ToString().ToUpperInvariant()} {Message}";
    }
}

// Raises an event instead of writing to the UI directly, so MainForm
// decides how (and whether) to display each entry.
public class DiagnosticLogger
{
    public event EventHandler<LogEntry>? EntryLogged;

    public void Info(string message, string? correlationId = null) =>
        Log(LogLevel.Info, message, correlationId);

    public void Success(string message, string? correlationId = null) =>
        Log(LogLevel.Success, message, correlationId);

    public void Error(string message, string? correlationId = null) =>
        Log(LogLevel.Error, message, correlationId);

    private void Log(LogLevel level, string message, string? correlationId)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            CorrelationId = correlationId
        };

        EntryLogged?.Invoke(this, entry);
    }
}
