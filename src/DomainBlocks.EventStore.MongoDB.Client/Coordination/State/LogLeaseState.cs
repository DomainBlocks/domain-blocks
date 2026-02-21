using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases.Schema;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.State;

public sealed class LogLeaseState(CollectionNamespace collectionNamespace)
{
    public const string ResourceId = "dbx_LogLeaseState";

    private LeaseState? _leaseState;
    private long? _commitPosition;

    public async Task LoadAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var leases = database.GetCollection<LeaseState>(collectionNamespace.CollectionName);
        var filter = Builders<LeaseState>.Filter.Eq(x => x.ResourceId, ResourceId);
        var leaseState = await leases.Find(filter).FirstOrDefaultAsync(cancellationToken);

        _leaseState = leaseState;
        _commitPosition = leaseState?.Counters.GetValueOrDefault(CounterNames.CommitPosition);
    }

    public bool TryApply(ChangeStreamDocument<BsonDocument> change, [NotNullWhen(true)] out IChangeEvent? changeEvent)
    {
        changeEvent = null;

        if (!change.CollectionNamespace.Equals(collectionNamespace))
            return false;

        if (change.DocumentKey[LeaseStateFieldNames.ResourceId].AsString != ResourceId)
            return false;

        if (change.FullDocument is null)
            return false;

        var prevCommitPosition = _commitPosition;
        _leaseState = BsonSerializer.Deserialize<LeaseState>(change.FullDocument);
        _commitPosition = _leaseState.Counters.GetValueOrDefault(CounterNames.CommitPosition);

        switch (_leaseState.LastMutation.MutationKind)
        {
            case LeaseStateMutationKind.Acquire:
                changeEvent = new LeaseAcquired(_leaseState.Claim);
                return true;
            case LeaseStateMutationKind.Renew:
                changeEvent = new LeaseRenewed(_leaseState.Claim);
                return true;
            case LeaseStateMutationKind.IncrementCounter:
                if (!_commitPosition.HasValue)
                    return false; // This is probably an error

                if (prevCommitPosition == _commitPosition)
                    return false;

                changeEvent = new CommitPositionAdvanced(_commitPosition.Value);
                return true;
            case LeaseStateMutationKind.Release:
                changeEvent = new LeaseReleased(_leaseState.Claim);
                return true;
            default:
                return false;
        }
    }

    // private static LogProperties? GetLogProperties(LeaseState leaseState)
    // {
    //     return leaseState.ResourceState.IsBsonNull
    //         ? null
    //         : BsonSerializer.Deserialize<LogProperties>(leaseState.ResourceState.AsBsonDocument);
    // }
}