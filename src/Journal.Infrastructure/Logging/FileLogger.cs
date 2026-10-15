using Microsoft.Extensions.Logging;

namespace Journal.Infrastructure.Logging;

/// <summary>
/// Minimal rolling file logger (one file per day, 7 days kept). Callers must never log journal content,
/// tags, captions, file names or key material (NFR-SEC-06); messages are fixed strings plus counts and error types.
/// </summary>
public sealed class FileLoggerProvider(string folder, LogLevel minLevel = LogLevel.Information) : ILoggerProvider
{
    private readonly Lock _gate = new();

    public string Folder { get; } = folder;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, string message, Exception? e)
    {
        if (level < minLevel) return;
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}";
        if (e is not null) line += $" ({e.GetType().Name}: {e.HResult:X8})";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"journal-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                foreach (var old in Directory.GetFiles(Folder, "journal-*.log").OrderDescending().Skip(7))
                    File.Delete(old);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Write(logLevel, category, formatter(state, null), exception);
    }
}
