using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Feeds;

/// <summary>
/// Creates a <see cref="ChangeStreamFeed{TDocument, TChange, TResult}"/>, inferring its type arguments.
/// </summary>
internal static class ChangeStreamFeed
{
    public static ChangeStreamFeed<TDocument, TChange, TResult> Create<TDocument, TChange, TResult>(
        IMongoClient mongoClient,
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
        Func<TChange, BsonDocument> resumeTokenSelector,
        Func<TChange, BsonTimestamp> clusterTimeSelector,
        Func<TChange, TResult> resultSelector,
        FeedResumePolicy resumePolicy,
        ILogger? logger = null)
    {
        return new ChangeStreamFeed<TDocument, TChange, TResult>(
            mongoClient,
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            clusterTimeSelector,
            resultSelector,
            resumePolicy,
            logger);
    }
}

/// <summary>
/// Represents a feed of change streams. The first starts one tick after the primary's last write, and each later one
/// resumes after the last change that the feed delivered, so a lost connection costs no reset. When the oplog no longer
/// holds that change, the next change stream starts afresh, as the first does, and the feed resets.
/// </summary>
internal sealed class ChangeStreamFeed<TDocument, TChange, TResult>(
    IMongoClient mongoClient,
    ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
    PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
    Func<TChange, BsonDocument> resumeTokenSelector,
    Func<TChange, BsonTimestamp> clusterTimeSelector,
    Func<TChange, TResult> resultSelector,
    FeedResumePolicy resumePolicy,
    ILogger? logger = null) :
    EventLogFeed<TResult, BsonTimestamp>(resumePolicy, logger)
{
    private ChangeStreamSession<TChange, TResult>? _lastSession;

    protected override async Task<IFeedSession<TResult, BsonTimestamp>> CreateSessionAsync(
        CancellationToken cancellationToken)
    {
        if (_lastSession?.ResumeToken is { } resumeToken)
        {
            var resumeOptions = new ChangeStreamOptions { ResumeAfter = resumeToken };

            try
            {
                var resumedCursor = await cursorFactory(pipeline, resumeOptions, cancellationToken)
                    .ConfigureAwait(false);

                return CreateSession(
                    resumedCursor,
                    resumeToken,
                    "resumed change stream",
                    isResumed: true,
                    initialMark: null);
            }
            catch (MongoCommandException ex) when (ChangeStreamTransientErrors.IsHistoryLost(ex))
            {
                Logger?.ChangeStreamHistoryLost(ex);
                _lastSession = null;
            }
        }

        // The stream starts one tick after the primary's last write. The mark must be an oplog optime rather than the
        // current cluster time, because a catch-up reads only once the oplog reaches the mark. After a transaction
        // commits, the logical clock runs one tick ahead of the oplog, so a mark at the current cluster time would
        // stall a catch-up until the periodic no-op writer runs, every 10 seconds by default. Only a shard writes a
        // no-op to reach the time at once:
        // https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/db/read_concern_mongod.cpp#L146-L153
        // The server would start the stream at the same point by default, but the mark would be unknown:
        // https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/db/pipeline/document_source_change_stream.cpp#L238-L253
        var lastApplied = await GetLastAppliedOpTimeAsync(cancellationToken).ConfigureAwait(false);
        var startAt = new BsonTimestamp(lastApplied.Value + 1);
        var startOptions = new ChangeStreamOptions { StartAtOperationTime = startAt };
        var cursor = await cursorFactory(pipeline, startOptions, cancellationToken).ConfigureAwait(false);

        // The session starts with the cursor's resume token, so a cursor that fails before its first batch resumes
        // rather than restarting later. The driver exposes the token when the first batch is empty, and a non-empty
        // first batch cannot fail before it is read.
        // See:
        // - https://github.com/mongodb/specifications/blob/bed11334/source/change-streams/change-streams.md#updating-the-cached-resume-token
        // - https://jira.mongodb.org/browse/SERVER-35740
        //
        // The start is inclusive, so the stream won't deliver the changes up to the last write.
        return CreateSession(
            cursor,
            cursor.GetResumeToken(),
            $"change stream from {startAt}",
            isResumed: false,
            lastApplied);
    }

    private ChangeStreamSession<TChange, TResult> CreateSession(
        IChangeStreamCursor<TChange> cursor,
        BsonDocument? resumeToken,
        string description,
        bool isResumed,
        BsonTimestamp? initialMark)
    {
        _lastSession = new ChangeStreamSession<TChange, TResult>(
            cursor,
            resumeToken,
            description,
            isResumed,
            initialMark,
            resumeTokenSelector,
            clusterTimeSelector,
            resultSelector,
            Logger);

        return _lastSession;
    }

    private async Task<BsonTimestamp> GetLastAppliedOpTimeAsync(CancellationToken cancellationToken)
    {
        // hello.lastWrite.opTime is the optime of the last write applied on the node that answers. After a failover,
        // only the primary is sure to have applied every majority-committed write.
        // https://www.mongodb.com/docs/manual/reference/command/hello/
        //
        // This requires MongoDB 4.4.2 or later. The driver requires 4.4, and the hello command arrived in 4.4.2.
        // https://github.com/mongodb/mongo-csharp-driver/blob/v3.11.1/src/MongoDB.Driver/Core/Misc/WireVersion.cs#L187
        // https://github.com/mongodb/mongo/blob/r4.4.2/src/mongo/db/repl/replication_info.cpp#L304
        var hello = await mongoClient
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), ReadPreference.Primary, cancellationToken)
            .ConfigureAwait(false);

        if (!hello.TryGetValue("lastWrite", out var lastWrite))
        {
            throw new NotSupportedException(
                "The server did not report a last write. Change streams require a replica set member.");
        }

        return lastWrite["opTime"]["ts"].AsBsonTimestamp;
    }
}