using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStore<TPayload> : IEventStoreBackend<TPayload>;