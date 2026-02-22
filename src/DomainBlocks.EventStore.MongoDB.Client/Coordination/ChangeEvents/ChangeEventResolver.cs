using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed class ChangeEventResolver(EventStoreNamespaceSettings namespaceSettings)
{
    public bool TryResolve(ChangeStreamDocument<BsonDocument> change, out IChangeEvent? changeEvent)
    {
        if (TryResolveAppendRequestEvent(change, out changeEvent)) return true;
        if (TryResolveLeaseEvent(change, out changeEvent)) return true;
        return false;
    }

    private bool TryResolveAppendRequestEvent(ChangeStreamDocument<BsonDocument> change, out IChangeEvent? changeEvent)
    {
        changeEvent = null;

        if (!change.CollectionNamespace.Equals(namespaceSettings.AppendRequestsCollectionNamespace))
            return false;

        var id = change.DocumentKey["_id"];
        if (!id.IsGuid)
            return false;

        var commitId = id.AsGuid;

        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
                changeEvent = new AppendRequested(commitId, change.FullDocument);
                return true;
            case ChangeStreamOperationType.Update:
            {
                var updatedFields = change.UpdateDescription.UpdatedFields;

                if (!updatedFields.TryGetValue(AppendRequestFieldNames.LastSeenAtUtc, out var lastSeenAtUtc) ||
                    !lastSeenAtUtc.IsBsonDateTime)
                {
                    return false;
                }

                changeEvent = new AppendRequestRetried(commitId, lastSeenAtUtc.AsBsonDateTime);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryResolveLeaseEvent(ChangeStreamDocument<BsonDocument> change, out IChangeEvent? changeEvent)
    {
        changeEvent = null;

        if (!change.CollectionNamespace.Equals(namespaceSettings.LeasesCollectionNamespace))
            return false;

        var id = change.DocumentKey["_id"];
        if (!id.IsString)
            return false;

        var resourceId = id.AsString;
        if (resourceId != CommitCoordinator.LeaseResourceId)
            return false;

        if (change.FullDocument is null)
        {
            throw new InvalidOperationException(
                "FullDocument was expected but is null. Ensure pre/post images are enabled on the " +
                $"'{namespaceSettings.LeasesCollectionNamespace}' collection.");
        }

        var lease = BsonSerializer.Deserialize<LeaseDocument>(change.FullDocument);

        switch (lease.LastUpdateKind)
        {
            case LeaseUpdateKind.Acquired:
                changeEvent = new LeaseAcquired(lease.Claim);
                return true;
            case LeaseUpdateKind.Renewed:
                changeEvent = new LeaseRenewed(lease.Claim);
                return true;
            case LeaseUpdateKind.StateUpdated:
                const string commitPositionFieldPath =
                    $"{LeaseDocument.FieldNames.State}.{LogLeaseState.FieldNames.CommitPosition}";

                if (!change.UpdateDescription.UpdatedFields.Contains(commitPositionFieldPath))
                    return false;

                var commitPosition = lease.State[LogLeaseState.FieldNames.CommitPosition].AsInt64;
                changeEvent = new CommitPositionAdvanced(commitPosition);
                return true;
            case LeaseUpdateKind.Released:
                changeEvent = new LeaseReleased(lease.Claim);
                return true;
            default:
                return false;
        }
    }
}