namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Why a <see cref="SubscriptionQueue{TEvent,TSourceEvent,TStreamId,TStreamPos,TLogPos}"/> stopped taking events.
/// </summary>
internal enum SubscriptionStopReason
{
    None,
    QueueOverflow,
    Disposed
}