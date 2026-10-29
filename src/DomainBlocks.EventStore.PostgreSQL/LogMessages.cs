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

    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}] catch-up still waiting after {Elapsed} for queries to see position {Position}")]
    internal static partial void CatchUpWaitingForVisibility(
        this ILogger logger,
        string subscriptionId,
        long position,
        TimeSpan elapsed);

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

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] restarting from start")]
    internal static partial void SubscriptionRestartingFromStart(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] restarting after position {Position}")]
    internal static partial void SubscriptionRestarting(this ILogger logger, string subscriptionId, ulong position);

    [LoggerMessage(LogLevel.Debug, "[sub: {SubscriptionId}] canceled")]
    internal static partial void SubscriptionCanceled(this ILogger logger, string subscriptionId);

    [LoggerMessage(LogLevel.Error, "[sub: {SubscriptionId}] failed")]
    internal static partial void SubscriptionFailed(this ILogger logger, Exception exception, string subscriptionId);

    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] stopped")]
    internal static partial void SubscriptionStopped(this ILogger logger, string subscriptionId);

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

    [LoggerMessage(LogLevel.Debug, "[replication] created slot {SlotName}")]
    internal static partial void ReplicationSlotCreated(this ILogger logger, string slotName);

    [LoggerMessage(LogLevel.Debug, "[replication] dropped slot {SlotName}")]
    internal static partial void ReplicationSlotDropped(this ILogger logger, string slotName);

    [LoggerMessage(LogLevel.Warning, "[replication] could not drop slot {SlotName}; a later cleanup will")]
    internal static partial void ReplicationSlotDropFailed(this ILogger logger, Exception exception, string slotName);

    [LoggerMessage(LogLevel.Information, "[replication] dropped inactive slot {SlotName}")]
    internal static partial void InactiveReplicationSlotDropped(this ILogger logger, string slotName);

    [LoggerMessage(
        LogLevel.Debug,
        "[replication] skipped inactive slot {SlotName}, which was dropped or became active")]
    internal static partial void InactiveReplicationSlotDropSkipped(
        this ILogger logger,
        Exception exception,
        string slotName);

    [LoggerMessage(LogLevel.Warning, "[replication] could not drop inactive slots; trying again in {Interval}")]
    internal static partial void InactiveReplicationSlotCleanupFailed(
        this ILogger logger,
        Exception exception,
        TimeSpan interval);

    [LoggerMessage(LogLevel.Warning, "[replication] slot {SlotName} was dropped or invalidated; creating a new one")]
    internal static partial void ReplicationSlotLost(this ILogger logger, string slotName);

    [LoggerMessage(
        LogLevel.Warning,
        "[replication] the server has synchronous standbys but no synchronized_standby_slots, so live subscriptions " +
        "can receive events that a failover loses")]
    internal static partial void ReplicationSlotNotSynchronized(this ILogger logger);

    [LoggerMessage(
        LogLevel.Warning,
        "[replication] could not end walsender {ProcessId}, which holds slot {SlotName}; waiting for it to notice " +
        "that its client has gone")]
    internal static partial void WalSenderTerminationDenied(
        this ILogger logger,
        Exception exception,
        int processId,
        string slotName);

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