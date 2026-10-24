using DomainBlocks.EventStore;

namespace DomainBlocks.Testing.Integration.EventStore;

public static class SubscriptionProbeExtensions
{
    /// <summary>
    /// Reads the subscription through a probe, which ends a pending read before it disposes the subscription.
    /// </summary>
    public static SubscriptionProbe<TStreamPos, TLogPos> Probe<TStreamPos, TLogPos>(
        this IAsyncEnumerable<SubscriptionMessage<object, string, TStreamPos, TLogPos>> subscription,
        CancellationToken cancellationToken)
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        return new SubscriptionProbe<TStreamPos, TLogPos>(subscription, cancellationToken);
    }
}