using DomainBlocks.EventStore;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

var options = new MongoEventStoreNodeOptions
{
    DatabaseName = "domainblocks_examples",
    NodeRole = NodeRole.Client
};

using var loggerFactory = LoggerFactory.Create(x => x
    .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
    .SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger("ClientNode");

using var mongoClient = new MongoClient(MongoConnectionStrings.Default);

// Quit with Ctrl+C
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, options, cts.Token);

// Create and start node
await using var node = new MongoEventStoreNode(mongoClient, options, loggerFactory);
await node.StartAsync(cts.Token);

// Create client
var eventTypeMap = EventTypeMap.Create(x => x.MapType<ExampleEvent>());
var eventCodec = MongoTestEventCodec.Create<IDomainEvent>(eventTypeMap);
var client = node.CreateClient(eventCodec);

// Append an event every second
var streamId = $"example-{Guid.NewGuid():N}";
var counter = 0;

logger.LogInformation("Starting event loop on stream {StreamId}. Press Ctrl+C to stop.", streamId);

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var @event = new ExampleEvent(counter++, DateTimeOffset.UtcNow);

        await client.AppendToStreamAsync(streamId, [@event], cancellationToken: cts.Token);

        logger.LogInformation("Appended event #{Counter} at {Timestamp}", counter, @event.Timestamp);

        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    logger.LogInformation("Stopped.");
}

internal interface IDomainEvent;

internal record ExampleEvent(int Counter, DateTimeOffset Timestamp) : IDomainEvent;