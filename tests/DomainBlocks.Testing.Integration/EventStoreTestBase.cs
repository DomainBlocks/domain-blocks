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
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<TEvent>>? contractMappers = null,
        string loggerNameSuffix = "");

    protected virtual TStreamPos CreateStreamPosition(ulong value) => throw new NotImplementedException();
}