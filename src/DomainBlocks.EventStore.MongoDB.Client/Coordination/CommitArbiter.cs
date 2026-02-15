using System.Diagnostics;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Infrastructure.MongoDB.Sequences;
using Microsoft.Extensions.Logging;
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
        }
    }
}