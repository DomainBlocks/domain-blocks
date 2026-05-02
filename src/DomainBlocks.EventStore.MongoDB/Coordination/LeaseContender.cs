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

    // Signals from the change stream when the lease is explicitly released.
    private readonly Channel<bool> _releaseSignal =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
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
                        logger.LogWarning("Stepping down (epoch {Epoch}): lease lost ({Reason})",
                            lease.Epoch, lostInfo.Reason);
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

        if (updateKind == LeaseUpdateKind.Released)
            _releaseSignal.Writer.TryWrite(true);

        return ValueTask.CompletedTask;
    }

    private async Task<Lease> AcquireAsync(bool waitFirst, CancellationToken cancellationToken)
    {
        logger.LogInformation("Waiting to acquire lease");
        var clockSkewTolerance = TimeSpan.Zero;

        if (waitFirst)
            clockSkewTolerance = await WaitForOpportunityAsync(cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Attempting to acquire lease");

            var doc = await store
                .AcquireAsync(Environment.MachineName, options.LeaseDuration(), clockSkewTolerance, cancellationToken)
                .ConfigureAwait(false);

            if (doc is not null)
            {
                logger.LogInformation("Lease acquired (epoch {Epoch}, commitPosition {CommitPosition})",
                    doc.Epoch, doc.CommitPosition);

                return new Lease(doc, store, options.LeaseDuration, options.LeaseRenewInterval, logger, _timeProvider);
            }

            logger.LogDebug("Lease held elsewhere; polling for opportunity");
            clockSkewTolerance = await WaitForOpportunityAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Returns ClockSkewTolerance if an explicit release was observed, zero otherwise.
    private async Task<TimeSpan> WaitForOpportunityAsync(CancellationToken cancellationToken)
    {
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pollCts.CancelAfter(options.LeaseRenewInterval + GetJitter());

        try
        {
            await _releaseSignal.Reader.ReadAsync(pollCts.Token).ConfigureAwait(false);
            logger.LogDebug("Lease release observed; attempting acquire");
            await _timeProvider.Delay(GetJitter(), cancellationToken).ConfigureAwait(false);
            return options.ClockSkewTolerance;
        }
        catch (OperationCanceledException) when (pollCts.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Poll interval elapsed; attempting acquire");
            return TimeSpan.Zero;
        }
    }

    private TimeSpan GetJitter() =>
        TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * options.MaxLeaseAcquireJitter.TotalMilliseconds);
}