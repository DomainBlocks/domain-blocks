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

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}, obs: {ObserverId}, count: {ObserverCount}] attached")]
    internal static partial void ObserverAttached(
        this ILogger logger,
        string feedId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}, obs: {ObserverId}, count: {ObserverCount}] detached")]
    internal static partial void ObserverDetached(
        this ILogger logger,
        string feedId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] started")]
    internal static partial void FeedStarted(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Debug, "[replication] created temporary slot {SlotName} (walsender pid {ProcessId})")]
    internal static partial void ReplicationSlotCreated(this ILogger logger, string slotName, int processId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] connected ({Session})")]
    internal static partial void FeedConnected(this ILogger logger, string feedId, string session);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] reset; notifying {ObserverCount} observer(s)")]
    internal static partial void FeedReset(this ILogger logger, string feedId, int observerCount);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] stopping")]
    internal static partial void FeedStopping(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] session ended; reconnecting")]
    internal static partial void FeedEnded(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] connection lost; reconnecting")]
    internal static partial void FeedConnectionLost(this ILogger logger, Exception exception, string feedId);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}, attempt: {Attempt}] failed to connect; retrying in {Delay}")]
    internal static partial void FeedRetrying(
        this ILogger logger,
        Exception exception,
        string feedId,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] canceled")]
    internal static partial void FeedCanceled(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}] failed")]
    internal static partial void FeedFailed(this ILogger logger, Exception exception, string feedId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] stopped")]
    internal static partial void FeedStopped(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}, obs: {ObserverId}] failed while receiving a row; detaching")]
    internal static partial void ObserverOnNextFailed(
        this ILogger logger,
        Exception exception,
        string feedId,
        string observerId);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}, obs: {ObserverId}] failed while receiving a reset; detaching")]
    internal static partial void ObserverOnResetFailed(
        this ILogger logger,
        Exception exception,
        string feedId,
        string observerId);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}, obs: {ObserverId}] failed while receiving an error")]
    internal static partial void ObserverOnErrorFailed(
        this ILogger logger,
        Exception exception,
        string feedId,
        string observerId);
}
