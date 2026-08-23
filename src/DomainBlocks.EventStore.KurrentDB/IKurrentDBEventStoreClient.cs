using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStoreClient<TEvent> : IEventStoreClient<TEvent, string, StreamPosition, Position>
    where TEvent : notnull;