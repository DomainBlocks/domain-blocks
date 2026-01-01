namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreConnectionProvider<TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    ValueTask<ConnectionScope<TEventData, TMetadata>> AcquireAsync(CancellationToken cancellationToken = default);
}