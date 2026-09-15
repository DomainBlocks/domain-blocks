using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamConnection : IAsyncDisposable
{
    Task Completion { get; }

    /// <summary>
    /// The optime the change stream is anchored to: every change after it is delivered, and a majority read that
    /// waits for it sees at least every change up to it.
    /// </summary>
    BsonTimestamp OperationTime { get; }
}