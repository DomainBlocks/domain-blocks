using System.Collections.Immutable;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class CommitSubject(ILogger<CommitSubject> logger)
{
    private ImmutableArray<ICommitObserver> _observers = [];

    public void Attach(ICommitObserver observer)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, item) => current.Add(item),
            observer);
    }

    public void NotifyCommitted(Guid commitId)
    {
        InvokeCommitted(commitId);
    }

    public void NotifyDuplicatesSkipped(BsonValue payload)
    {
        var commitIds = payload[DuplicatesSkipped.FieldNames.CommitIds].AsBsonArray;

        foreach (var id in commitIds)
            InvokeCommitted(id.AsGuid);
    }

    public void NotifyConflictsRejected(BsonValue payload)
    {
        var conflicts = payload[ConflictsRejected.FieldNames.Conflicts].AsBsonArray;

        foreach (var conflict in conflicts)
        {
            var commitId = conflict[AppendConflict.FieldNames.CommitId].AsGuid;
            InvokeConflictRejected(commitId, conflict);
        }
    }

    private void InvokeCommitted(Guid commitId)
    {
        foreach (var observer in _observers)
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

    private void InvokeConflictRejected(Guid commitId, BsonValue conflict)
    {
        foreach (var observer in _observers)
        {
            try
            {
                observer.OnConflictRejected(commitId, conflict);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitRejected for commit ID {CommitId}", commitId);
            }
        }
    }
}