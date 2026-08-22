using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.New;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStoreClient2<TEvent> : IEventStoreClient2<
    AppendEvent<TEvent>,
    ReadEvent2<TEvent, string, StreamPosition, Position>,
    string,
    StreamPosition,
    Position>
    where TEvent : notnull;