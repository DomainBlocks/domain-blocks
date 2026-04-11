using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class Lease : IAsyncDisposable
{
    private readonly string _holderId;
    private readonly LeaseStore _store;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _renewInterval;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Task _heartbeatTask;
    private readonly CancellationTokenSource _leaseLostCts = new();

    private readonly TaskCompletionSource<LeaseLostInfo> _leaseLostTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _disposed;

    public Lease(
        LeaseDocument document,
        LeaseStore store,
        TimeSpan duration,
        TimeSpan renewInterval,
        ILogger logger,
        TimeProvider timeProvider)
    {
        _holderId = document.HolderId;
        Epoch = document.Epoch;
        CommitPosition = document.CommitPosition;
        _store = store;
        _duration = duration;
        _renewInterval = renewInterval;
        _logger = logger;
        _timeProvider = timeProvider;
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    public long Epoch { get; }
    public long? CommitPosition { get; }
    public CancellationToken LeaseLostToken => _leaseLostCts.Token;
    public Task<LeaseLostInfo> LeaseLostTask => _leaseLostTcs.Task;

    public Task<bool> TryAdvanceCommitPositionAsync(long count, CancellationToken cancellationToken = default)
    {
        return _store.TryAdvanceCommitPositionAsync(_holderId, Epoch, count, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

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
                await _timeProvider.Delay(_renewInterval, _leaseLostCts.Token).ConfigureAwait(false);

                var renewed = await _store
                    .TryRenewAsync(_holderId, Epoch, _duration, _leaseLostCts.Token)
                    .ConfigureAwait(false);

                if (renewed)
                {
                    _logger.LogDebug("Lease renewed by '{HolderId}' (epoch {Epoch})", _holderId, Epoch);
                    continue;
                }

                _logger.LogInformation("Lease lost by '{HolderId}' (epoch {Epoch}): revoked", _holderId, Epoch);

                await _leaseLostCts.CancelAsync().ConfigureAwait(false);
                _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Revoked));
            }
        }
        catch (OperationCanceledException) when (_leaseLostCts.IsCancellationRequested)
        {
            await ReleaseAsync().ConfigureAwait(false);
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Released));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lease heartbeat failed for '{HolderId}' (epoch {Epoch})", _holderId, Epoch);
            await ReleaseAsync().ConfigureAwait(false);
            await _leaseLostCts.CancelAsync().ConfigureAwait(false);
            _leaseLostTcs.TrySetResult(new LeaseLostInfo(LeaseLostReason.Error, ex));
        }
    }

    private async Task ReleaseAsync()
    {
        try
        {
            using var cts = _timeProvider.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

            if (await _store.TryReleaseAsync(_holderId, Epoch, cts.Token).ConfigureAwait(false))
            {
                _logger.LogInformation("Log lease released by '{HolderId}' (epoch {Epoch})", _holderId, Epoch);
            }
            else
            {
                _logger.LogInformation(
                    "Holder '{HolderId}' (epoch {Epoch}) unable to release lease (no longer held)",
                    _holderId,
                    Epoch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release log lease for '{HolderId}' (epoch {Epoch})", _holderId, Epoch);
        }
    }
}