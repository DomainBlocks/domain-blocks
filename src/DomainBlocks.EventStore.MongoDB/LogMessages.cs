using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB;

internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] started")]
    internal static partial void SubscriptionStarted(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Debug, "[sub: {SubscriptionId}] catch-up boundary is {HighWaterMark}")]
    internal static partial void CatchUpBoundary(this ILogger logger, string subscriptionId, ulong? highWaterMark);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] caught up")]
    internal static partial void SubscriptionCaughtUp(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Warning, "[sub: {SubscriptionId}] fell behind; restarting")]
    internal static partial void SubscriptionFellBehind(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Debug, "[sub: {SubscriptionId}] canceled")]
    internal static partial void SubscriptionCanceled(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Error, "[sub: {SubscriptionId}] failed")]
    internal static partial void SubscriptionFailed(this ILogger logger, Exception exception,
        string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] stopped")]
    internal static partial void SubscriptionStopped(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}, obs: {ObserverId}, count: {ObserverCount}] attached")]
    internal static partial void ObserverAttached(
        this ILogger logger,
        string subjectId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}, obs: {ObserverId}, count: {ObserverCount}] detached")]
    internal static partial void ObserverDetached(
        this ILogger logger,
        string subjectId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}] started")]
    internal static partial void ChangeStreamStarted(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}] connected")]
    internal static partial void ChangeStreamConnected(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}] stopping")]
    internal static partial void ChangeStreamStopping(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Trace, "[chg: {SubjectId}] processed batch of {Count}")]
    internal static partial void ChangeStreamBatchProcessed(this ILogger logger, string subjectId, int count);

    [LoggerMessage(LogLevel.Warning, "[chg: {SubjectId}] cursor ended; reconnecting")]
    internal static partial void ChangeStreamCursorEnded(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Warning, "[chg: {SubjectId}] connection lost; reconnecting")]
    internal static partial void ChangeStreamConnectionLost(this ILogger logger, Exception exception, string subjectId);

    [LoggerMessage(LogLevel.Warning, "[chg: {SubjectId}, attempt: {Attempt}] failed to connect; retrying in {Delay}")]
    internal static partial void ChangeStreamRetrying(
        this ILogger logger,
        Exception exception,
        string subjectId,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}] canceled")]
    internal static partial void ChangeStreamCanceled(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Error, "[chg: {SubjectId}] failed")]
    internal static partial void ChangeStreamFailed(this ILogger logger, Exception exception, string subjectId);

    [LoggerMessage(LogLevel.Debug, "[chg: {SubjectId}] stopped")]
    internal static partial void ChangeStreamStopped(this ILogger logger, string subjectId);

    [LoggerMessage(LogLevel.Error, "[chg: {SubjectId}, obs: {ObserverId}] failed while receiving a change; detaching")]
    internal static partial void ObserverOnNextFailed(
        this ILogger logger,
        Exception exception,
        string subjectId,
        string observerId);

    [LoggerMessage(LogLevel.Error, "[chg: {SubjectId}, obs: {ObserverId}] failed while receiving an error")]
    internal static partial void ObserverOnErrorFailed(
        this ILogger logger,
        Exception exception,
        string subjectId,
        string observerId);
}