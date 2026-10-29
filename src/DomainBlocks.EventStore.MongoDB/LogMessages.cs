using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB;

internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] started")]
    internal static partial void SubscriptionStarted(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Debug, "[sub: {SubscriptionId}] catch-up boundary is {EndPos}")]
    internal static partial void CatchUpBoundary(this ILogger logger, string subscriptionId, ulong? endPos);

    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}, attempt: {Attempt}] catch-up read failed; retrying in {Delay}")]
    internal static partial void CatchUpRetrying(
        this ILogger logger,
        Exception exception,
        string subscriptionId,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] caught up")]
    internal static partial void SubscriptionCaughtUp(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Warning, "[sub: {SubscriptionId}] fell behind")]
    internal static partial void SubscriptionFellBehind(this ILogger logger, string subscriptionId);

    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}] queue overflowed (capacity {QueueCapacity}); restart pending")]
    internal static partial void SubscriptionQueueOverflowed(
        this ILogger logger,
        string subscriptionId,
        int queueCapacity);

    [LoggerMessage(LogLevel.Warning, "[sub: {SubscriptionId}] feed was reset; restart pending")]
    internal static partial void SubscriptionFeedReset(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Warning, "[sub: {SubscriptionId}] change stream history was lost; restart pending")]
    internal static partial void SubscriptionChangeHistoryLost(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] restarting from start")]
    internal static partial void SubscriptionRestartingFromStart(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] restarting after position {Position}")]
    internal static partial void SubscriptionRestarting(this ILogger logger, string subscriptionId, ulong position);

    [LoggerMessage(LogLevel.Debug, "[sub: {SubscriptionId}] canceled")]
    internal static partial void SubscriptionCanceled(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Error, "[sub: {SubscriptionId}] failed")]
    internal static partial void SubscriptionFailed(this ILogger logger, Exception exception,
        string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] stopped")]
    internal static partial void SubscriptionStopped(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] started")]
    internal static partial void ChangeStreamStarted(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] connected")]
    internal static partial void ChangeStreamConnected(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] stopping")]
    internal static partial void ChangeStreamStopping(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Trace, "[feed: {FeedId}] processed batch of {Count}")]
    internal static partial void ChangeStreamBatchProcessed(this ILogger logger, string feedId, int count);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] cursor ended; reconnecting")]
    internal static partial void ChangeStreamCursorEnded(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] connection lost; reconnecting")]
    internal static partial void ChangeStreamConnectionLost(this ILogger logger, Exception exception, string feedId);

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}, attempt: {Attempt}] failed to connect; retrying in {Delay}")]
    internal static partial void ChangeStreamRetrying(
        this ILogger logger,
        Exception exception,
        string feedId,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] canceled")]
    internal static partial void ChangeStreamCanceled(this ILogger logger, string feedId);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}] failed")]
    internal static partial void ChangeStreamFailed(this ILogger logger, Exception exception, string feedId);

    [LoggerMessage(LogLevel.Debug, "[feed: {FeedId}] stopped")]
    internal static partial void ChangeStreamStopped(this ILogger logger, string feedId);
}