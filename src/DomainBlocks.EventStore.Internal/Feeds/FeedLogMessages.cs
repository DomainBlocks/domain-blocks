using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Feeds;

internal static partial class FeedLogMessages
{
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

    [LoggerMessage(LogLevel.Warning, "[feed: {FeedId}] reset; notifying {ObserverCount} observer(s)")]
    internal static partial void FeedReset(this ILogger logger, string feedId, int observerCount);

    [LoggerMessage(LogLevel.Error, "[feed: {FeedId}, obs: {ObserverId}] failed while receiving an item; detaching")]
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