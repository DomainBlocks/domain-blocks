using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public sealed class Lease : ILease
{
    private const int NoScheduledPriority = int.MinValue;

    private LeaseState _leaseState;
    private readonly AcquireLeaseOptions _acquireOptions;
    private readonly ILeaseStore _leaseStore;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Task _heartbeatTask;

    private readonly CancellationTokenSource _leaseLostCts = new();

    private readonly TaskCompletionSource<LeaseLostInfo> _leaseLostTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _scheduledPriority = NoScheduledPriority;

    private int _disposed;

    public Lease(
        LeaseState leaseState,
        AcquireLeaseOptions acquireOptions,
        ILeaseStore leaseStore,
        ILogger logger,
        TimeProvider timeProvider)
    {
        ResourceId = leaseState.ResourceId;
        HolderId = leaseState.HolderId;
        Epoch = leaseState.Epoch;

        _leaseState = leaseState;
        _acquireOptions = acquireOptions;
        _leaseStore = leaseStore;
        _logger = logger;
        _timeProvider = timeProvider;

        // Intentionally not Task.Run: calling HeartbeatAsync() executes synchronously up to the first incomplete await.
        // This schedules the initial TimeProvider.Delay before returning, avoiding startup races in tests.
        _heartbeatTask = HeartbeatAsync();
    }

    public string ResourceId { get; }
    public string HolderId { get; }
    public long Epoch { get; }
    public int HolderPriority => Volatile.Read(ref _leaseState).HolderPriority;
    public DateTime UpdatedAtUtc => Volatile.Read(ref _leaseState).UpdatedAtUtc;
    public DateTime HeldSinceUtc => Volatile.Read(ref _leaseState).HeldSinceUtc;
    public DateTime ExpiresAtUtc => Volatile.Read(ref _leaseState).ExpiresAtUtc;
    public CancellationToken LeaseLostToken => _leaseLostCts.Token;
    public Task<LeaseLostInfo> LeaseLostTask => _leaseLostTcs.Task;

    public void ScheduleHolderPriorityChange(int priority)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        Interlocked.Exchange(ref _scheduledPriority, priority);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _logger.LogInformation(
            "Disposing lease for resource '{ResourceId}' held by '{HolderId}'",
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
                await _timeProvider.Delay(_acquireOptions.RenewInterval, _leaseLostCts.Token).ConfigureAwait(false);

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
        RenewLeaseOptions? renewOptions = null;

        var scheduledPriority = Volatile.Read(ref _scheduledPriority);
        if (scheduledPriority != NoScheduledPriority)
        {
            renewOptions = new RenewLeaseOptions
            {
                HolderPriority = scheduledPriority,
                Duration = _acquireOptions.Duration
            };
        }

        var state = await _leaseStore.RenewAsync(ResourceId, HolderId, Epoch, renewOptions, _leaseLostCts.Token);
        if (state is null)
            return false;

        // Clear scheduled priority after a successful renewal only if it matches the value we applied.
        if (scheduledPriority != NoScheduledPriority)
            Interlocked.CompareExchange(ref _scheduledPriority, NoScheduledPriority, scheduledPriority);

        Volatile.Write(ref _leaseState, state);

        _logger.LogDebug(
            "Holder '{HolderId}' renewed lease for resource '{ResourceId}'; " +
            "expires at {ExpiresAtUtc:yyyy-MM-ddTHH:mm:ssZ}",
            HolderId,
            ResourceId,
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
                    "Holder '{HolderId}' released lease for resource '{ResourceId}'",
                    HolderId,
                    ResourceId);
            }
            else
            {
                _logger.LogInformation(
                    "Holder '{HolderId}' unable to release lease for resource '{ResourceId}' (no longer held)",
                    HolderId,
                    ResourceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Holder '{HolderId}' failed to release lease for resource '{ResourceId}'",
                HolderId,
                ResourceId);
        }
    }

    private void LogLeaseLost(LeaseLostReason reason, Exception? exception = null)
    {
        _logger.Log(
            exception is null ? LogLevel.Information : LogLevel.Error,
            exception,
            "Holder '{HolderId}' lost lease for resource '{ResourceId}' (reason: {Reason})",
            HolderId,
            ResourceId,
            reason);
    }
}