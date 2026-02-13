using System.Diagnostics;
using DomainBlocks.EventStore.Primitives.Identity;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Infrastructure.MongoDB.Sequences;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public class CommitArbiter
{
    private const string LeaseResourceId = "$dbx.sys.commit_arbiter";
    private const string GlobalPositionSequenceId = "$dbx.sys.global_position";
    private const string LeadershipStreamId = "$dbx.sys/coord/leadership";

    private readonly IMongoClient _mongoClient;
    private readonly ILeaseProvider _leaseProvider;
    private readonly ISequenceStore _sequenceStore;
    private readonly IMongoCollection<EventDocument2> _eventsCollection;
    private readonly ILogger<CommitArbiter> _logger;

    public CommitArbiter(
        IMongoClient mongoClient,
        ILeaseProvider leaseProvider,
        ISequenceStore sequenceStore,
        ILogger<CommitArbiter> logger)
    {
        var db = mongoClient.GetDatabase("domainblocks");

        _mongoClient = mongoClient;
        _leaseProvider = leaseProvider;
        _sequenceStore = sequenceStore;
        _eventsCollection = db.GetCollection<EventDocument2>("dbx_events");
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var lease = await _leaseProvider.AcquireLeaseAsync(
                LeaseResourceId,
                new AcquireLeaseOptions
                {
                    Duration = TimeSpan.FromSeconds(10),
                    MinTenure = TimeSpan.FromSeconds(2),
                    AcquireTimeout = Timeout.InfiniteTimeSpan,
                    AcquireRetryDelay = TimeSpan.FromSeconds(1),
                    RenewInterval = TimeSpan.FromSeconds(3)
                },
                cancellationToken);

            if (!lease.IsAcquired)
                throw new UnreachableException("Expected to obtain lease with AcquireTimeout=InfiniteTimeSpan.");

            var isLeader = await TryEmitLeaderElectedAsync(lease.Handle, cancellationToken);
            if (!isLeader)
                continue;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lease.Handle.LeaseLostToken);

            await RunAsLeaderAsync(lease.Handle.Token.Epoch, linkedCts.Token);
        }
    }

    private async Task<bool> TryEmitLeaderElectedAsync(ILeaseHandle lease, CancellationToken cancellationToken)
    {
        var txnOptions = new TransactionOptions(
            ReadConcern.Snapshot,
            ReadPreference.Primary,
            WriteConcern.WMajority);

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        return await session.WithTransactionAsync(
            async (s, ct) =>
            {
                var isLeader = await lease.TryFenceAsync(s, ct);
                if (!isLeader)
                    return false;

                var globalPosition = await _sequenceStore.NextAsync(s, GlobalPositionSequenceId, ct);

                var leaderElected = new SystemEvents.LeaderElected
                {
                    HolderId = lease.Token.HolderId,
                    Epoch = lease.Token.Epoch,
                    GlobalPosition = globalPosition
                };

                var commitId = Guid.NewGuid();

                var eventDocument = new EventDocument2
                {
                    StreamId = LeadershipStreamId,
                    CommitId = commitId,
                    CommitIndex = 0,
                    EventId = EventIdGenerator.Generate(LeadershipStreamId, commitId, 0),
                    EventName = SystemEvents.LeaderElected.Name,
                    EventData = leaderElected.ToBsonDocument(),
                    Metadata = BsonNull.Value,
                    CreatedAtUtc = DateTime.UtcNow
                };

                await _eventsCollection.InsertOneAsync(s, eventDocument, cancellationToken: ct);

                return true;
            },
            txnOptions,
            cancellationToken);
    }

    private async Task RunAsLeaderAsync(long epoch, CancellationToken cancellationToken)
    {
        await using var subscription = _eventsCollection.SubscribeToChangeStream();

        // Or establish an anchor rather than buffer everything
        await subscription.WaitUntilLiveAsync(cancellationToken);

        // Catch up
        // - Figure out all pending requests
        // - Build up state about pending requests by commit ID
        // - Goal: all commits eventually have a terminal event (?)

        // Requirements per commit
        // Per-stream OCC

        await subscription.ForEachAsync(
            (doc, ct) => { return default; },
            cancellationToken);
    }
}