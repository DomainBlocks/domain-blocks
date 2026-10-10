# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Work in progress:** The API and functionality may change as the project matures.

## Features

DomainBlocks is a set of NuGet packages, so you reference only what you need. Event storage, event filtering, and event
evolution are the first feature areas; others will follow.

### Event store

The event store is built to be trusted as the system of record:

- **Atomic appends.** The events in a given append operation either all commit or none do, and they are contiguous in
  the stream.
- **Optimistic concurrency.** Pass the stream state you expect, and the append is rejected with
  `StreamAppendConflictException` if another writer got there first. The exception carries the state that was
  observed, so the caller can reload and retry.
- **Idempotent appends.** Give an append a commit id and a retry of the same request succeeds without writing again,
  even if the first attempt committed but its reply was lost (e.g. due to a timeout).
- **One global log in commit order.** Positions are assigned at commit time, without gaps, so a replay from the start
  and a live subscription see the same events in the same order.
- **Subscriptions never skip or repeat.** A subscriber that cannot keep up is told it fell behind and resumes from its
  last delivered position; no event is dropped silently.

`DomainBlocks.EventStore` holds the store contracts and the append/read pipeline; a store package plugs a database in.
Add `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, build a store, then append and read
events:

```csharp
await using var store = new PostgresEventStoreBuilder<IDomainEvent>() // Or MongoEventStoreBuilder
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

### Event filtering

Pass an `EventFilter` to a read to select events by name, stream, metadata, or creation time:

```csharp
var options = new ReadAllOptions { Filter = EventFilter.EventNames("OrderPlaced", "OrderShipped") };

await foreach (var e in store.ReadAll(options: options))
    Console.WriteLine($"{e.Context.StreamId}: {e.Payload}");
```

These are the filters to build from:

```csharp
EventFilter.EventNames("OrderPlaced", "OrderShipped")
EventFilter.StreamIds("order-1", "order-2")
EventFilter.StreamIdStartsWith("order-")
EventFilter.MetadataExists("tenant")
EventFilter.Metadata("tenant", "acme", "initech")
EventFilter.CreatedAtOrAfter(dateTime)
EventFilter.CreatedBefore(dateTime)
```

Combine them with `&`, `|`, and `!`:

```csharp
var filter =
    EventFilter.StreamIdStartsWith("order-") &
    EventFilter.Metadata("tenant", "acme") &
    !EventFilter.EventNames("OrderNoteAdded");
```

A subscription takes a filter in the same way, and applies it both while catching up and to new events:

```csharp
var options = new SubscriptionOptions { Filter = filter };

await foreach (var message in store.SubscribeToAll(SubscriptionOrigin.Start, options))
{
    if (message.Event is { } e)
        Console.WriteLine($"{e.Context.StreamId}: {e.Payload}");
}
```

Names and values match exactly, and event names are the names that events are stored under. Events that a filter
excludes are never decoded.

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