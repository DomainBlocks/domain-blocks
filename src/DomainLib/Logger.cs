using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DomainLib
{
    public static class Logger
    {
        private static ILoggerFactory _loggerFactory;
        private static int _userLoggerFactorySet;

        static Logger()
        {
            _loggerFactory = new NullLoggerFactory();
        }

        /// <summary>
        /// Set the library logger factory to an instance of <see cref="Microsoft.Extensions.Logging.ILoggerFactory" />.
        /// This should be done once on start-up of your application and not changed again./>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if user tries to change the <see cref="Microsoft.Extensions.Logging.ILoggerFactory" /> once set.</exception>
        public static void SetLoggerFactory(ILoggerFactory loggerFactory)
        {
            if (Interlocked.CompareExchange(ref _userLoggerFactorySet, 1, 0) == 0)
            {
                _loggerFactory = loggerFactory;
            }
            else
            {
                throw new InvalidOperationException("LoggerFactory has already been set. Cannot set it again.");
            }
        }

        public static ILogger<T> CreateFor<T>()
        {
            return _loggerFactory.CreateLogger<T>();
        }
    }
}