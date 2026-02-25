using System.Runtime.CompilerServices;
using DomainBlocks.Infrastructure.MongoDB.Utilities;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public class LeaseHandle : ILeaseHandle
{
    private ILeaseSnapshot _initialSnapshot;
    private readonly AcquireLeaseOptions _acquireOptions;
    private readonly ILeaseStore _leaseStore;
    private readonly RenewLeaseOptions _renewOptions;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Task _heartbeatTask;
    private readonly CancellationTokenSource _leaseLostCts = new();

    private readonly TaskCompletionSource<LeaseLostInfo> _leaseLostTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StrongBox<int>? _scheduledPriority;
    private int _disposed;

    public LeaseHandle(LeaseHandleContext context)
    {
        _initialSnapshot = context.InitialSnapshot;
        _acquireOptions = context.AcquireOptions;
        _leaseStore = context.LeaseStore;
        _renewOptions = new RenewLeaseOptions { Duration = context.AcquireOptions.Duration };
        _logger = context.Logger;
        _timeProvider = context.TimeProvider;
        _heartbeatTask = Task.Run(HeartbeatAsync);

        Claim = context.InitialSnapshot.Claim;
    }

    public LeaseClaim Claim { get; }
    public ILeaseSnapshot CurrentSnapshot => Volatile.Read(ref _initialSnapshot);
    public CancellationToken LeaseLostToken => _leaseLostCts.Token;
    public Task<LeaseLostInfo> LeaseLostTask => _leaseLostTcs.Task;

    public void ScheduleContentionPriorityChange(int priority)
    {
        Volatile.Write(ref _scheduledPriority, new StrongBox<int>(priority));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _logger.LogInformation(
            "Disposing lease for resource '{ResourceId}' held by '{HolderId}'",
            Claim.ResourceId,
            Claim.HolderId);

        using (_leaseLostCts)
        {
            await _leaseLostCts.CancelAsync().ConfigureAwait(false);
            await _heartbeatTask.ConfigureAwait(false);
        }
    }

    protected async Task<bool> TryUpdateStateAsync<TState>(
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default)
    {
        var result = await _leaseStore.UpdateStateAsync(Claim, updateState, cancellationToken);
        if (result.IsSuccess)
            Volatile.Write(ref _initialSnapshot, result.Snapshot);

        return result.IsSuccess;
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (true)
            {
                await _timeProvider.Delay(_acquireOptions.RenewInterval, _leaseLostCts.Token).ConfigureAwait(false);

                if (await TryRenewAsync().ConfigureAwait(false))
                    continue;

                LogLeaseLost(LeaseLostReason.Revoked);
                await _leaseLostCts.CancelAsync().ConfigureAwait(false);
                _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Revoked));

                return;
            }
        }
        catch (OperationCanceledException) when (_leaseLostCts.IsCancellationRequested)
        {
            await ReleaseLeaseAsync().ConfigureAwait(false);
            LogLeaseLost(LeaseLostReason.Released);
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Released));
        }
        catch (Exception ex)
        {
            await ReleaseLeaseAsync().ConfigureAwait(false);
            LogLeaseLost(LeaseLostReason.Error, ex);
            await _leaseLostCts.CancelAsync().ConfigureAwait(false);
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Error, ex));
        }
    }

    private async Task<bool> TryRenewAsync()
    {
        var priority = Volatile.Read(ref _scheduledPriority);
        var renewOptions = _renewOptions.With(x => x.ContentionPriority = priority?.Value);

        var result = await _leaseStore.RenewAsync(Claim, renewOptions, _leaseLostCts.Token).ConfigureAwait(false);
        if (!result.IsSuccess)
            return false;

        // Clear scheduled priority after a successful renewal only if it matches what we applied.
        if (priority is not null)
            Interlocked.CompareExchange(ref _scheduledPriority, null, priority);

        Volatile.Write(ref _initialSnapshot, result.Snapshot);

        _logger.LogDebug(
            "Holder '{HolderId}' renewed lease for resource '{ResourceId}'; " +
            "expires at {ExpiresAtUtc:yyyy-MM-ddTHH:mm:ssZ}",
            Claim.HolderId,
            Claim.ResourceId,
            result.Snapshot.ExpiresAt);

        return true;
    }

    private async Task ReleaseLeaseAsync()
    {
        try
        {
            using var cts = _timeProvider.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

            var result = await _leaseStore.ReleaseAsync(Claim, cts.Token).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                Volatile.Write(ref _initialSnapshot, result.Snapshot);

                _logger.LogInformation(
                    "Holder '{HolderId}' released lease for resource '{ResourceId}'",
                    Claim.HolderId,
                    Claim.ResourceId);
            }
            else
            {
                _logger.LogInformation(
                    "Holder '{HolderId}' unable to release lease for resource '{ResourceId}' (no longer held)",
                    Claim.HolderId,
                    Claim.ResourceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Holder '{HolderId}' failed to release lease for resource '{ResourceId}'",
                Claim.HolderId,
                Claim.ResourceId);
        }
    }

    private void LogLeaseLost(LeaseLostReason reason, Exception? exception = null)
    {
        _logger.Log(
            exception is null ? LogLevel.Information : LogLevel.Error,
            exception,
            "Holder '{HolderId}' lost lease for resource '{ResourceId}' (reason: {Reason})",
            Claim.HolderId,
            Claim.ResourceId,
            reason);
    }
}

public sealed class LeaseHandle<TState>(LeaseHandleContext context) : LeaseHandle(context), ILeaseHandle<TState>
{
    public Task<bool> TryUpdateStateAsync(
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default)
    {
        return base.TryUpdateStateAsync(updateState, cancellationToken);
    }
}