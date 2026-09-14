using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace DomainBlocks.Testing;

/// <summary>
/// Writes log messages to the NUnit progress output. Messages below <paramref name="minimumLevel"/> are dropped here
/// regardless of the logger factory's own minimum level, so both must be lowered to see Trace output.
/// </summary>
public sealed class NUnitLoggerProvider(LogLevel minimumLevel = LogLevel.Debug) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new NUnitLogger(categoryName, minimumLevel);

    public void Dispose()
    {
    }

    private sealed class NUnitLogger(string categoryName, LogLevel minimumLevel) : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);

            TestContext.Progress.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {categoryName}: {message}");

            if (exception != null)
                TestContext.Progress.WriteLine(exception);
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}