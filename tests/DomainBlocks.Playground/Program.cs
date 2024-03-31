using DomainBlocks.Abstractions;
using DomainBlocks.Persistence.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;

var settings = new PostgresStreamStoreSettings(
    "Server=localhost;Port=5434;Database=test-events;User Id=postgres;Password=postgres;");

var streamStore = new PostgresStreamStore(settings);
await streamStore.CreateSchemaIfNotExists();

var eventStore = new SqlStreamStoreEventStore(streamStore);
var subscription = eventStore.SubscribeToAll(new GlobalPosition(10));

await foreach (var message in subscription.ReadMessagesAsync())
{
    switch (message)
    {
        case StreamMessage.Event e:
            Console.WriteLine($"{e.ReadEvent.GlobalPosition}: {e.ReadEvent.Name}");
            break;
        case StreamMessage.CaughtUp:
            Console.WriteLine("Caught up");
            break;
        case StreamMessage.FellBehind:
            Console.WriteLine("Fell behind");
            break;
        case StreamMessage.SubscriptionDropped dropped:
            Console.WriteLine($"Subscription dropped: {dropped.Exception?.Message}");
            break;
    }
}