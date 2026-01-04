namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClientOptions<TEvent, TEventDocument> where TEvent : notnull
{
    public required EventCollectionOptions<TEventDocument> Collection { get; init; }
    public required IEventDocumentCodec<TEvent, TEventDocument> DocumentCodec { get; init; }
}