using System.Diagnostics;
using DomainBlocks.EventStore.Primitives.Identity;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public class CommitArbiter(
    IMongoClient mongoClient,
    ILeaseProvider leaseProvider,
    ILeaseFencer leaseFencer,
    ISequenceAllocator sequenceAllocator,
    EventStoreCollectionOptions collectionOptions,
    ILogger<CommitArbiter> logger)
{
    private const string LeaseResourceId = "$dbx.sys.commit_arbiter";
    private const string GlobalPositionSequenceId = "$dbx.sys.global_position";
    private const string LeadershipStreamId = "$dbx.sys/coord/leadership";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var eventsCollection = db.GetCollection<Schema.EventDocument2>(collectionOptions.EventsCollectionName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var lease = await leaseProvider.AcquireLeaseAsync(
                LeaseResourceId,
                new AcquireLeaseOptions
                {
                    AcquireTimeout = Timeout.InfiniteTimeSpan
                },
                cancellationToken);

            if (lease is null)
                throw new UnreachableException("Expected to obtain lease with AcquireTimeout=InfiniteTimeSpan.");

            var globalPositionAllocation =
                await sequenceAllocator.AllocateNextAsync(GlobalPositionSequenceId, 1, cancellationToken);

            var txnOptions = new TransactionOptions(
                readConcern: ReadConcern.Majority,
                readPreference: ReadPreference.Primary,
                writeConcern: WriteConcern.WMajority,
                maxCommitTime: TimeSpan.FromSeconds(5));

            using var session = await mongoClient.StartSessionAsync(
                new ClientSessionOptions { CausalConsistency = true },
                cancellationToken);

            var success = await session.WithTransactionAsync(
                async (s, ct) =>
                {
                    try
                    {
                        var isStillLeader = await leaseFencer.TryFenceAsync(
                            s,
                            lease.ResourceId,
                            lease.HolderId,
                            lease.Epoch,
                            ct);

                        if (!isStillLeader)
                            return false;

                        var leaderElected = new Schema.LeaderElected
                        {
                            HolderId = lease.HolderId,
                            Epoch = lease.Epoch,
                            GlobalPosition = globalPositionAllocation.Start
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

                        await eventsCollection.InsertOneAsync(s, eventDocument, cancellationToken: ct);

                        return true;
                    }
                    catch (MongoWriteException ex)
                    {
                        throw;
                    }
                },
                txnOptions,
                cancellationToken);

            if (success)
                break;
        }
    }
}