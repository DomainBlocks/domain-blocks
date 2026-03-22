using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class LeaseContender(ILeaseClient leaseClient, ILogger<LeaseContender> logger) : ILeaseContender
{
    public const string ResourceId = "dbx_event_log_lease";

    public async Task RunAsync(
        IReadOnlyCollection<ILeaseObserver> observers,
        CancellationToken cancellationToken = default)
    {
        var options = new AcquireLeaseOptions
        {
            AcquireTimeout = Timeout.InfiniteTimeSpan
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await leaseClient
                .AcquireLeaseAsync<LeaseState>(ResourceId, options, cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            var handle = result.Handle;

            await using (handle.ConfigureAwait(false))
            {
                await NotifyLeaseAcquiredAsync(observers, handle, cancellationToken).ConfigureAwait(false);

                try
                {
                    var leaseLostInfo = await handle.LeaseLostTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                    await NotifyLeaseLostAsync(observers, handle.Claim, leaseLostInfo, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug("Lease contender canceled");
                }
                finally
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async Task NotifyLeaseAcquiredAsync(
        IReadOnlyCollection<ILeaseObserver> observers,
        ILeaseHandle<LeaseState> handle,
        CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnLeaseAcquiredAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(
                    ex,
                    "OnLeaseAcquiredAsync failed for observer '{ObserverType}'",
                    observer.GetType().FullName);
            }
        }
    }

    private async Task NotifyLeaseLostAsync(
        IReadOnlyCollection<ILeaseObserver> observers,
        LeaseClaim leaseClaim,
        LeaseLostInfo? leaseLostInfo,
        CancellationToken cancellationToken)
    {
        foreach (var observer in observers)
        {
            try
            {
                await observer.OnLeaseLostAsync(leaseClaim, leaseLostInfo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(
                    ex,
                    "OnLeaseLostAsync failed for observer '{ObserverType}'",
                    observer.GetType().FullName);
            }
        }
    }
}