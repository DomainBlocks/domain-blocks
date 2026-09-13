using Microsoft.Extensions.Logging;

namespace DomainBlocks.Testing;

/// <summary>
/// Creates the logger factory used by every test environment: NUnit progress output at Debug, or at the level named
/// by the DBX_TEST_LOG_LEVEL environment variable (e.g. Trace to see per-batch append logging).
/// </summary>
public static class TestLoggerFactory
{
    public const string LogLevelEnvironmentVariable = "DBX_TEST_LOG_LEVEL";

    public static ILoggerFactory Create(LogLevel defaultMinimumLevel = LogLevel.Debug)
    {
        var minimumLevel = Enum.TryParse<LogLevel>(
            Environment.GetEnvironmentVariable(LogLevelEnvironmentVariable), true, out var configured)
            ? configured
            : defaultMinimumLevel;

        return LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider(minimumLevel))
            .SetMinimumLevel(minimumLevel));
    }
}