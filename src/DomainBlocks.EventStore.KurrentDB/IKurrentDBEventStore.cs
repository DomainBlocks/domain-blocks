using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;

public interface IKurrentDBEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, Position>
    where TEvent : notnull;