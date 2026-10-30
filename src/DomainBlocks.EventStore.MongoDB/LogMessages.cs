using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB;

internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Trace, "[change stream] processed batch of {Count}")]
    internal static partial void ChangeStreamBatchProcessed(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "[change stream] history lost; starting a new change stream")]
    internal static partial void ChangeStreamHistoryLost(this ILogger logger, Exception exception);
}