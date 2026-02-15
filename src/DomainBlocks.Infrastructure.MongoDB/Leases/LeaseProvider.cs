using Microsoft.Extensions.Logging;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseProvider(
    ILeaseStore leaseStore,
    ILogger<LeaseProvider> logger,
    TimeProvider? timeProvider = null) : ILeaseProvider
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LeaseAcquisition> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AcquireLeaseOptions.Default;

        logger.LogInformation("Attempting to acquire lease for resource '{ResourceId}'", resourceId);

        var deadline = options.AcquireTimeout == Timeout.InfiniteTimeSpan
            ? DateTimeOffset.MaxValue
            : _timeProvider.GetUtcNow() + options.AcquireTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handle = await AcquireOnceAsync(resourceId, options, cancellationToken).ConfigureAwait(false);
            if (handle is not null)
            {
                logger.LogInformation(
                    "Lease for resource '{ResourceId}' acquired by holder '{HolderId}'",
                    resourceId,
                    handle.Token.HolderId);

                return LeaseAcquisition.From(handle);
            }

            if (options.AcquireTimeout == TimeSpan.Zero)
            {
                logger.LogInformation("Failed to acquire lease for resource '{ResourceId}' (zero timeout)", resourceId);
                return LeaseAcquisition.NotAcquired;
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                logger.LogInformation(
                    "Failed to acquire lease for resource '{ResourceId}' (timeout reached)",
                    resourceId);

                return LeaseAcquisition.NotAcquired;
            }

            logger.LogDebug(
                "Lease for resource '{ResourceId}' is held elsewhere; retrying in {AcquireRetryDelay}",
                resourceId,
                options.AcquireRetryDelay);

            await _timeProvider.Delay(options.AcquireRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ILeaseHandle?> AcquireOnceAsync(
        string resourceId,
        AcquireLeaseOptions options,
        CancellationToken cancellationToken)
    {
        var state = await leaseStore.AcquireAsync(resourceId, options, cancellationToken).ConfigureAwait(false);
        return state is not null ? new LeaseHandle(state, options, leaseStore, logger, _timeProvider) : null;
    }
}