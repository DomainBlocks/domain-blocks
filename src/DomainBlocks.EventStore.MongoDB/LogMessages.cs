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

    [LoggerMessage(LogLevel.Trace, "[change stream] processed batch of {Count}")]
    internal static partial void ChangeStreamBatchProcessed(this ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "[change stream] history lost; starting a new change stream")]
    internal static partial void ChangeStreamHistoryLost(this ILogger logger, Exception exception);
}