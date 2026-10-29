using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

/// <summary>
/// Represents one change stream of a feed. It records on the feed's <see cref="ChangeStreamResumePoint"/> what it
/// delivers, so that the next change stream resumes where this one stops.
/// </summary>
internal sealed class ChangeStreamSession<TChange, TResult>(
    IChangeStreamCursor<TChange> cursor,
    ChangeStreamResumePoint resumePoint,
    string description,
    bool isResumed,
    BsonTimestamp? highWaterMark,
    Func<TChange, BsonDocument> resumeTokenSelector,
    Func<TChange, BsonTimestamp> clusterTimeSelector,
    Func<TChange, TResult> resultSelector,
    ILogger? logger) :
    IFeedSession<TResult, BsonTimestamp>
{
    public string Description { get; } = description;

    public bool IsResumed { get; } = isResumed;

    /// <summary>
    /// Gets an optime at or after every change that the session has delivered or won't deliver. A new change stream
    /// starts it at the primary's last write, just before the stream's start, and a resumed one has none until its
    /// first change. A majority read that waits for it sees every such change, even on a node whose commit point
    /// lagged after a failover.
    /// </summary>
    public BsonTimestamp? HighWaterMark { get; private set; } = highWaterMark;

    public async IAsyncEnumerable<TResult> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            var batchCount = 0;

            foreach (var change in cursor.Current)
            {
                var result = resultSelector(change);

                // A change's cluster time is the optime of the oplog entry that committed it, so reads can wait for it.
                HighWaterMark = clusterTimeSelector(change);

                yield return result;

                // The feed has handed the change to its observers.
                resumePoint.Record(resumeTokenSelector(change));
                batchCount++;
            }

            logger?.ChangeStreamBatchProcessed(batchCount);

            if (cursor.GetResumeToken() is { } batchResumeToken)
                resumePoint.Record(batchResumeToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        cursor.Dispose();
        return ValueTask.CompletedTask;
    }
}