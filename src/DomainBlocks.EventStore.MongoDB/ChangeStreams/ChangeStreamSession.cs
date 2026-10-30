using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

/// <summary>
/// Represents one change stream of a feed. It keeps the token of what it has delivered, so that the next change stream
/// resumes where this one stops.
/// </summary>
internal sealed class ChangeStreamSession<TChange, TResult>(
    IChangeStreamCursor<TChange> cursor,
    BsonDocument? resumeToken,
    string description,
    bool isResumed,
    BsonTimestamp? initialHighWaterMark,
    Func<TChange, BsonDocument> resumeTokenSelector,
    Func<TChange, BsonTimestamp> clusterTimeSelector,
    Func<TChange, TResult> resultSelector,
    ILogger? logger) :
    IFeedSession<TResult, BsonTimestamp>
{
    public string Description { get; } = description;

    public bool IsResumed { get; } = isResumed;

    /// <summary>
    /// Gets the token that a later change stream resumes after: that of the last change delivered or, before the first,
    /// the one that the session started from. It is <see langword="null"/> if there is none yet.
    /// </summary>
    public BsonDocument? ResumeToken { get; private set; } = resumeToken;

    /// <summary>
    /// Gets an optime at or after every change that the session won't deliver. A new change stream has the primary's
    /// last write, just before the stream's start, and a resumed one has none. A majority read that waits for it sees
    /// every such change, even on a node whose commit point lagged after a failover.
    /// </summary>
    public BsonTimestamp? InitialHighWaterMark { get; } = initialHighWaterMark;

    public async IAsyncEnumerable<(TResult Item, BsonTimestamp HighWaterMark)> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            var batchCount = 0;

            foreach (var change in cursor.Current)
            {
                var result = resultSelector(change);

                // A change's cluster time is the optime of the oplog entry that committed it, so reads can wait for it.
                yield return (result, clusterTimeSelector(change));

                // The feed has handed the change to its observers.
                ResumeToken = resumeTokenSelector(change);
                batchCount++;
            }

            logger?.ChangeStreamBatchProcessed(batchCount);

            if (cursor.GetResumeToken() is { } batchResumeToken)
                ResumeToken = batchResumeToken;
        }
    }

    public ValueTask DisposeAsync()
    {
        cursor.Dispose();
        return ValueTask.CompletedTask;
    }
}