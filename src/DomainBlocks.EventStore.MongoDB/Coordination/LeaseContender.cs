using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaseContender(
    LeaseStore store,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    LeaderRunner leaderRunner,
    MongoEventStoreNodeOptions options,
    ILogger<LeaseContender> logger,
    TimeProvider? timeProvider = null) : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly CollectionNamespace _leasesNs = new(options.DatabaseName, options.LeasesCollectionName);

    private readonly Channel<LeaseUpdateKind> _leaseUpdates =
        Channel.CreateBounded<LeaseUpdateKind>(new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Lease contender starting");

        using var _ = changeStreamSubject.Attach(this);

        try
        {
            var waitFirst = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                var lease = await AcquireAsync(waitFirst, cancellationToken).ConfigureAwait(false);
                waitFirst = true;

                await using (lease.ConfigureAwait(false))
                {
                    logger.LogInformation("Became leader (epoch {Epoch})", lease.Epoch);

                    try
                    {
                        await leaderRunner.RunAsync(lease, cancellationToken).ConfigureAwait(false);

                        logger.LogWarning("Stepping down (epoch {Epoch}): unexpected exit", lease.Epoch);
                    }
                    catch (OperationCanceledException) when (lease.LeaseLostToken.IsCancellationRequested
                                                             && !cancellationToken.IsCancellationRequested)
                    {
                        var lostInfo = await lease.LeaseLostTask.ConfigureAwait(false);

                        logger.LogWarning(
                            "Stepping down (epoch {Epoch}): lease lost ({Reason})",
                            lease.Epoch,
                            lostInfo.Reason);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning("Stepping down (epoch {Epoch}): error", lease.Epoch);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            logger.LogInformation("Lease contender stopped");
        }
    }

    ValueTask IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>.OnNextAsync(
        ChangeStreamDocument<BsonDocument> change,
        CancellationToken cancellationToken)
    {
        if (!change.CollectionNamespace.Equals(_leasesNs) ||
            change.OperationType != ChangeStreamOperationType.Update ||
            change.DocumentKey["_id"].AsString != LeaseDocument.LeaseId)
        {
            return ValueTask.CompletedTask;
        }

        var updatedFields = change.UpdateDescription?.UpdatedFields;

        if (updatedFields is null ||
            !updatedFields.TryGetValue(LeaseDocument.FieldNames.LastUpdate, out var lastUpdateValue))
        {
            return ValueTask.CompletedTask;
        }

        var updateKind = Enum.Parse<LeaseUpdateKind>(lastUpdateValue[LeaseUpdate.FieldNames.Kind].AsString);
        return _leaseUpdates.Writer.WriteAsync(updateKind, cancellationToken);
    }

    private async Task<Lease> AcquireAsync(bool waitFirst, CancellationToken cancellationToken)
    {
        logger.LogInformation("Waiting to acquire lease");
        var clockSkewTolerance = TimeSpan.Zero;

        if (waitFirst)
        {
            var outcome = await WaitForOpportunityAsync(cancellationToken).ConfigureAwait(false);
            clockSkewTolerance = outcome == WaitOutcome.ReleaseObserved ? options.ClockSkewTolerance : TimeSpan.Zero;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Attempting to acquire lease");

            var doc = await store
                .AcquireAsync(Environment.MachineName, options.LeaseDuration(), clockSkewTolerance, cancellationToken)
                .ConfigureAwait(false);

            if (doc is not null)
            {
                logger.LogInformation(
                    "Lease acquired (epoch {Epoch}, commitPosition {CommitPosition})",
                    doc.Epoch,
                    doc.CommitPosition);

                return new Lease(
                    doc,
                    store,
                    options.LeaseDuration,
                    options.LeaseRenewInterval,
                    logger,
                    _timeProvider);
            }

            logger.LogDebug("Lease held elsewhere; watching change stream for opportunity");
            var outcome = await WaitForOpportunityAsync(cancellationToken).ConfigureAwait(false);

            // A releasing node sets ExpiresAtUtc = its local now. If its clock leads ours, the lease will appear
            // unexpired to us even though it has been released. ClockSkewTolerance widens the acquire filter to
            // (ExpiresAtUtc <= our now + tolerance), absorbing up to that much clock lead in the releasing node.
            clockSkewTolerance = outcome == WaitOutcome.ReleaseObserved ? options.ClockSkewTolerance : TimeSpan.Zero;
        }
    }

    private async Task<WaitOutcome> WaitForOpportunityAsync(CancellationToken cancellationToken)
    {
        var countdown = options.LeaseRenewInterval + options.ClockSkewTolerance;

        while (true)
        {
            using var countdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            countdownCts.CancelAfter(countdown + GetJitter());

            try
            {
                var updateKind = await _leaseUpdates.Reader.ReadAsync(countdownCts.Token).ConfigureAwait(false);

                switch (updateKind)
                {
                    case LeaseUpdateKind.Acquired:
                        logger.LogDebug("Lease acquired by another holder; resetting countdown");
                        break;
                    case LeaseUpdateKind.Renewed:
                        logger.LogDebug("Lease renewal observed; resetting countdown");
                        break;
                    case LeaseUpdateKind.Released:
                        logger.LogDebug("Lease release observed; attempting acquire");
                        await _timeProvider.Delay(GetJitter(), cancellationToken).ConfigureAwait(false);
                        return WaitOutcome.ReleaseObserved;
                }
            }
            catch (OperationCanceledException) when (countdownCts.IsCancellationRequested)
            {
                // Countdown completed without a renewal - the lease may be expiring.
                logger.LogDebug("Renewal not observed after {Countdown}; attempting acquire", countdown);
                return WaitOutcome.RenewalNotObserved;
            }
        }

        TimeSpan GetJitter() =>
            TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * options.MaxLeaseAcquireJitter.TotalMilliseconds);
    }

    private enum WaitOutcome
    {
        ReleaseObserved,
        RenewalNotObserved
    }
}