using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using DomainBlocks.Infrastructure.MongoDB.Sequences;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.Coordination;

public class CommitArbiterTests
{
    private MongoClient _mongoClient = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }

    [Test]
    public async Task Test()
    {
        var db = _mongoClient.GetDatabase("domainblocks");
        var leaseStates = db.GetCollection<LeaseState>("dbx_leases");
        var leaseStore = new LeaseStore(leaseStates);

        using var loggerFactory = LoggerFactory.Create(x => x
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        var leaseProvider = new LeaseProvider(leaseStore, loggerFactory.CreateLogger<LeaseProvider>());
        var sequences = db.GetCollection<BsonDocument>("dbx_sequences");
        var sequenceAllocator = new SequenceStore(sequences);

        var commitArbiter = new CommitArbiter(
            _mongoClient,
            leaseProvider,
            sequenceAllocator,
            loggerFactory.CreateLogger<CommitArbiter>());

        await commitArbiter.RunAsync();
    }
}