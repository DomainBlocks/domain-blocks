namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClientOptions<TEventBase, TEventDocument> where TEventBase : class
{
    public required EventCollectionOptions<TEventDocument> Collection { get; init; }
    public required IEventDocumentCodec<TEventBase, TEventDocument> DocumentCodec { get; init; }
}