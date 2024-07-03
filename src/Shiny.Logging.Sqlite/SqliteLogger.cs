using System;
using Microsoft.Extensions.Logging;

namespace Shiny.Logging.Sqlite;


public class SqliteLogger(string categoryName, LogLevel configLogLevel, LoggingSqliteConnection conn) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= configLogLevel;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        conn.GetConnection().Insert(new LogStore
        {
            Category = categoryName,
            StackTrace = exception?.ToString() ?? String.Empty,
            Message = message,
            EventId = eventId.Id,
            LogLevel = logLevel,
            TimestampUtc = DateTime.UtcNow
        });
    }
}