using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreAdapter<TPayload> : IEventStoreAdapter<TPayload> where TPayload : notnull;