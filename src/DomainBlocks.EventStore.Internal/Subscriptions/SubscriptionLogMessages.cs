using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

internal static partial class SubscriptionLogMessages
{
    [LoggerMessage(
        LogLevel.Warning,
        "[sub: {SubscriptionId}] queue overflowed (capacity {QueueCapacity}); restart pending")]
    internal static partial void SubscriptionQueueOverflowed(
        this ILogger logger,
        string subscriptionId,
        int queueCapacity);

    [LoggerMessage(LogLevel.Warning, "[sub: {SubscriptionId}] feed was reset; restart pending")]
    internal static partial void SubscriptionFeedReset(this ILogger logger, string subscriptionId);
}