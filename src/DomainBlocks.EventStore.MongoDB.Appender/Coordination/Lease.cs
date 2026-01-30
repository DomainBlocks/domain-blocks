using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public sealed class Lease : ILease
{
    private readonly AcquireLeaseOptions _acquireOptions;
    private readonly ILeaseStore _leaseStore;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _leaseLostCts = new();

    private readonly TaskCompletionSource<LeaseLostInfo> _leaseLostTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Task _heartbeatTask;
    private int _disposed;

    public Lease(
        string resourceId,
        string holderId,
        long epoch,
        AcquireLeaseOptions acquireOptions,
        ILeaseStore leaseStore,
        ILogger logger,
        TimeProvider? timeProvider = null)
    {
        ResourceId = resourceId;
        HolderId = holderId;
        Epoch = epoch;
        HolderPriority = acquireOptions.HolderPriority;

        _acquireOptions = acquireOptions;
        _leaseStore = leaseStore;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    public string ResourceId { get; }
    public string HolderId { get; }
    public long Epoch { get; }

    public int HolderPriority
    {
        get => Volatile.Read(ref field);
        set => Interlocked.Exchange(ref field, value);
    }

    public CancellationToken LeaseLostToken => _leaseLostCts.Token;
    public Task<LeaseLostInfo> LeaseLostTask => _leaseLostTcs.Task;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _logger.LogInformation(
            "Disposing lease for resource {ResourceId} held by {HolderId}",
            ResourceId,
            HolderId);

        try
        {
            await _leaseLostCts.CancelAsync();
            await _heartbeatTask;
        }
        finally
        {
            _leaseLostCts.Dispose();
        }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (true)
            {
                _leaseLostCts.Token.ThrowIfCancellationRequested();

                await _timeProvider.Delay(_acquireOptions.RenewInterval, _leaseLostCts.Token);

                if (await TryRenewAsync())
                    continue;

                LogLeaseLost(LeaseLostReason.Revoked);
                await _leaseLostCts.CancelAsync();
                _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Revoked));

                return;
            }
        }
        catch (OperationCanceledException) when (_leaseLostCts.IsCancellationRequested)
        {
            await ReleaseLeaseAsync();
            LogLeaseLost(LeaseLostReason.Released);
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Released));
        }
        catch (Exception ex)
        {
            await ReleaseLeaseAsync();
            LogLeaseLost(LeaseLostReason.Error, ex);
            await _leaseLostCts.CancelAsync();
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Error, ex));
        }
    }

    private async Task<bool> TryRenewAsync()
    {
        var renewOptions = new RenewLeaseOptions
        {
            HolderPriority = HolderPriority,
            Duration = _acquireOptions.Duration
        };

        var state = await _leaseStore.RenewAsync(ResourceId, HolderId, Epoch, renewOptions, _leaseLostCts.Token);
        if (state is null)
            return false;

        _logger.LogDebug(
            "Lease for resource {ResourceId} renewed by holder {HolderId}; expires at {ExpiresAtUtc}",
            ResourceId,
            HolderId,
            state.ExpiresAtUtc);

        return true;
    }

    private async Task ReleaseLeaseAsync()
    {
        try
        {
            using var cts = _timeProvider.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

            var succeeded = await _leaseStore.TryReleaseAsync(ResourceId, HolderId, Epoch, cts.Token);
            if (succeeded)
            {
                _logger.LogInformation(
                    "Holder {HolderId} released lease for resource {ResourceId}",
                    HolderId,
                    ResourceId);
            }
            else
            {
                _logger.LogInformation(
                    "Holder {HolderId} unable to release lease for resource {ResourceId} (may no longer hold it)",
                    HolderId,
                    ResourceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Holder {HolderId} failed to release lease for resource {ResourceId}",
                HolderId,
                ResourceId);
        }
    }

    private void LogLeaseLost(LeaseLostReason reason, Exception? exception = null)
    {
        _logger.Log(
            exception is null ? LogLevel.Information : LogLevel.Error,
            exception,
            "Holder {HolderId} lost lease for resource {ResourceId} (reason: {Reason})",
            HolderId,
            ResourceId,
            reason);
    }
}