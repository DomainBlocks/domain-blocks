using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;

public sealed class LeaderLeaseContender(ILeaseClient leaseClient) : ILeaderLeaseContender
{
    public const string ResourceId = "dbx_LogLease";

    private ILeaseHandle<LeaseState>? _handle;

    public async Task RunAsync(ILeaderLeaseObserver observer, CancellationToken stopToken = default)
    {
        var options = new AcquireLeaseOptions
        {
            AcquireTimeout = Timeout.InfiniteTimeSpan
        };

        while (!stopToken.IsCancellationRequested)
        {
            var result = await leaseClient
                .AcquireLeaseAsync<LeaseState>(ResourceId, options, stopToken)
                .ConfigureAwait(false);

            if (!result.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            var handle = result.Handle;

            await using (handle.ConfigureAwait(false))
            {
                Volatile.Write(ref _handle, handle);
                await observer.OnLeaderLeaseAcquired(handle, stopToken).ConfigureAwait(false);

                try
                {
                    var leaseLostInfo = await handle.LeaseLostTask.WaitAsync(stopToken).ConfigureAwait(false);
                    await observer.OnLeaderLeaseLost(handle.Claim, leaseLostInfo, stopToken).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _handle, null);
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var handle = Volatile.Read(ref _handle);
        if (handle != null)
            await handle.DisposeAsync();
    }
}