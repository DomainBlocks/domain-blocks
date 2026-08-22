using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.New;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClient2<TEvent> : IEventStoreClient2<
    AppendEvent<TEvent>,
    ReadEvent2<TEvent, string, StreamPosition, LogPosition>,
    string,
    StreamPosition,
    LogPosition>
    where TEvent : notnull;