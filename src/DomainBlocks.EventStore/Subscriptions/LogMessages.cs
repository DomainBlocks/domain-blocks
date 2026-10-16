using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}] starting")]
    internal static partial void TailStarting(this ILogger logger, string tailId);

    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}] started at the end of the log")]
    internal static partial void TailStarted(this ILogger logger, string tailId);

    [LoggerMessage(LogLevel.Warning, "[tail: {TailId}, attempt: {Attempt}] read failed; retrying in {Delay}")]
    internal static partial void TailRetrying(
        this ILogger logger,
        Exception exception,
        string tailId,
        int attempt,
        TimeSpan delay);

    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}] stopping")]
    internal static partial void TailStopping(this ILogger logger, string tailId);

    [LoggerMessage(LogLevel.Error, "[tail: {TailId}] failed")]
    internal static partial void TailFailed(this ILogger logger, Exception exception, string tailId);

    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}] stopped")]
    internal static partial void TailStopped(this ILogger logger, string tailId);

    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}, obs: {ObserverId}, count: {ObserverCount}] attached")]
    internal static partial void TailObserverAttached(
        this ILogger logger,
        string tailId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Debug, "[tail: {TailId}, obs: {ObserverId}, count: {ObserverCount}] detached")]
    internal static partial void TailObserverDetached(
        this ILogger logger,
        string tailId,
        string observerId,
        int observerCount);

    [LoggerMessage(LogLevel.Error, "[tail: {TailId}, obs: {ObserverId}] failed while receiving an event; detaching")]
    internal static partial void TailObserverOnNextFailed(
        this ILogger logger,
        Exception exception,
        string tailId,
        string observerId);

    [LoggerMessage(LogLevel.Error, "[tail: {TailId}, obs: {ObserverId}] failed while receiving an error")]
    internal static partial void TailObserverOnErrorFailed(
        this ILogger logger,
        Exception exception,
        string tailId,
        string observerId);
}