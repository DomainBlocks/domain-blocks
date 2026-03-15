using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class ChangeStreamEventResolver(EventStoreNamespaceSettings namespaceSettings)
{
    public bool TryResolve(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IAppenderEvent? @event)
    {
        if (TryResolveAppendRequestEvent(change, out @event)) return true;
        if (TryResolveLeaseEvent(change, out @event)) return true;
        return false;
    }

    private bool TryResolveAppendRequestEvent(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IAppenderEvent? @event)
    {
        @event = null;

        if (!change.CollectionNamespace.Equals(namespaceSettings.AppendRequestsCollectionNamespace))
            return false;

        var id = change.DocumentKey["_id"];
        if (!id.IsGuid)
            return false;

        var commitId = id.AsGuid;

        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
                @event = new AppendRequestObserved(commitId, change.FullDocument);
                return true;
            case ChangeStreamOperationType.Update:
            {
                var updatedFields = change.UpdateDescription.UpdatedFields;

                if (!updatedFields.TryGetValue(AppendRequest.FieldNames.LastSeenAtUtc, out var lastSeenAtUtc) ||
                    !lastSeenAtUtc.IsBsonDateTime)
                {
                    return false;
                }

                @event = new AppendRequestRetried(commitId, lastSeenAtUtc.AsBsonDateTime);
                return true;
            }
            default:
                return false;
        }
    }

    private bool TryResolveLeaseEvent(
        ChangeStreamDocument<BsonDocument> change,
        [NotNullWhen(true)] out IAppenderEvent? @event)
    {
        @event = null;

        if (!change.CollectionNamespace.Equals(namespaceSettings.LeasesCollectionNamespace))
            return false;

        var id = change.DocumentKey["_id"];
        if (!id.IsString)
            return false;

        var resourceId = id.AsString;
        if (resourceId != LeaseContender.ResourceId)
            return false;

        var doc = change.FullDocument ?? throw new InvalidOperationException(
            "FullDocument was expected but is null. Ensure pre/post images are enabled on the " +
            $"'{namespaceSettings.LeasesCollectionNamespace}' collection.");

        var snapshot = new LeaseSnapshotView(doc);
        @event = new LeaseUpdateObserved(snapshot);
        return true;
    }
}