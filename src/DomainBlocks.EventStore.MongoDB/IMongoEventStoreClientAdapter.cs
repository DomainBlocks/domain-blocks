using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStoreClientAdapter<TPayload> : IEventStoreClientAdapter<TPayload> where TPayload : notnull;