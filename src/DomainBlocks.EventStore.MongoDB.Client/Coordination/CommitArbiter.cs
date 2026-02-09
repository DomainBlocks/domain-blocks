using System.Diagnostics;
using DomainBlocks.EventStore.Primitives.Identity;
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
    private readonly ISequenceAllocator _sequenceAllocator;
    private readonly IMongoCollection<Schema.EventDocument2> _eventsCollection;
    private readonly ILogger<CommitArbiter> _logger;

    public CommitArbiter(
        IMongoClient mongoClient,
        ILeaseProvider leaseProvider,
        ISequenceAllocator sequenceAllocator,
        ILogger<CommitArbiter> logger)
    {
        var db = mongoClient.GetDatabase("domainblocks");

        _mongoClient = mongoClient;
        _leaseProvider = leaseProvider;
        _sequenceAllocator = sequenceAllocator;
        _eventsCollection = db.GetCollection<Schema.EventDocument2>("dbx_events");
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
                    AcquireTimeout = Timeout.InfiniteTimeSpan
                },
                cancellationToken);

            if (!lease.IsAcquired)
                throw new UnreachableException("Expected to obtain lease with AcquireTimeout=InfiniteTimeSpan.");

            var isLeader = await TryEmitLeaderElectedAsync(lease.Handle, cancellationToken);
            if (!isLeader)
                continue;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lease.Handle.LeaseLostToken);

            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
        }
    }

    private async Task<bool> TryEmitLeaderElectedAsync(ILeaseHandle lease, CancellationToken cancellationToken)
    {
        var txnOptions = new TransactionOptions(
            ReadConcern.Majority,
            ReadPreference.Primary,
            WriteConcern.WMajority);

        using var session = await _mongoClient.StartSessionAsync(
            new ClientSessionOptions { CausalConsistency = true },
            cancellationToken);

        return await session.WithTransactionAsync(
            async (s, ct) =>
            {
                var isLeader = await lease.TryFenceAsync(s, ct);
                if (!isLeader)
                {
                    await s.AbortTransactionAsync(ct);
                    return false;
                }

                var globalPosition = await _sequenceAllocator.AllocateNextAsync(s, GlobalPositionSequenceId, ct);

                var leaderElected = new Schema.LeaderElected
                {
                    HolderId = lease.HolderId,
                    Epoch = lease.Epoch,
                    GlobalPosition = globalPosition
                };

                var commitId = Guid.NewGuid();

                var eventDocument = new Schema.EventDocument2
                {
                    StreamId = LeadershipStreamId,
                    CommitId = commitId,
                    CommitIndex = 0,
                    EventId = EventIdGenerator.Generate(LeadershipStreamId, commitId, 0),
                    EventName = "$dbx.sys.LeaderElected",
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
}