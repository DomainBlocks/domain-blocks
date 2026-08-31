using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB;

public interface IKurrentDBEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, Position>
    where TEvent : notnull;