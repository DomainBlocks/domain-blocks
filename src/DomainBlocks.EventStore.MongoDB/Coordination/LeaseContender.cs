using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaseContender(
    LeaseStore store,
    MongoEventStoreNodeOptions options,
    ILogger<LeaseContender> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task RunAsync(ILeaseHandler handler, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);

            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    await InvokeHandleLeaseAcquiredAsync(handler, lease, cancellationToken).ConfigureAwait(false);

                    var lostInfo = await lease.LeaseLostTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                    await InvokeHandleLeaseLostAsync(handler, lostInfo, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug("Lease contender cancelled");
                }
            }
        }
    }

    private async Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Waiting to acquire lease");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Attempting to acquire lease");

            var doc = await store
                .AcquireAsync(Environment.MachineName, options.LeaseDuration, cancellationToken)
                .ConfigureAwait(false);

            if (doc is not null)
            {
                logger.LogInformation(
                    "Lease acquired (epoch {Epoch}, commitPosition {CommitPosition})",
                    doc.Epoch, doc.CommitPosition);

                return new Lease(doc, store, options.LeaseDuration, options.LeaseRenewInterval, logger, _timeProvider);
            }

            logger.LogDebug("Lease held elsewhere; retrying in {Delay}", options.LeaseAcquireRetryDelay);
            await _timeProvider.Delay(options.LeaseAcquireRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InvokeHandleLeaseAcquiredAsync(
        ILeaseHandler handler,
        Lease lease,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleLeaseAcquiredAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"{nameof(ILeaseHandler.HandleLeaseAcquiredAsync)} invocation failed");
        }
    }

    private async Task InvokeHandleLeaseLostAsync(
        ILeaseHandler handler,
        LeaseLostInfo info,
        CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleLeaseLostAsync(info, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"{nameof(ILeaseHandler.HandleLeaseLostAsync)} invocation failed");
        }
    }
}