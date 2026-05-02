using DomainBlocks.Testing.Integration;
using MongoDB.Bson;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClient2BenchmarkTests : EventStoreClientBenchmarkTests
{
    protected override Task<ITestEventStoreClientFactory<object>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default)
    {
        var options = new MongoEventStoreClientOptions2 { DatabaseName = "domainblocks_benchmark_tests_v2" };
        var clientFactory = TestMongoEventStoreClient2Factory.CreateDefault(options);
        return Task.FromResult<ITestEventStoreClientFactory<object>>(clientFactory);
    }

    protected override async Task OnThroughputTestCompletedAsync(CancellationToken cancellationToken)
    {
        var clientFactory = (TestMongoEventStoreClient2Factory<object>)ClientFactory;
        var mongoClient = clientFactory.MongoClient;

        var admin = mongoClient.GetDatabase("admin");

        var result = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument("currentOp", 1),
            cancellationToken: cancellationToken);

        var inprog = result["inprog"].AsBsonArray.Select(x => x.AsBsonDocument).ToList();

        var inFlightTransactions = inprog
            .Where(x => x.Contains("transaction"))
            .ToList();

        var openCursors = inprog
            .Where(x => x.Contains("cursor"))
            .ToList();

        await TestContext.Out.WriteLineAsync();
        await TestContext.Out.WriteLineAsync($"In-flight transactions: {inFlightTransactions.Count}");
        await TestContext.Out.WriteLineAsync($"Open cursors:           {openCursors.Count}");

        foreach (var cursor in openCursors)
        {
            var ns = cursor.GetValue("ns", "unknown").AsString;
            await TestContext.Out.WriteLineAsync($"  cursor on: {ns}");
        }
    }
}