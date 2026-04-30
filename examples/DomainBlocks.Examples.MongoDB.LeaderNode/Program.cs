using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

var chaosMode = SelectChaosMode(args);

var options = new MongoEventStoreNodeOptions
{
    DatabaseName = "domainblocks_examples",
    NodeRole = NodeRole.Leader,
    LeaseDuration = chaosMode
        ? () => Random.Shared.NextDouble() < 0.5 ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(6)
        : () => TimeSpan.FromSeconds(6)
};

using var loggerFactory = LoggerFactory.Create(x => x
    .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
    .SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger("LeaderNode");

using var mongoClient = new MongoClient(TestMongoConnectionStrings.Default);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, options, cts.Token);

logger.LogInformation("Starting leader node (chaos mode: {ChaosMode})", chaosMode);

await using var node = new MongoEventStoreNode(mongoClient, options, loggerFactory);

try
{
    await node.StartAsync(cts.Token);
    await node.Completion.WaitAsync(cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    logger.LogInformation("Stopped");
}

static bool SelectChaosMode(string[] args)
{
    if (args.Length > 0)
    {
        return args[0].ToLowerInvariant() switch
        {
            "chaos" => true,
            "normal" => false,
            var other => throw new ArgumentException($"Unknown mode '{other}'. Valid options: normal, chaos")
        };
    }

    Console.WriteLine();
    Console.WriteLine("Select mode:");
    Console.WriteLine("  1  Normal");
    Console.WriteLine("  2  Chaos (randomly expires lease to trigger failover)");
    Console.WriteLine();
    Console.Write("Choice: ");

    return Console.ReadLine()?.Trim() switch
    {
        "1" => false,
        "2" => true,
        var other => throw new InvalidOperationException($"Invalid choice '{other}'.")
    };
}