using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

/// <summary>
/// An observer's attachment to a change stream. Disposing it detaches the observer.
/// </summary>
internal interface IChangeStreamAttachment : IAsyncDisposable
{
    /// <inheritdoc cref="IChangeStreamConnection.OperationTime"/>
    BsonTimestamp OperationTime { get; }
}