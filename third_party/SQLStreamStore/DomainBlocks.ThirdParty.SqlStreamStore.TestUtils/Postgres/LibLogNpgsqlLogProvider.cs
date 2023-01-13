using DomainBlocks.Logging;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.ThirdParty.SqlStreamStore.TestUtils.Postgres
{
    using System;
    using Npgsql.Logging;

    public class LibLogNpgsqlLogProvider : INpgsqlLoggingProvider
    {
        public NpgsqlLogger CreateLogger(string name)
        {
            var logger = Logger.Create(name);
            return new LibLogNpgsqlLogger(logger, name);
        }

        private class LibLogNpgsqlLogger : NpgsqlLogger
        {
            private readonly ILogger _logger;
            private readonly string _name;

            public LibLogNpgsqlLogger(ILogger logger, string name)
            {
                _logger = logger;
                _name = name;
            }

            public override bool IsEnabled(NpgsqlLogLevel level) => true;

            public override void Log(NpgsqlLogLevel level, int connectorId, string msg, Exception exception = null)
                // TODO (DS): Fix this
                => _logger.LogInformation($@"[{level:G}] [{_name}] (Connector Id: {connectorId}); {msg}; {FormatOptionalException(exception)}");

            private static string FormatOptionalException(Exception exception)
                => exception == null ? string.Empty : $"(Exception: {exception})";
        }
    }
}