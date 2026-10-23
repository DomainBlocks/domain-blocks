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

Subscriptions never deliver an event that a failover could undo, provided the deployment does its part:

- **MongoDB** needs a replica set. The store reads from the primary itself, so the connection string's read preference
  doesn't matter.
- **PostgreSQL** needs version 17 or later, set up as [Deploying PostgreSQL](#deploying-postgresql) describes.

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

## Deploying PostgreSQL

The store needs PostgreSQL 17 or later. Subscriptions receive new events through logical replication, so they also
need `wal_level = logical` and a role with the `REPLICATION` attribute.

Connect through a single host that always reaches the primary. Subscriptions fail if the connection string lists
several hosts, because Npgsql can't open a replication connection to them. Never connect to a standby, even a
synchronous one: it has a commit once its disk has it, not once its queries can see it, so a subscription reading
there could miss an event.

What else the server needs depends on how it is replicated:

|                              | Single server | Amazon Aurora        | Cluster with standbys                 |
|------------------------------|---------------|----------------------|---------------------------------------|
| Commits are durable on       | its disk      | the shared storage   | the synchronous standbys              |
| Connect to                   | the server    | the cluster endpoint | a virtual IP or proxy for the primary |
| `synchronous_standby_names`  | empty         | empty                | the standbys a failover can promote   |
| `synchronized_standby_slots` | not used      | not available        | their physical slots                  |
| `sync_replication_slots`     | not used      | not available        | on, optionally                        |
| After a failover             | no failover   | subscriptions resume | subscriptions resume or catch up      |

### Amazon Aurora

Aurora replicates its storage rather than the WAL, so it has no standbys to set up. A commit is durable before any
query can see it, and the cluster endpoint follows the writer.

- Use Aurora PostgreSQL 17 or later.
- Set `rds.logical_replication = 1` in the cluster parameter group, which is how Aurora sets `wal_level = logical`. It
  takes effect after a reboot.
- Grant the store's role `rds_replication`, which stands in for the `REPLICATION` attribute.
- Connect to the cluster endpoint. During a failover it can reach the old writer for a while, now a reader, and
  subscriptions keep retrying until it reaches the new one.

The store creates its replication slot with PostgreSQL's failover option, which has no effect on Aurora. Aurora keeps
slots in its shared storage instead, so after a writer failover subscriptions resume on the new writer without
catching up. AWS doesn't document this, but the [Aurora tests](tests/DomainBlocks.EventStore.PostgreSQL.Tests.Aurora)
check it. If a failover did lose the slot, subscriptions would catch up from where they were and miss nothing.

The store also relies on something the tests can't check: that Aurora's logical replication sends a commit only once
its storage has it, as Aurora's queries see a commit only then. If it sent one earlier, a failover could lose an
event that a subscription had already received. AWS doesn't document this either.

### A cluster with standbys

Each standby keeps its own copy of the WAL, so a commit is durable only once the standbys that a failover can promote
have it. The store runs its appends with `synchronous_commit = on`, and a catch-up delivers what it read only once the
synchronous standbys have it. The server has to do the rest:

- Name the standbys that a failover can promote in `synchronous_standby_names`, and use a failover manager that
  promotes only a synchronous standby, such as Patroni in synchronous mode.
- Give each of those standbys a physical replication slot, and list the slots in `synchronized_standby_slots`. The
  primary then holds each commit back from subscriptions until those standbys have it. Without the list,
  subscriptions still work, and the store logs a warning, but a failover can lose an event that a subscription has
  received, and the subscription then skips the event that reuses its position. A listed slot that is missing,
  invalidated or inactive holds subscriptions back, and only the server's log says why.
- Optionally, set `sync_replication_slots = on` on those standbys, with `hot_standby_feedback = on`,
  `wal_level = logical`, and a `dbname` in `primary_conninfo`. They then keep a copy of the store's slot, and after a
  failover subscriptions resume from it instead of catching up.
- Keep the lists in step. `synchronous_standby_names` names standbys by application name and
  `synchronized_standby_slots` by slot name, and PostgreSQL doesn't check that they agree, so let the failover manager
  own both.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT.