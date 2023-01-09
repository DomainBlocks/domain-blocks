using Microsoft.Extensions.Logging;
using SqlStreamStore.Logging.LogProviders;
using SqlStreamLogLevel = SqlStreamStore.Logging.LogLevel;
using SqlStreamStoreLogger = SqlStreamStore.Logging.Logger;

namespace DomainBlocks.SqlStreamStore;

public class SqlStreamStoreLogProvider : LogProviderBase
{
    private readonly ILoggerFactory _loggerFactory;

    public SqlStreamStoreLogProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public override SqlStreamStoreLogger GetLogger(string name)
    {
        var logger = _loggerFactory.CreateLogger(name);

        return (level, func, exception, parameters) =>
        {
            var message = func?.Invoke();

            var logLevel = level switch
            {
                SqlStreamLogLevel.Trace => LogLevel.Trace,
                SqlStreamLogLevel.Debug => LogLevel.Debug,
                SqlStreamLogLevel.Info => LogLevel.Information,
                SqlStreamLogLevel.Warn => LogLevel.Warning,
                SqlStreamLogLevel.Error => LogLevel.Error,
                SqlStreamLogLevel.Fatal => LogLevel.Critical,
                _ => LogLevel.Information
            };

            logger.Log(logLevel, exception, message, parameters);

            return true;
        };
    }
}