using Microsoft.Extensions.Logging;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseClient(
    ILeaseStore leaseStore,
    ILogger<LeaseClient> logger,
    TimeProvider? timeProvider = null) :
    ILeaseClient
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AcquireLeaseResult<ILeaseHandle>> AcquireLeaseAsync(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = await AcquireLeaseCoreAsync(resourceId, options, cancellationToken);

        return context is not null
            ? new AcquireLeaseResult<ILeaseHandle>(new LeaseHandle(context), context.InitialSnapshot)
            : AcquireLeaseResult<ILeaseHandle>.NotAcquired;
    }

    public async Task<AcquireLeaseResult<ILeaseHandle<TState>>> AcquireLeaseAsync<TState>(
        string resourceId,
        AcquireLeaseOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var context = await AcquireLeaseCoreAsync(resourceId, options, cancellationToken);

        return context is not null
            ? new AcquireLeaseResult<ILeaseHandle<TState>>(new LeaseHandle<TState>(context), context.InitialSnapshot)
            : AcquireLeaseResult<ILeaseHandle<TState>>.NotAcquired;
    }

    private async Task<LeaseHandleContext?> AcquireLeaseCoreAsync(
        string resourceId,
        AcquireLeaseOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= AcquireLeaseOptions.Default;

        logger.LogInformation("Attempting to acquire lease for resource '{ResourceId}'", resourceId);

        var deadline = options.AcquireTimeout == Timeout.InfiniteTimeSpan
            ? DateTimeOffset.MaxValue
            : _timeProvider.GetUtcNow() + options.AcquireTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await leaseStore.AcquireAsync(resourceId, options, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Lease for resource '{ResourceId}' acquired by holder '{HolderId}'",
                    resourceId,
                    result.Snapshot.HolderId);

                return CreateHandleContext(result.Snapshot, options);
            }

            if (options.AcquireTimeout == TimeSpan.Zero)
            {
                logger.LogInformation("Failed to acquire lease for resource '{ResourceId}' (zero timeout)", resourceId);
                return null;
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                logger.LogInformation(
                    "Failed to acquire lease for resource '{ResourceId}' (timeout reached)",
                    resourceId);

                return null;
            }

            logger.LogDebug(
                "Lease for resource '{ResourceId}' is held elsewhere; retrying in {AcquireRetryDelay}",
                resourceId,
                options.AcquireRetryDelay);

            await _timeProvider.Delay(options.AcquireRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private LeaseHandleContext CreateHandleContext(ILeaseSnapshot snapshot, AcquireLeaseOptions options)
    {
        return new LeaseHandleContext
        {
            InitialSnapshot = snapshot,
            AcquireOptions = options,
            LeaseStore = leaseStore,
            Logger = logger,
            TimeProvider = _timeProvider
        };
    }
}