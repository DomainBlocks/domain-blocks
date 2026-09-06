using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace DomainBlocks.Testing;

public sealed class NUnitLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new NUnitLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class NUnitLogger(string categoryName) : ILogger
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

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

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