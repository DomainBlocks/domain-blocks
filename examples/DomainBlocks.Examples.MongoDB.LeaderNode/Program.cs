using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

var options = new MongoEventStoreNodeOptions
{
    DatabaseName = "domainblocks_examples",
    NodeRole = NodeRole.Leader
};

using var loggerFactory = LoggerFactory.Create(x => x
    .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
    .SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger("LeaderNode");

using var mongoClient = new MongoClient(MongoConnectionStrings.Default);

// Quit with Ctrl+C
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, options, cts.Token);

logger.LogInformation("Starting leader node");

// Create and start node
await using var node = new MongoEventStoreNode(mongoClient, options, loggerFactory);

try
{
    await node.StartAsync(cts.Token);
    await node.Completed.WaitAsync(cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    logger.LogInformation("Stopped");
}