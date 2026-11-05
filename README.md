# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Pre-release:** DomainBlocks is in active development, and the API may change before the first stable release.

## Features

DomainBlocks is a set of NuGet packages, so you reference only what you need. Event storage, event filtering, and event
evolution are the first major feature areas; others will follow.

### Event store

The event store is built to be trusted as the system of record:

- **Atomic appends.** The events in a given append operation either all commit or none do.
- **Optimistic concurrency.** Pass the [`ExpectedStreamState`](src/DomainBlocks.EventStore/ExpectedStreamState.cs) with
  an append, and the operation is rejected with
  [`StreamAppendConflictException`](src/DomainBlocks.EventStore/StreamAppendConflictException.cs) if another writer got
  there first. The exception carries the state that was observed, so the caller may reload and retry.
- **Idempotent appends.** Give an append a unique commit ID, and a retry of the same request succeeds without writing
  again, even if the first attempt committed but its reply was lost (e.g. due to a timeout).
- **Globally ordered event log.** Global event sequencing guarantees events are observed in commit order
  across both historical reads and live subscriptions. Causes are always observed before their effects, and a replay
  sees exactly the sequence a live subscriber saw.
- **No skipped, phantom, or duplicate events.** A subscription delivers every committed event once, in order, and never
  delivers an event from a transaction that rolled back. A subscriber that cannot keep up is told it fell behind and
  automatically resumes from its last delivered position, so nothing is dropped silently.

The MongoDB store gets its global order from
[`DomainBlocks.MongoDB.Sequencing`](src/DomainBlocks.MongoDB.Sequencing/README.md), a package that can also be used on
its own to append documents to a collection with a contiguous sequence number.

Add a reference to `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, build a store, then append
and read events:

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

The default origin is the end of the event log, so omitting it receives only new events. `SubscribeToStream` works the
same way for a single event stream. A subscriber that consumes too slowly receives a message with `IsFellBehind` set to
`true`, then automatically catches up again.

Subscriptions never deliver an event that a failover could undo, provided the deployment does its part:

- **PostgreSQL** needs version 17 or later, set up as [Deploying PostgreSQL](#deploying-postgresql) describes.
- **MongoDB** needs a replica set. The event store always reads and writes with majority concern, so it never returns an
  event that a failover could roll back.

### Event filtering

Pass an `EventFilter` to a read to select events by name, stream, metadata, or creation time:

```csharp
var options = new ReadAllOptions { Filter = EventFilter.EventNames("OrderPlaced", "OrderShipped") };

await foreach (var e in store.ReadAll(options: options))
    Console.WriteLine($"{e.Context.StreamId}: {e.Payload}");
```

These are the filters to build from:

- `EventFilter.EventNames("OrderPlaced", "OrderShipped")`
- `EventFilter.StreamIds("order-1", "order-2")`
- `EventFilter.StreamIdStartsWith("order-")`
- `EventFilter.MetadataExists("tenant")`
- `EventFilter.Metadata("tenant", "acme", "initech")`
- `EventFilter.CreatedAtOrAfter(dateTime)`
- `EventFilter.CreatedBefore(dateTime)`

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

### Event evolution

Events are never modified in place in storage. To evolve their shape, register mappings in the event type map and add
read transforms. These apply whenever an event is read, through a normal read or a subscription, so the rest of your
code only ever sees current shapes.

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
builder.AddReadTransform((OrderShippedV1 e) =>
[
    new OrderShipped(e.OrderId),
    new TrackingNumberAssigned(e.OrderId, e.TrackingNumber)
]);
```

Rename an event by also reading its old stored name:

```csharp
new EventTypeMapBuilder()
    .Add<OrderCancelled>()
    .AddRead<OrderCancelled>("OrderVoided");
```

Reading an event fails if its stored name has no mapping, or if a read transform produces no events for it. To ignore
an event, map its stored name to a placeholder instance, or return the placeholder from a read transform when the
decision depends on the event's content.

```csharp
var typeMap = new EventTypeMapBuilder()
    .AddRead<OrderPlacedV1>("OrderPlaced")
    .Add<OrderPlaced>("OrderPlacedV2")
    .AddRead(Ignored.Instance, "OrderNoteAdded", "OrderNoteRemoved")
    .Build();

builder.AddReadTransform((OrderPlacedV1 e) => e.IsTest
    ? Ignored.Instance
    : new OrderPlaced(e.OrderId, e.Total, Currency: "GBP"));

public sealed record Ignored : IDomainEvent
{
    public static readonly Ignored Instance = new();
}
```

## Deploying PostgreSQL

The PostgreSQL implementation of the event store needs PostgreSQL 17 or later. Its subscriptions receive new events
through logical replication, so they also need `wal_level = logical` and a role with the `REPLICATION` attribute.

Connect through a single host that always reaches the primary. Subscriptions fail if the connection string lists
multiple hosts, because Npgsql can't open a replication connection to them.

The rest depends on how PostgreSQL is deployed:

|                              | Single server | Amazon Aurora        | Cluster with standbys                 |
|------------------------------|---------------|----------------------|---------------------------------------|
| Commits are durable on       | its disk      | the cluster volume   | the synchronous standbys              |
| Connect to                   | the server    | the cluster endpoint | a virtual IP or proxy for the primary |
| `synchronous_standby_names`  | empty         | empty                | the standbys a failover can promote   |
| `synchronized_standby_slots` | not used      | not available        | their physical slots                  |
| `sync_replication_slots`     | not used      | not available        | on, optionally                        |
| After a failover             | no failover   | subscriptions resume | subscriptions resume or catch up      |

### Amazon Aurora

Aurora needs none of the standby settings. A commit is durable once it reaches the cluster volume, which every
instance shares, so a failover can't lose it. Use Aurora PostgreSQL 17 or later with `rds.logical_replication = 1`,
grant the store's role `rds_replication`, and connect to the cluster endpoint.
[PostgreSQL on Amazon Aurora](docs/unpublished/postgresql-on-aurora.md) covers the setup, what happens in a failover,
and an assumption about Aurora that hasn't been confirmed yet.

### A cluster with standbys

A failover keeps a commit only if the standby it promotes has already flushed it to disk. To make sure it does, name
the standbys that a failover can promote in `synchronous_standby_names`, list their physical slots in
`synchronized_standby_slots`, and use a failover manager that keeps both lists in step. See
[PostgreSQL with standbys](docs/unpublished/postgresql-with-standbys.md) for further details.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT, and
that the [mongodb-sequencing repository](https://github.com/DomainBlocks/mongodb-sequencing), including every
`DomainBlocks.MongoDB.Sequencing` release up to and including 1.0.0, remains licensed under Apache 2.0.