using DomainBlocks.EventStore.MongoDB.Client.Appender;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class LeaderWorkerRunner(
    IEnumerable<ILeaderWorkerFactory> factories,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject) :
    ILeaseObserver
{
    private Session? _session;

    public async Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken)
    {
        _session = new Session(handle, factories, changeStreamSubject);
        await _session.StartAsync(cancellationToken);
    }

    public async Task OnLeaseLostAsync(
        LeaseClaim leaseClaim,
        LeaseLostInfo? leaseLostInfo,
        CancellationToken cancellationToken)
    {
        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly ILeaderWorker[] _workers;
        private readonly IDisposable _attachment;

        public Session(
            ILeaseHandle<LeaseState> handle,
            IEnumerable<ILeaderWorkerFactory> factories,
            IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject)
        {
            _workers = [.. factories.Select(x => x.Create(handle))];
            _attachment = changeStreamSubject.AttachGroup(_workers);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var worker in _workers)
                await worker.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _attachment.Dispose();

            foreach (var worker in _workers)
                await worker.DisposeAsync().ConfigureAwait(false);
        }
    }
}