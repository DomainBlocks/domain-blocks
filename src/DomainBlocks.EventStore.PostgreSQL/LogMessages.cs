using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Error, "[append] batch of {RequestCount} request(s) failed")]
    internal static partial void AppendBatchFailed(this ILogger logger, Exception exception, int requestCount);
}
