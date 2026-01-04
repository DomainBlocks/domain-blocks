using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentEncoder<TEventBase, out TEventDocument> where TEventBase : class
{
    TEventDocument Encode(
        AppendEvent<TEventBase> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAtUtc);
}