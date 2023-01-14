using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DomainBlocks.Logging;

public static class Log
{
    private static ILoggerFactory _loggerFactory;
    private static int _userLoggerFactorySet;

    static Log()
    {
        _loggerFactory = new NullLoggerFactory();
    }

    /// <summary>
    /// Set the library logger factory to an instance of <see cref="Microsoft.Extensions.Logging.ILoggerFactory" />.
    /// This should be done once on start-up of your application and not changed again./>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if user tries to change the <see cref="Microsoft.Extensions.Logging.ILoggerFactory" /> once set.
    /// </exception>
    public static void SetLoggerFactory(
        ILoggerFactory loggerFactory,
        bool throwOnSettingLoggerFactoryIfAlreadySet = true)
    {
        if (Interlocked.CompareExchange(ref _userLoggerFactorySet, 1, 0) == 0)
        {
            _loggerFactory = loggerFactory;
        }
        else
        {
            if (throwOnSettingLoggerFactoryIfAlreadySet)
                throw new InvalidOperationException("LoggerFactory has already been set. Cannot set it again.");
        }
    }

    public static ILogger<T> Create<T>()
    {
        return _loggerFactory.CreateLogger<T>();
    }

    public static ILogger Create(string categoryName)
    {
        return _loggerFactory.CreateLogger(categoryName);
    }
}