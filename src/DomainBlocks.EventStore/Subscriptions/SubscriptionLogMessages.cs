using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

internal static partial class SubscriptionLogMessages
{
    [LoggerMessage(LogLevel.Information, "[sub: {SubscriptionId}] started")]
    internal static partial void SubscriptionStarted(this ILogger logger, string subscriptionId);

    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}] queue overflowed (capacity {QueueCapacity}); restart pending")]
    internal static partial void SubscriptionQueueOverflowed(
        this ILogger logger,
        string subscriptionId,
        int queueCapacity);
}