using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreTestBase<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    protected abstract IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> CreateEventStore(
        EventTypeMap eventTypeMap,
        string name = "default",
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null);

    protected virtual TStreamPos CreateStreamPosition(ulong value) => throw new NotImplementedException();
}