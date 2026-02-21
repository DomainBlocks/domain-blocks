using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.State;

public sealed class CommitCoordinatorState(EventStoreNamespaceSettings namespaceSettings)
{
    private readonly AppendRequestsState _requests = new(namespaceSettings.AppendRequestsCollectionNamespace);
    private readonly LogLeaseState _leaseState = new(namespaceSettings.LeasesCollectionNamespace);

    public async Task LoadAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _requests.LoadAsync(database, cancellationToken),
            _leaseState.LoadAsync(database, cancellationToken));
    }

    public bool TryApply(ChangeStreamDocument<BsonDocument> change, [NotNullWhen(true)] out IChangeEvent? changeEvent)
    {
        if (_requests.TryApply(change, out changeEvent)) return true;
        if (_leaseState.TryApply(change, out changeEvent)) return true;

        changeEvent = null;
        return false;
    }
}