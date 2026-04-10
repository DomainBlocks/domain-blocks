using System.Collections.Immutable;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class CommitSubject(ILogger<CommitSubject> logger)
{
    private ImmutableArray<ICommitObserver> _observers = [];

    public void Attach(ICommitObserver observer)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, item) => current.Add(item),
            observer);
    }

    public void Notify(BsonValue appendBatchRecorded)
    {
        var observersSnapshot = _observers;
        if (observersSnapshot.IsEmpty)
            return;

        var appendedCommitIds = appendBatchRecorded[AppendBatchRecorded.FieldNames.AppendedCommitIds].AsBsonArray;
        var duplicateCommitIds = appendBatchRecorded[AppendBatchRecorded.FieldNames.DuplicateCommitIds].AsBsonArray;
        var rejections = appendBatchRecorded[AppendBatchRecorded.FieldNames.Rejections].AsBsonArray;

        foreach (var commitId in appendedCommitIds.Concat(duplicateCommitIds))
            NotifyCommitted(commitId.AsGuid, observersSnapshot);

        foreach (var rejection in rejections)
        {
            var commitId = rejection[CommitRejection.FieldNames.CommitId].AsGuid;
            NotifyCommitRejected(commitId, rejection, observersSnapshot);
        }
    }

    private void NotifyCommitted(Guid commitId, ImmutableArray<ICommitObserver> observers)
    {
        foreach (var observer in observers)
        {
            try
            {
                observer.OnCommitted(commitId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitted for commit ID {CommitId}", commitId);
            }
        }
    }

    private void NotifyCommitRejected(Guid commitId, BsonValue rejection, ImmutableArray<ICommitObserver> observers)
    {
        foreach (var observer in observers)
        {
            try
            {
                observer.OnCommitRejected(commitId, rejection);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitRejected for commit ID {CommitId}", commitId);
            }
        }
    }
}