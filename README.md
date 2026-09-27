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
        case { IsCaughtUp: true }:
            Console.WriteLine("Caught up; now receiving live events");
            break;
    }
}
```

The default origin is the end of the log, so omitting it receives only new events. `SubscribeToStream` works the same
way for a single event stream. A subscriber that consumes too slowly receives a message with `IsFellBehind` set to
`true`, then catches up again.

#### Filtering

Reads and subscriptions take an `EventFilter`, so that a consumer of part of the log does not fetch and decode the
rest of it. Filters are made with factories and combined with `&`, `|` and `!`:

```csharp
var filter =
    EventFilter.StreamIdStartsWith("order-") &
    EventFilter.Metadata("tenant", "acme") &
    (EventFilter.OfType<OrderPlaced>(e => e.Total > 100) | EventFilter.OfType<OrderShipped>());

await foreach (var e in store.ReadAll(options: new() { Filter = filter }))
    Console.WriteLine(e.Payload);

await foreach (var message in store.SubscribeToAll(SubscriptionOrigin.Start, new() { Filter = filter }))
    ...
```

A filter selects by what is stored with an event: its name (`EventName`), the type it is read as (`OfType`), its
stream (`StreamId`, `StreamIdStartsWith`), its metadata (`MetadataExists`, `Metadata`) and when it was created
(`CreatedAtOrAfter`, `CreatedBefore`). `EventFilter.All` and `EventFilter.None` are the identities of `&` and `|`, so
filters compose without null checks.

To select by what an event says, give `OfType` a predicate over the event: `OfType<T>(e => ...)`. It works with every
serializer, as the event is decoded first. The database narrows the read to the names that are read as `T`, and the
predicate is tested in process. Hold the filter in a static field: two predicates are equal only if they are the same
expression.

Where the database can see into payloads, which it can where they are stored as `jsonb` on PostgreSQL and as
documents on MongoDB, the defaults, it also helps with a predicate. It leaves out the events whose stored values
contradict it: for `e => e.Total > 100`, those with a stored total that is a number and at most 100. An event
whose total is missing, null or stored as something else is read and tested, as only reading it says what its total
is read as, so the events selected are the same as without this. In a log of 100,000 events of which the predicate
selects 100, it made a read about six times faster on PostgreSQL and nearly eight on MongoDB.

It applies to comparisons of a member of the event (`e.Total`, `e.Customer.Name`) with a value, to a `bool` member,
and to `!`, `&&` and `||` over them, where the member is text, a `bool`, an integer, a `decimal` or an enum. The
event type has to be stored as itself, not through a contract mapper or as several derived types, and the member
has to be written by `System.Text.Json` or the MongoDB driver itself, not by a converter of your own, read back as
it is written, and on MongoDB not written as a double. Anything else in a predicate is simply tested in process, as
the whole of it always is. The value is read when the read starts, or when a subscription starts to catch up, so a
captured variable should not change meanwhile. A static value that is not read-only, such as `DateTime.UtcNow`, is
never given to the database.

**Where a filter is evaluated.** As much of a filter as the database can evaluate becomes part of the query, and
each event that comes back is tested against the rest before it is decoded. The events selected are the same either
way. `FilterPushdown.Require` refuses a filter that would leave anything to test in process, and
`FilterPushdown.None` tests all of it in process.

A store says what it makes of a filter, without reading anything, and logs the same at debug level:

```csharp
var plan = store.ExplainFilter(EventFilter.OfType<OrderPlaced>(e => e.Total > 100));

Console.WriteLine(plan); // database: ...; in process: ...
```

A predicate is always in `plan.Remainder`, as only the decoded event says whether it is true, which is also why
`Require` refuses one. What tells whether the database helps with it is `plan.IsNarrowedByPayload`. Where that is
false, every event of the type is read and decoded to be tested, which a test can assert against for a read that is
meant to be narrow.

With read transforms, a filter is about the events that a read returns: `OfType<OrderPlaced>()` selects an
`OrderPlacedV1` that a transform turns into one.

**Subscriptions.** The live feed of a store is shared by its subscriptions and carries every event. Each subscription
tests its filter against the stored event, before it is queued, and decodes it only if a predicate is all that is
left to decide. An event is decoded once for all the subscriptions that take it, and not at all if none does.
Catching up is a filtered read.

A subscription that selects little may go a long time between events, so one with a filter also says how far it has
looked:

```csharp
case { LogCheckpoint: { HasValue: true, Value: var position } }:
    await SaveAsync(position); // resume with SubscriptionOrigin.After(position)
    break;
```

A checkpoint is the position to resume after: every event that the filter selects up to it has been delivered. It
comes before `CaughtUp` if catching up looked past the last event it delivered, and while live for events that are
passed over, every `SubscriptionOptions.CheckpointInterval` that there is one to report, so the last of them is
reported though nothing follows it. `SubscribeToStream` reports `StreamCheckpoint`, the position in its stream.
With read transforms, an event that the filter leaves nothing of is reported too: the first at once, and those that
follow within an interval when the store next has something to say.

**Store-level subscription filter.** An application that knows up front which events it will ever subscribe to can
have the server leave the rest out of the live feed, which saves their traffic as well:

```csharp
var store = new PostgresEventStoreBuilder<IDomainEvent>()
    .ConfigureOptions(o => o.SubscriptionFilter = EventFilter.StreamIdStartsWith("order-"))
    ...
```

Every subscription of the store is then subject to it, along with its own filter. Reads are not. It selects by what
is stored beside the payload: event names, streams, metadata and time. Filter by event name rather than by type, as a
store refuses any other filter as it is built.

- On MongoDB it becomes part of the `$match` of the store's change stream.
- On PostgreSQL it becomes the row filter of the store's publication, which needs PostgreSQL 15. The publication is
  created by `EnsureInitializedAsync` and named after the filter as it is written, so stores with different filters
  can share a schema while a deployment rolls out. A filter that is written another way, even with its parts in
  another order, gets a publication of its own. The one it had is left, and does nothing: no replication slot
  outlives its store. `DROP PUBLICATION` removes it, and `PostgresEventStoreAdmin.DropAsync` drops every publication
  of the schema. Keep the schema name to 40 characters.

The checkpoints of a subscription go by the events that the server sends, so they do not move on for the events
that a store-level filter leaves out.

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