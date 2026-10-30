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

    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}] catch-up still waiting after {Elapsed} for queries to see position {Position}")]
    internal static partial void CatchUpWaitingForVisibility(
        this ILogger logger,
        string subscriptionId,
        long position,
        TimeSpan elapsed);

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
}