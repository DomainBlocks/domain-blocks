using DomainBlocks.EventStore;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

var role = SelectRole(args);

var options = new MongoEventStoreNodeOptions
{
    DatabaseName = "domainblocks_examples",
    NodeRole = role
};

using var loggerFactory = LoggerFactory.Create(x => x
    .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
    .SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger(role.ToString());

using var mongoClient = new MongoClient(MongoConnectionStrings.Default);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, options, cts.Token);

await using var node = new MongoEventStoreNode(mongoClient, options, loggerFactory);
await node.StartAsync(cts.Token);

var eventTypeMap = EventTypeMap.Create(x => x.MapType<ExampleEvent>());
var eventCodec = MongoTestEventCodec.Create<IDomainEvent>(eventTypeMap);
var client = node.CreateClient(eventCodec);

var streamId = $"example-{Guid.NewGuid():N}";
var counter = 0;

logger.LogInformation("Writing events on stream {StreamId}. Press Ctrl+C to stop.", streamId);

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var @event = new ExampleEvent(counter++, DateTimeOffset.UtcNow);

        await client.AppendToStreamAsync(streamId, [@event], cancellationToken: cts.Token);

        logger.LogInformation("Appended event #{Counter}", counter);

        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    logger.LogInformation("Stopped");
}

static NodeRole SelectRole(string[] args)
{
    if (args.Length > 0)
    {
        return args[0].ToLowerInvariant() switch
        {
            "client" => NodeRole.Client,
            "client-leader" => NodeRole.ClientLeader,
            var unknown => throw new ArgumentException(
                $"Unknown role '{unknown}'. Valid options: client, client-leader")
        };
    }

    Console.WriteLine();
    Console.WriteLine("Select node role:");
    Console.WriteLine("  1  Client");
    Console.WriteLine("  2  Client-Leader");
    Console.WriteLine();
    Console.Write("Choice: ");

    return Console.ReadLine()?.Trim() switch
    {
        "1" => NodeRole.Client,
        "2" => NodeRole.ClientLeader,
        var other => throw new InvalidOperationException($"Invalid choice '{other}'.")
    };
}

internal interface IDomainEvent;

internal record ExampleEvent(int Counter, DateTimeOffset Timestamp) : IDomainEvent;