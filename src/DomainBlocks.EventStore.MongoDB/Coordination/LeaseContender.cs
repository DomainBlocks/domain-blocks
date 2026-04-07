using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class LeaseContender(ILeaseClient leaseClient, ILogger<LeaseContender> logger)
{
    public const string ResourceId = "dbx_event_log_lease";

    public async Task RunAsync(ILeaseHandler handler, CancellationToken cancellationToken = default)
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
                await NotifyLeaseAcquiredAsync(handler, handle, cancellationToken).ConfigureAwait(false);

                try
                {
                    var leaseLostInfo = await handle.LeaseLostTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                    await NotifyLeaseLostAsync(handler, handle.Claim, leaseLostInfo, cancellationToken)
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
        ILeaseHandler handler,
        ILeaseHandle<LeaseState> handle,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleLeaseAcquiredAsync(handle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking OnLeaseAcquiredAsync");
        }
    }

    private async Task NotifyLeaseLostAsync(
        ILeaseHandler handler,
        LeaseClaim leaseClaim,
        LeaseLostInfo? leaseLostInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleLeaseLostAsync(leaseClaim, leaseLostInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking NotifyLeaseLostAsync");
        }
    }
}