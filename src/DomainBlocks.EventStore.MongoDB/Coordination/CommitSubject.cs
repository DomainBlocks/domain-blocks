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

    public void NotifyCommitPositionAdvanced(long commitPosition)
    {
        foreach (var observer in _observers)
        {
            try
            {
                observer.OnCommitPositionAdvanced(commitPosition);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error invoking OnCommitPositionAdvanced for position {Position}", commitPosition);
            }
        }
    }

    public void NotifyCommitted(Guid commitId)
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

    public void NotifyDuplicatesSkipped(BsonValue payload)
    {
        var commitIds = payload[DuplicatesSkipped.FieldNames.CommitIds].AsBsonArray;

        foreach (var id in commitIds)
            NotifyCommitted(id.AsGuid);
    }

    public void NotifyConflictsRejected(BsonValue payload)
    {
        var conflicts = payload[ConflictsRejected.FieldNames.Conflicts].AsBsonArray;
        var observers = _observers;

        foreach (var conflict in conflicts)
        {
            var commitId = conflict[AppendConflict.FieldNames.CommitId].AsGuid;

            foreach (var observer in observers)
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
}