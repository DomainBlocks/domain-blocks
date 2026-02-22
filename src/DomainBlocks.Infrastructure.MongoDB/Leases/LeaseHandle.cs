using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed class LeaseHandle : ILeaseHandle
{
    private LeaseDocument _leaseDocument;
    private readonly AcquireLeaseOptions _acquireOptions;
    private readonly ILeaseManager _leaseManager;
    private readonly RenewLeaseOptions _renewOptions;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Task _heartbeatTask;
    private readonly CancellationTokenSource _leaseLostCts = new();

    private readonly TaskCompletionSource<LeaseLostInfo> _leaseLostTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StrongBox<int>? _scheduledPriority;
    private int _disposed;

    public LeaseHandle(
        LeaseDocument leaseDocument,
        AcquireLeaseOptions acquireOptions,
        ILeaseManager leaseManager,
        ILogger logger,
        TimeProvider timeProvider)
    {
        Claim = leaseDocument.Claim;

        _leaseDocument = leaseDocument;
        _acquireOptions = acquireOptions;
        _leaseManager = leaseManager;
        _renewOptions = new RenewLeaseOptions { Duration = acquireOptions.Duration };
        _logger = logger;
        _timeProvider = timeProvider;
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    public LeaseClaim Claim { get; }
    public int ContentionPriority => Volatile.Read(ref _leaseDocument).ContentionPriority;
    public DateTimeOffset HeldSince => Volatile.Read(ref _leaseDocument).HeldSinceUtc;
    public DateTimeOffset ExpiresAt => Volatile.Read(ref _leaseDocument).ExpiresAtUtc;
    public DateTimeOffset UpdatedAt => Volatile.Read(ref _leaseDocument).LastUpdatedAtUtc;
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

        var state = await _leaseManager.RenewAsync(Claim, renewOptions, _leaseLostCts.Token).ConfigureAwait(false);
        if (state is null)
            return false;

        // Clear scheduled priority after a successful renewal only if it matches what we applied.
        if (priority is not null)
            Interlocked.CompareExchange(ref _scheduledPriority, null, priority);

        Volatile.Write(ref _leaseDocument, state);

        _logger.LogDebug(
            "Holder '{HolderId}' renewed lease for resource '{ResourceId}'; " +
            "expires at {ExpiresAtUtc:yyyy-MM-ddTHH:mm:ssZ}",
            Claim.HolderId,
            Claim.ResourceId,
            state.ExpiresAtUtc);

        return true;
    }

    private async Task ReleaseLeaseAsync()
    {
        try
        {
            using var cts = _timeProvider.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

            var succeeded = await _leaseManager.TryReleaseAsync(Claim, cts.Token).ConfigureAwait(false);
            if (succeeded)
            {
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

public sealed class LeaseHandle<TState>(ILeaseHandle inner, ILeaseManager leaseManager) : ILeaseHandle<TState>
{
    public LeaseClaim Claim => inner.Claim;
    public int ContentionPriority => inner.ContentionPriority;
    public DateTimeOffset HeldSince => inner.HeldSince;
    public DateTimeOffset ExpiresAt => inner.ExpiresAt;
    public DateTimeOffset UpdatedAt => inner.UpdatedAt;
    public CancellationToken LeaseLostToken => inner.LeaseLostToken;
    public Task<LeaseLostInfo> LeaseLostTask => inner.LeaseLostTask;

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    public void ScheduleContentionPriorityChange(int priority) => inner.ScheduleContentionPriorityChange(priority);

    public Task<bool> TryUpdateStateAsync(
        Action<IScopedUpdateBuilder<TState>> updateState,
        CancellationToken cancellationToken = default)
    {
        return leaseManager.TryUpdateStateAsync(inner.Claim, updateState, cancellationToken);
    }
}