using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal interface IChangeStreamConnection : IAsyncDisposable
{
    Task Completion { get; }

    /// <summary>
    /// An optime at or after every change that the stream has delivered or won't deliver. It starts just before the
    /// stream's start, or at the primary's last write when a resume token sets the start, and moves forwards with each
    /// change delivered. A majority read that waits for it sees every such change, even on a node whose commit point
    /// lagged after a failover.
    /// </summary>
    BsonTimestamp OpTimeHighWaterMark { get; }
}