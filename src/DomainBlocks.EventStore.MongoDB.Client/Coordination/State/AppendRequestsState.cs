using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using FieldNames = DomainBlocks.EventStore.MongoDB.Client.Schema2.AppendRequestFieldNames;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.State;

public sealed class AppendRequestsState(CollectionNamespace collectionNamespace)
{
    private readonly Dictionary<Guid, BsonDocument> _requests = [];

    public async Task LoadAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var appendRequests = database.GetCollection<BsonDocument>(collectionNamespace.CollectionName);

        using var appendRequestsCursor = await appendRequests.FindAsync(
            Builders<BsonDocument>.Filter.Empty,
            cancellationToken: cancellationToken);

        while (await appendRequestsCursor.MoveNextAsync(cancellationToken))
        {
            foreach (var appendRequest in appendRequestsCursor.Current)
            {
                var commitId = appendRequest[FieldNames.CommitId].AsBsonBinaryData.AsGuid;
                _requests.Add(commitId, appendRequest);
            }
        }
    }

    public bool TryApply(ChangeStreamDocument<BsonDocument> change, [NotNullWhen(true)] out IChangeEvent? changeEvent)
    {
        changeEvent = null;

        if (!change.CollectionNamespace.Equals(collectionNamespace))
            return false;

        var commitId = change.DocumentKey[FieldNames.CommitId].AsBsonBinaryData.AsGuid;

        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
                _requests.Add(commitId, change.FullDocument);
                changeEvent = new AppendRequested(commitId);
                return true;
            case ChangeStreamOperationType.Update:
            {
                if (!_requests.TryGetValue(commitId, out var request))
                    return false;

                var updatedFields = change.UpdateDescription.UpdatedFields;
                if (!updatedFields.TryGetValue(FieldNames.LastSeenAtUtc, out var lastSeenAtUtc))
                    return false;

                request[FieldNames.LastSeenAtUtc] = lastSeenAtUtc;
                changeEvent = new AppendRequested(commitId);
                return true;
            }
            default:
                return false;
        }
    }

    public bool TryGet(Guid commitId, [NotNullWhen(true)] out AppendRequest? request)
    {
        if (!_requests.TryGetValue(commitId, out var doc))
        {
            request = null;
            return false;
        }

        request = BsonSerializer.Deserialize<AppendRequest>(doc);
        return true;
    }
}