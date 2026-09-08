using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static partial class LogMessages
{
    [LoggerMessage(
        LogLevel.Trace,
        "[append] committed batch of {BatchSize}/{MaxBatchSize} request(s) in {ElapsedMs:F2} ms " +
        "(~{QueuedCount} queued)")]
    internal static partial void AppendBatchProcessed(
        this ILogger logger,
        int batchSize,
        int maxBatchSize,
        int queuedCount,
        double elapsedMs);

    [LoggerMessage(LogLevel.Warning, "[append] transient error committing batch of {RequestCount}; retrying")]
    internal static partial void AppendBatchRetrying(this ILogger logger, Exception exception, int requestCount);

    [LoggerMessage(LogLevel.Error, "[append] batch of {RequestCount} request(s) failed")]
    internal static partial void AppendBatchFailed(this ILogger logger, Exception exception, int requestCount);

    [LoggerMessage(LogLevel.Debug, "[append] loop stopped")]
    internal static partial void AppendLoopStopped(this ILogger logger);

    [LoggerMessage(LogLevel.Critical, "[append] loop failed; further appends will be rejected")]
    internal static partial void AppendLoopFailed(this ILogger logger, Exception exception);
}
