namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventSubscriber<in TStreamId, TStreamPos, TLogPos> where TStreamPos : notnull where TLogPos : notnull
{
    IAsyncEnumerable<ISubscriptionMessage> Subscribe(SubscriptionDefinition<TLogPos> definition);

    IAsyncEnumerable<ISubscriptionMessage> Subscribe(TStreamId streamId, SubscriptionDefinition<TStreamPos> definition);
}