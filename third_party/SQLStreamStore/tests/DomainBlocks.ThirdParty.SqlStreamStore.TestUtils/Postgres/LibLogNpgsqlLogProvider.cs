using DomainBlocks.Logging;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.ThirdParty.SqlStreamStore.TestUtils.Postgres
{
    using System;

    public class LibLogNpgsqlLogProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string name)
        {
            var logger = Log.Create(name);
            return new LibLogNpgsqlLogger(logger, name);
        }

        private class LibLogNpgsqlLogger : ILogger
        {
            private readonly ILogger _logger;
            private readonly string _name;

            public LibLogNpgsqlLogger(ILogger logger, string name)
            {
                _logger = logger;
                _name = name;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                // ReSharper disable TemplateIsNotCompileTimeConstantProblem
                switch (logLevel)
                {
                    case LogLevel.Trace:
                        _logger.LogTrace(eventId, exception, formatter(state, exception));
                        break;
                    case LogLevel.Debug:
                        _logger.LogDebug(eventId, exception, formatter(state, exception));
                        break;
                    case LogLevel.Information:
                        _logger.LogInformation(eventId, exception, formatter(state,exception));
                        break;
                    case LogLevel.Warning:
                        _logger.LogWarning(eventId, exception, formatter(state,exception));

                        break;
                    case LogLevel.Error:
                        _logger.LogError(eventId, exception, formatter(state,exception));

                        break;
                    case LogLevel.Critical:
                        _logger.LogCritical(eventId, exception, formatter(state,exception));
                        break;
                    case LogLevel.None:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
                }
            }

            public bool IsEnabled(LogLevel level) => true;
            public IDisposable BeginScope<TState>(TState state)
            {
                return _logger.BeginScope(state);
            }
        }

        public void Dispose()
        {
        }
    }
}