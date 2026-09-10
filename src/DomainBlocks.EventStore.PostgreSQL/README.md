# DomainBlocks.EventStore.PostgreSQL

A PostgreSQL implementation of `IEventStore` built on [Npgsql](https://www.npgsql.org/). Events live in a single
append-only table with a gap-free, commit-ordered global position, and live subscriptions are fed by logical
replication.

Requires PostgreSQL 14 or later and .NET 10.

## Guarantees

- **Stream positions** are zero-based and contiguous within a stream. Optimistic concurrency is enforced through
  `ExpectedStreamState` (`Any`, `DoesNotExist`, `Exists`, `AtVersion`); a conflict throws
  `StreamAppendConflictException<StreamPosition>` carrying the observed state.
- **Global positions** are zero-based, gap-free and assigned in commit order. All appends serialize on a single
  sequence row inside the append transaction, and the row lock is released only when the transaction commits, so a
  reader tailing `position > last` can never skip an event that commits later with a lower position. Historical
  reads sorted by position and live subscriptions therefore observe the same total order.
- **Idempotent commits**: appending with a `commitId` that already exists in the log succeeds without writing.
- **Subscriptions** replay from an origin up to a high-water mark, emit `CaughtUp`, then deliver live events. When a
  subscriber falls behind (its queue overflows) or the replication feed is re-established, the subscription emits
  `FellBehind` and catches up again from the last delivered position. Events are never skipped or duplicated.

## Getting started

```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
var options = new PostgresEventStoreOptions { Schema = "dbx" };

// Creates the schema, table, sequence row, append function and publication. Idempotent; run on start-up.
await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, options);

var codec = EventCodec.Create(new EventCodecOptions<object, PostgresEventData, string>
{
    TypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<OrderPlaced>()),
    EventSerde = new JsonObjectSerde().AsPostgresEventDataSerde(),   // jsonb column
    MetadataSerde = new JsonMetadataSerde()
});

await using var eventStore = PostgresEventStore.Create(dataSource, codec, options, logger);

await eventStore.AppendAsync("order-1", [new OrderPlaced(...)], ExpectedStreamState.DoesNotExist<StreamPosition>());
```

Event data is stored either as `jsonb` (queryable in SQL) or as `bytea`. Adapt any `IObjectSerde<string>` with
`AsPostgresEventDataSerde()` for `jsonb`, or any `IObjectSerde<byte[]>` / `IObjectSerde<ReadOnlyMemory<byte>>` for
`bytea`, e.g. `ProtobufBytesObjectSerde`. Metadata is always `jsonb`.

The store borrows connections from the data source and never disposes it. Disposing the store stops the append loop
and closes the replication connection.

## Schema

Everything lives in the configured schema (default `dbx`), which is also the unit of isolation for tests.

| Object | Purpose |
|---|---|
| `event_log` | One row per event: `position` (PK), `stream_id`, `stream_position`, `commit_id`, `commit_index`, `event_name`, `event_data jsonb`, `event_data_bytes bytea`, `metadata jsonb`, `created_at`. `UNIQUE (stream_id, stream_position)`. |
| `sequences` | The `event_log` counter row that all appends lock. |
| `append_events(...)` | PL/pgSQL function that commits a batch of appends in one round trip. |
| `<schema>_event_log_pub` | Publication of `event_log` inserts for logical replication. |

All writes must go through `append_events`; writing to `event_log` directly breaks the position guarantees.

## Appends

`AppendAsync` queues the request; a single loop per store instance commits queued requests in batches with one call
to `append_events`. Because appends serialize on the sequence row, batching is what recovers throughput under
concurrent load. Every request in a batch is evaluated independently: one conflict never aborts the others.

`AppendOptions.Timeout` bounds how long the caller waits. A request whose caller times out or cancels after it was
queued is still committed when its batch runs.

## Options

| Option | Default | Purpose |
|---|---|---|
| `Schema` | `dbx` | Schema holding all objects. `^[a-z_][a-z0-9_]{0,62}$`. |
| `AppendQueueCapacity` | 1000 | Queued append requests before callers wait. |
| `AppendBatchSize` | 500 | Maximum requests per round trip. |
| `AppendBatchingDelay` | 0 | Wait for more requests before committing a partial batch (Nagle-style). |
| `AppendBatchingDelayMinCount` | 0 | Queued requests required before the delay applies. |
| `ReadBatchSize` | 1000 | Rows per page when reading; reads use keyset pagination. |
| `Replication.ConnectionString` | data source's | Connection string for the replication connection (see below). |
| `Replication.SlotNamePrefix` | `dbx` | Prefix of temporary slot names. |
| `Replication.UseBinaryProtocol` | `true` | Binary row values (PostgreSQL 14+). |
| `Replication.WalReceiverTimeout` | 60 s | Silence before the replication connection is considered lost. |
| `Replication.WalReceiverStatusInterval` | 10 s | How often the consumed WAL position is acknowledged. |
| `Replication.RetryDelay` / `MaxRetryDelay` / `MaxRetryAttempts` | 1 s / 1 min / unlimited | Reconnect backoff. |

## Server requirements for subscriptions

Live subscriptions use logical replication with the `pgoutput` plugin. The server needs:

- `wal_level = logical` (requires a restart).
- `max_replication_slots` and `max_wal_senders` at least the number of store instances that subscribe concurrently.
  Each instance holds one slot and one walsender while it has at least one active subscription.
- A role with the `REPLICATION` attribute (or a superuser) for the replication connection.
- The publication created by `EnsureInitializedAsync` (or created out of band with
  `PostgresEventStoreAdminOptions.CreatePublication = false`). Note that `pgoutput` resolves publications lazily: a
  missing publication only fails when the first change is decoded, i.e. on the first write after subscribing.

By default the replication connection reuses the data source's connection string. Npgsql strips the password from
that string unless `Persist Security Info=true`, so either enable that or set `Replication.ConnectionString`
explicitly.

### Why temporary slots

Each store instance creates a *temporary* replication slot when its first subscription starts. The server drops it
when the connection closes, including when the process crashes, so no WAL is ever retained on behalf of a consumer
that never comes back. Subscriptions do not resume from the slot; they resume from the global position, which is the
canonical checkpoint for consumers as well.

If the replication connection is lost, the store reconnects with exponential backoff and a new slot. A new slot only
streams transactions committed after it was created, so every active subscription is told to reset: it emits
`FellBehind`, catches up from its last delivered position with an ordinary read, emits `CaughtUp` and continues live.
Gap-free positions make this exact.

Trade-offs: one walsender per subscribing instance, and slot creation waits for in-flight transactions to finish, so
a long-running transaction delays the first subscription and reconnects.

## Limitations

- PostgreSQL text and `jsonb` cannot contain NUL characters (`\0` or the `\u0000` escape). Stream ids, event data and
  metadata containing them are rejected with `ArgumentException`.
- `jsonb` normalises JSON (key order, whitespace, duplicate keys), so stored JSON is not byte-identical to the input.
- `append_events` must run under `READ COMMITTED` and never inside a caller-managed transaction; the store guarantees
  both.

## Benchmarks

The `[Explicit("Benchmark")]` tests in `PostgresEventStoreBenchmarkTests` measure the store end to end (codec included)
against a throw-away `postgres:17` container. They are skipped by a plain `dotnet test`; run them explicitly, in
Release, and read the results from the test output:

```shell
dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration -c Release \
  --filter "FullyQualifiedName~PostgresEventStoreBenchmarkTests" --logger "console;verbosity=detailed"
```

Methodology:

- Every append writes one small JSON event to a new stream, the cheapest possible append, so the figures are the
  store's ceiling rather than a workload.
- `AppendAsync_MeasureLatency` runs one append at a time after a warm-up on the same code path and reports
  percentiles over 10,000 samples.
- `AppendAsync_MeasureThroughput` runs a fixed number of closed-loop workers (1 to 1,000 in flight, over 1 or 4 store
  instances), warms up for 5 s and measures for 15 s by snapshotting a completion counter, so no in-flight append is
  cancelled or double counted. It also reports per-second stability and latency under that load. The throughput
  ceiling is the plateau across the cases.
- `SubscribeToAll_MeasureLiveLatency` appends one event and waits for a live subscription to deliver it, repeatedly.
- `NoOpEventStoreBenchmarkTests` in `DomainBlocks.EventStore.Tests.Unit` runs the same tests against a store whose
  appends do nothing, giving the harness's own ceiling. A store figure close to that ceiling is a harness limit.
- Each report starts with the environment (OS, CPU count, runtime, GC mode, build configuration) and the store and
  server settings that affect the result (`AppendBatchSize`, `wal_writer_delay`, `synchronous_commit`, `fsync`,
  `shared_buffers`). A Debug build or attached debugger is flagged as non-representative.

Results depend heavily on the machine: with Docker Desktop the server runs in a VM behind a virtual network and disk,
and the container keeps PostgreSQL's defaults (`synchronous_commit = on`, `fsync = on`, `shared_buffers = 128MB`). A
real deployment with network latency and synchronous replication will differ. Figures previously measured with an
earlier, single-point version of these tests on a Windows laptop were ~22,700 appends/s at 1,000 in flight and
p50 0.8 ms for sequential appends; re-run the suite for current numbers on your hardware.

Append-to-observe latency for a live subscription was ~200 ms at p50 with the server defaults and ~10 ms with
`wal_writer_delay = 10ms`: on this server the logical walsender is woken by the WAL writer's flush cycle, so
subscription latency tracks `wal_writer_delay` (200 ms by default). Lower it if you need lower delivery latency, at the
cost of more frequent WAL flushes.
