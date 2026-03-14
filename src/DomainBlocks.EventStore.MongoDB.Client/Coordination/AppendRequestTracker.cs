using System.Collections.Concurrent;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public class AppendRequestTracker(CollectionNamespace appendRequestsNamespace) :
    IAppendRequestTracker,
    IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _pendingRequests = [];

    public Task WaitAsync(Guid commitId, CancellationToken cancellationToken = default)
    {
        var tcs = _pendingRequests.GetOrAdd(
            commitId,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        return tcs.Task.WaitAsync(cancellationToken);
    }

    public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
    {
        if (!change.CollectionNamespace.Equals(appendRequestsNamespace))
            return ValueTask.CompletedTask;

        var id = change.DocumentKey["_id"];
        if (!id.IsGuid)
            return ValueTask.CompletedTask;

        var commitId = id.AsGuid;

        if (_pendingRequests.TryRemove(commitId, out var tcs))
            tcs.TrySetResult();

        return ValueTask.CompletedTask;
    }
}