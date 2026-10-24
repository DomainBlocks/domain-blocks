using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

/// <summary>
/// An observer's attachment to a change stream. Disposing it detaches the observer.
/// </summary>
internal interface IChangeStreamAttachment : IAsyncDisposable
{
    /// <summary>
    /// The connection's <see cref="IChangeStreamConnection.OpTimeHighWaterMark"/>, read once the observer was attached,
    /// so it covers every change delivered before the observer could receive it.
    /// </summary>
    BsonTimestamp OpTimeHighWaterMark { get; }
}