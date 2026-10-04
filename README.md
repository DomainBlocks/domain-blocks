# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Work in progress:** The API and functionality may change as the project matures.

## Features

DomainBlocks is a set of NuGet packages, so you reference only what you need. Event storage and event evolution are the
first feature areas; others will follow.

### Event storage

`DomainBlocks.EventStore` holds the store contracts and the append/read pipeline; a store package plugs a database in.
Add `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, build a store, then append and read
events:

```csharp
await using var store = new PostgresEventStoreBuilder<IDomainEvent>()
    .UseConnectionString(connectionString)
    .ConfigureOptions(o => o.Schema = "events")
    .ConfigureCodec(c => c.MapEvent<OrderPlaced>())
    .Build();

await store.EnsureInitializedAsync();

await store.AppendAsync("order-1", [new OrderPlaced(...)]);

await foreach (var e in store.ReadStream("order-1"))
    Console.WriteLine($"{e.Context.StreamPosition}: {e.Payload}");
```

Subscribe to catch up on existing events and then keep receiving new ones as they are appended:

```csharp
await foreach (var message in store.SubscribeToAll(SubscriptionOrigin.Start))
{
    switch (message)
    {
        case { Event: { } e }:
            Console.WriteLine($"{e.Context.StreamId}: {e.Payload}");
            break;
        case { IsCaughtUp: true }:
            Console.WriteLine("Caught up; now receiving live events");
            break;
    }
}
```

The default origin is the end of the log, so omitting it receives only new events. `SubscribeToStream` works the same
way for a single event stream. A subscriber that consumes too slowly receives a message with `IsFellBehind` set to
`true`, then catches up again.

### Event evolution

Stored events are never rewritten. Instead, the event type map and read transforms reshape old events as they are read,
on ordinary reads and subscriptions alike, so the rest of the code only sees current shapes.

Upcast an event that gained a field. `AddRead` keeps the previous version readable under its stored name, and `Add` maps
the current version for both reading and writing:

```csharp
var typeMap = new EventTypeMapBuilder()
    .AddRead<OrderPlacedV1>("OrderPlaced") // previous version already stored
    .Add<OrderPlaced>("OrderPlacedV2")
    .Build();

var builder = new PostgresEventStoreBuilder<IDomainEvent>()
    .ConfigureCodec(c => c.UseEventTypeMap(typeMap))
    .AddReadTransform((OrderPlacedV1 e) => new OrderPlaced(e.OrderId, e.Total, Currency: "GBP"));
```

Split an event that recorded two things at once:

```csharp
builder.AddReadTransform((TradeExecutedV1 e) =>
[
    new TradeExecuted(e.TradeId, e.Commodity, e.Quantity, e.Price),
    new BrokerFeeAccrued(e.TradeId, e.BrokerFee)
]);
```

Rename an event by also reading its old stored name:

```csharp
new EventTypeMapBuilder()
    .Add<TradeBooked>()
    .AddRead<TradeBooked>("TradeCreated");
```

Ignore events by mapping their stored names to a placeholder instance, or by returning it from a transform. Returning a
placeholder event allows the event's stream position to still be observed - important for optimistic concurrency.

```csharp
var typeMap = new EventTypeMapBuilder()
    .Add<TradeBooked>()
    .AddRead<TradeBookedV1>()
    .AddRead(Ignored.Instance, "TradeNoteAdded", "TradeNoteRemoved")
    .Build();

builder.AddReadTransform((TradeBookedV1 e) => e.IsTest
    ? Ignored.Instance
    : new TradeBooked(e.TradeId, e.Quantity));

public sealed record Ignored : IDomainEvent
{
    public static readonly Ignored Instance = new();
}
```

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT.