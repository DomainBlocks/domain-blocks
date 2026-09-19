# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Work in progress:** The API and functionality may change as the project matures.

## Features

DomainBlocks is a set of NuGet packages, so you reference only what you need. Event storage and event evolution are the
first feature areas; others will follow.

### Event storage

`DomainBlocks.EventStore` holds the store contracts and the append and read pipeline; a store package plugs a database
in. Add `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, build a store, then append and read
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
        case SubscriptionCaughtUp:
            Console.WriteLine("Caught up; now receiving live events");
            break;
    }
}
```

A subscription message is a union of an event, `SubscriptionCaughtUp` and `SubscriptionFellBehind`. `Event` is a
shorthand for the event case, whose full type is `ReadEvent<IDomainEvent, string, StreamPosition, LogPosition>`;
matching on that type instead lets a `switch` expression over all three be checked for completeness.

The default origin is the end of the log, so omitting it receives only new events. `SubscribeToStream` works the same
way for a single event stream. A subscriber that consumes too slowly receives a `SubscriptionFellBehind` message, then
catches up again.

### Event evolution

Stored events are never rewritten. Instead, read transforms reshape old events as they are read, on reads and
subscriptions alike, so the rest of the code only sees current shapes.

Upcast an event that gained a field. A `ReadOnly` mapping keeps the retired version decodable under its stored name,
and a `ReadWrite` mapping names the current version for writing:

```csharp
var builder = new PostgresEventStoreBuilder<IDomainEvent>()
    .ConfigureCodec(c => c.MapEvents(
        EventTypeMapping.ReadOnly<OrderPlacedV1>("OrderPlaced"), // previous version already stored
        EventTypeMapping.ReadWrite<OrderPlaced>("OrderPlacedV2")))
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

Ignore events by stored name, or by producing nothing from a transform. Each is read as the sentinel, so its position
is still observed:

```csharp
builder
    .IgnoreEvents("TradeNoteAdded")
    .AddReadTransform((TradeBookedV1 e) => e.IsTest ? [] : [new TradeBooked(e.TradeId, e.Quantity)])
    .UseIgnoredEventSentinel(Ignored.Instance);

public sealed record Ignored : IDomainEvent
{
    public static readonly Ignored Instance = new();
}
```

A store typed over `object` can use the built-in `IgnoredEvent.Instance` as the sentinel instead.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT.