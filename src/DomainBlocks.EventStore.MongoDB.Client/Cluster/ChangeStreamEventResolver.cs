using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster;

public sealed class ChangeStreamEventResolver(EventStoreNamespaceSettings namespaceSettings)
{
    private const string CommitPositionFieldPath =
        $"{LeaseDocument.FieldNames.State}.{LogLeaseState.FieldNames.CommitPosition}";

    public bool TryResolve(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IChangeStreamEvent? changeEvent)
    {
        if (TryResolveAppendRequestEvent(change, out changeEvent)) return true;
        if (TryResolveLeaseEvent(change, out changeEvent)) return true;
        return false;
    }

    private bool TryResolveAppendRequestEvent(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IChangeStreamEvent? changeEvent)
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

                if (!updatedFields.TryGetValue(AppendRequest.FieldNames.LastSeenAtUtc, out var lastSeenAtUtc) ||
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

    private bool TryResolveLeaseEvent(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IChangeStreamEvent? changeEvent)
    {
        changeEvent = null;

        if (!change.CollectionNamespace.Equals(namespaceSettings.LeasesCollectionNamespace))
            return false;

        var id = change.DocumentKey["_id"];
        if (!id.IsString)
            return false;

        var resourceId = id.AsString;
        if (resourceId != AppenderNode.LeaseResourceId)
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
                if (!change.UpdateDescription.UpdatedFields.Contains(CommitPositionFieldPath))
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