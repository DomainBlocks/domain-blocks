# PostgreSQL implementation of IEventStore

## Prompt

The following prompt was used to generate this plan:

Task: Build a PostgreSQL implementation of IEventStore

- Define the plan in small steps, where each step is committed locally to GIT with a descriptive message
- Follow the 50/72 commit message rule
- Each step much be reviewable and verifiable by a human (me - a software engineer)
- Add features in this order:
    - Appends
    - Stream reads
    - "All" event reads - i.e. reading the global event log
    - Subscriptions, both "all" and stream subscriptions
- One feature doesn't necessarily mean one step. Break down into small incremental steps
- Explore the project and tests, particularly the MongoDB implementation, to understand the correctness guarantees we
  are providing.
- Pay attention to the additional folder "mongodb-sequencing", which contains the implementation for applying a total
  order (i.e. insertion order) with a gap-free sequence, such that historical replays sorted by a global sequence number
  match live event observations of those same events. The project name is DomainBlocks.MongoDB.Sequencing.
- The Postgres sequencing does not need to extract functionality to a separate library as it has been done with
  DomainBlocks.MongoDB.Sequencing.
- Appending a batch of events should be one roundtrip to the database - i.e. transaction and multiple steps handled
  entirely on the DB server (if possible). Prefer this over the client managing a transaction with multiple round trips.
  This should be possible, but inform me if not.
- Global sequence numbers should be gap free
- Recover append throughput with batching (similar to DomainBlocks.MongoDB.Sequencing's approach)
- Keep the implementation in a new project DomainBlocks.PostgreSQL.EventStore
- Use Npgsql for the implementation
- Use logical replication for subscriptions, with fan-out
- Suggest an efficient way to use slots with fan-out. With MongoDB change streams, we worked around needing resume
  tokens by creating a shared watcher that observers attach to - avoiding the need to checkpoint by resume token. The
  canonical resume mechanism is the global sequence number, i.e. global position. I'm not a logical replication expert,
  but my thoughts are we could do something similar with ephemeral slots per event store instance, to avoid needing to
  keep track of position on the Postgres side. If this does not make sense architecturally, challenge this and suggest
  alternatives.
- Support the minimum Postgres database version to support our requirements, factoring in what the latest Npgsql library
  supports.
- If there are any reusable concepts from the MongoDB implementation, e.g. channel-based observer, etc. suggest
  approaches for reuse where appropriate. Prefer a working implementation over early reusable abstractions, but
  highlight where reuse makes sense. If something is trivially reusable, incorporate this into the plan.

## Context

DomainBlocks has one complete `IEventStore` implementation (MongoDB) and one partial one (KurrentDB). The
MongoDB store relies on the external `DomainBlocks.MongoDB.Sequencing` package to give the global log a
gap-free, commit-ordered position so that a historical replay sorted by position and a live change stream
observe the same total order. That property is what makes catch-up-then-live subscriptions and position-based
checkpoints correct.

This plan adds `DomainBlocks.EventStore.PostgreSQL`, a full implementation on Npgsql 10 with the same
guarantees, built in small, individually reviewable commits in the order: appends, stream reads, all reads,
subscriptions. Branch `feat/postgres` is currently identical to `main`; the `src/DomainBlocks.EventStore.PostgreSQL`
folder on disk holds only gitignored `bin/obj` leftovers.

### Decisions already made with the user

| Topic | Decision |
|---|---|
| Project name | `src/DomainBlocks.EventStore.PostgreSQL` (repo convention); tests `tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration` |
| Payload storage | `event_data jsonb` + `event_data_bytes bytea`, exactly one non-null; `metadata jsonb`. C# union `PostgresEventData`; codec `EventCodec<TEvent, PostgresEventData, string>` |
| Fan-out infrastructure | Copy the generic MongoDB pieces into the Postgres project with backend-neutral names; consolidation is a follow-up |
| Live feed | Logical replication only (pgoutput, temporary slot per store instance), behind a small internal feed interface so a LISTEN/NOTIFY feed could be added later |
| Append | One round trip per batch via a PL/pgSQL function; gap-free global positions from a counter-row lock; client-side batching loop ported from `MongoSequencedAppender` |
| Versions | Npgsql 10.0.3 (latest stable, already pinned in the local cache), PostgreSQL 14+ (oldest release inside Npgsql's "currently supported PostgreSQL" policy and the floor for pgoutput binary mode), .NET 10 |

### Conventions to follow

- Conventional Commits, lowercase imperative subject, scope `postgres`, subject <= 50 chars, body wrapped at 72.
  Repo style: `feat:`, `fix:`, `test:`, `chore:`, `docs:`, `build:`, `refactor:`.
- `Directory.Build.props`: net10.0, nullable, `EnforceCodeStyleInBuild`, C# 14 features (`extension` blocks, `field`) are in use.
  `.editorconfig`: 120 columns, file-scoped namespaces, `var` everywhere, no expression-bodied methods.
- Central package management (`Directory.Packages.props`); new projects must be added to `DomainBlocks.slnx`.
- No DI, no builder: a static `Create(...)` factory taking a caller-owned `NpgsqlDataSource`, mirroring
  `MongoEventStore.Create(IMongoClient, EventCodec, options, logger)`. Schema init is explicit via an `Admin` class.
- Tests: NUnit 4 + Shouldly + Testcontainers, `[SetUpFixture]` with static data source and `NUnitLoggerProvider`
  (`tests/DomainBlocks.Testing/NUnitLoggerProvider.cs`). Shared suites live in `tests/DomainBlocks.Testing.Integration`
  (`EventStoreTests`, `EventStoreSubscriptionTests`, `EventStoreEventRepresentationTests`, `EventStoreBenchmarkTests`).
  Docker Desktop must be running (it was not at planning time).

## Architecture summary

```
PostgresEventStore<TEvent>              IPostgresEventStore<TEvent> : IEventStore<TEvent,string,StreamPosition,LogPosition>, IAsyncDisposable
 ├─ AppendAsync  ──► BatchingAppender ──► AppendBatchCommand ──► SELECT * FROM dbx.append_events($1..$9)   (1 round trip / batch)
 ├─ ReadStream / ReadAll ──► EventLogReader: keyset-paged SELECTs over NpgsqlDataReader
 └─ SubscribeToAll / SubscribeToStream ──► SubscriptionAsyncEnumerable<TEvent,TPos> (ported from Mongo)
        catch-up SQL (origin, HWM]  +  live rows from RefCountedEventLogFeed ──► EventLogFeed ──► ReplicationEventLogSession
                                                                                 (LogicalReplicationConnection, temporary pgoutput slot)
```

Correctness guarantees, identical to the MongoDB store:

- **Stream positions** are 0-based and contiguous per stream; `UNIQUE (stream_id, stream_position)` is the safety net.
- **Global positions** are 0-based, gap-free and commit-ordered. All appends serialize on one counter row inside the
  function's transaction; the lock is released only after commit, so position order == commit order == visibility order.
  A tailer doing `WHERE position > @last ORDER BY position` can never skip a row (the classic `bigserial` skip bug is
  impossible: no position is ever visible before every lower position is visible).
- **Idempotent commits**: an existing `commit_id` (anywhere in the log, any stream) makes the append succeed without writing.
- **Subscriptions**: attach live observer, read high-water mark, replay `(origin, HWM]`, emit `CaughtUp`, drain live rows
  skipping `position <= HWM`. Overflow of a subscriber's bounded queue, or loss of the replication feed, produces
  `FellBehind` and a restart of catch-up from the last delivered position.

## Schema (embedded `Sql/schema.sql`, `__schema__` token substituted, default schema `dbx`)

Schema is the unit of isolation (tests use one schema per fixture like Mongo's database-per-fixture); table names are
not configurable, only `Schema`. Schema name validated against `^[a-z_][a-z0-9_]{0,62}$` before interpolation.

```sql
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('__schema__:init'));       -- serialize concurrent initializers
CREATE SCHEMA IF NOT EXISTS __schema__;
CREATE TABLE IF NOT EXISTS __schema__.event_log (
    position         bigint      NOT NULL,
    stream_id        text        NOT NULL,
    stream_position  bigint      NOT NULL,
    commit_id        uuid        NOT NULL,
    commit_index     integer     NOT NULL,
    event_name       text        NOT NULL,
    event_data       jsonb,
    event_data_bytes bytea,
    metadata         jsonb,
    created_at       timestamptz NOT NULL,
    CONSTRAINT event_log_pkey PRIMARY KEY (position),
    CONSTRAINT event_log_stream_id_stream_position_key UNIQUE (stream_id, stream_position),
    CONSTRAINT event_log_event_data_check CHECK ((event_data IS NULL) <> (event_data_bytes IS NULL)),
    CONSTRAINT event_log_stream_position_check CHECK (stream_position >= 0));
CREATE INDEX IF NOT EXISTS event_log_commit_id_idx ON __schema__.event_log (commit_id);
CREATE TABLE IF NOT EXISTS __schema__.sequences (name text PRIMARY KEY, next bigint NOT NULL CHECK (next >= 0))
    WITH (fillfactor = 50);                                        -- hot single row, HOT updates
INSERT INTO __schema__.sequences (name, next) VALUES ('event_log', 0) ON CONFLICT (name) DO NOTHING;
-- CREATE OR REPLACE FUNCTION __schema__.append_events(...)  (Sql/append_events.sql)
DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = '__schema___event_log_pub') THEN
  EXECUTE 'CREATE PUBLICATION __schema___event_log_pub FOR TABLE __schema__.event_log WITH (publish = ''insert'')';
END IF; END $$;                                                   -- guarded by AdminOptions.CreatePublication
COMMIT;
```

`created_at` is `clock_timestamp()` captured once per batch *after* the lock is acquired (so it is monotone with
position; `now()` would be transaction start, i.e. before waiting on the lock). Read as `DateTimeOffset` via
`GetFieldValue<DateTimeOffset>`.

## Append function `__schema__.append_events` (embedded `Sql/append_events.sql`)

Parallel-array signature, no composite types (Npgsql sends `string?[]` as `Array|Jsonb`, `byte[]?[]` as `Array|Bytea`,
`long?[]` as `Array|Bigint`; NULL elements are supported). Fallback if `jsonb[]` parameters misbehave: `text[]` with a
per-element `::jsonb` cast inside the INSERT.

```sql
CREATE OR REPLACE FUNCTION __schema__.append_events(
    p_stream_ids text[], p_expected_kinds smallint[], p_expected_versions bigint[],   -- per request (R)
    p_commit_ids uuid[], p_event_counts integer[],
    p_event_names text[], p_event_data jsonb[], p_event_data_bytes bytea[], p_metadata jsonb[])  -- per event (E), flattened
RETURNS TABLE (request_index integer, status smallint, observed_kind smallint, observed_version bigint,
               first_position bigint, last_position bigint)
```

Protocol constants (C# `AppendProtocol`, never cast enums directly): expected kind 0 Any / 1 DoesNotExist / 2 Exists /
3 AtVersion; status 0 Appended / 1 Conflict / 2 Duplicate; observed kind 0 DoesNotExist / 1 AtVersion.

Body, in order:

1. Guard `current_setting('transaction_isolation') = 'read committed'` (the `FOR UPDATE` re-read relies on it); validate
   array cardinalities, `event_counts > 0`, kinds in range, version present iff AtVersion. Violations `RAISE` with
   `invalid_parameter_value` (client bug, whole batch faults).
2. `SELECT next INTO v_start FROM sequences WHERE name = 'event_log' FOR UPDATE` (serializes all appenders; after
   waking, READ COMMITTED re-fetches the newest committed value). `v_created_at := clock_timestamp()`.
3. Idempotency pre-pass: `v_existing := ARRAY(SELECT DISTINCT commit_id FROM event_log WHERE commit_id = ANY(p_commit_ids))`.
4. `FOR i IN 1..R`: head = `SELECT max(stream_position) WHERE stream_id = ...` (backward index probe; sees rows inserted
   earlier in this loop, so two requests to one stream chain correctly). Duplicate (in log or already seen in this batch)
   -> status 2, `RETURN NEXT`, `CONTINUE`. Expected-state mismatch -> status 1 with observed state, `RETURN NEXT`,
   `CONTINUE`. Otherwise `INSERT ... SELECT v_pos + k, ..., coalesce(head,-1)+1+k, ..., p_event_names[off+k], ...
   FROM generate_series(0, count-1) k`; status 0 with first/last position; advance `v_pos` and `off`.
5. `UPDATE sequences SET next = v_pos` only if rows were inserted. Counter advances by exactly the rows inserted:
   conflicts and duplicates leave no gap.

Per-request failures are result rows, never exceptions, so one request's conflict never aborts the batch. The unique
index cannot fire for writers going through the function (heads computed under the lock); if it fires because of an
out-of-band writer the whole batch is faulted with the `PostgresException` and not retried.

Head lookup via `max()` on the existing unique index instead of a `streams` head table: no extra hot row per stream,
no second source of truth, and the global lock removes any need for per-stream locking.

## C# design (append side)

Public:

- `PostgresEventData` readonly struct: `FromJson(string)`, `FromBytes(ReadOnlyMemory<byte>)`, `IsJson`, `Json`, `Bytes`.
- `ObjectSerdeExtensions.AsPostgresEventDataSerde()` for `IObjectSerde<string>`, `IObjectSerde<byte[]>`,
  `IObjectSerde<ReadOnlyMemory<byte>>` (same shape as `src/DomainBlocks.Serialization.MongoDB.Bson/ObjectSerdeExtensions.cs`).
- `JsonMetadataSerde : IMetadataSerde<string>` added to `src/DomainBlocks.Serialization.SystemTextJson` (there is only a
  UTF-8 bytes metadata serde today; jsonb needs a string).
- `PostgresEventStoreOptions { Schema = "dbx", AppendQueueCapacity = 1000, AppendBatchSize = 500,
  AppendBatchingDelay = 0, AppendBatchingDelayMinCount = 0 }` (mirrors `MongoEventStoreOptions` +
  `MongoSequencedAppenderOptions`).
- `IPostgresEventStore<TEvent>`, `PostgresEventStore.Create<TEvent>(NpgsqlDataSource, EventCodec<TEvent, PostgresEventData, string>, options?, logger?)`.
- `PostgresEventStoreAdmin.EnsureInitializedAsync(NpgsqlDataSource, PostgresEventStoreOptions, PostgresEventStoreAdminOptions? { CreatePublication = true }, ct)`:
  idempotent, concurrency-safe (advisory lock in script), runs the embedded SQL as one multi-statement command.

Internal:

- `AppendRequest` (stream id, expected state, commit id, `EncodedEvent<PostgresEventData,string>[]`, TCS with
  `RunContinuationsAsynchronously`, `TryComplete(Exception?)`) — port of the sequencing library's `AppendRequest`.
- `AppendBatchCommand`: one reusable `NpgsqlCommand` created from the data source with positional `$1..$9` typed array
  parameters; `ExecuteAsync(batch)` flattens requests, executes, maps result rows: Appended/Duplicate -> complete;
  Conflict -> `StreamAppendConflictException<StreamPosition>(streamId, expected, observed)` with observed always
  populated; missing row -> `InvalidOperationException`. Never enlists in a transaction (autocommit makes the function
  call atomic). Relies on auto-prepare (`Max Auto Prepare` set by the fixture / documented for users).
- `BatchingAppender`: port of `RunAppendLoopAsync` from `D:\dev\mongodb-sequencing\src\DomainBlocks.MongoDB.Sequencing\MongoSequencedAppender.cs`
  (bounded channel, single reader, drain to `MaxBatchSize`, Nagle delay gated by `BatchingDelayMinCount`, per-request
  timeout via linked CTS -> `TimeoutException`, `FaultAll` on batch error without stopping the loop, `DrainWithFault` on
  dispose). Extra: one retry of the identical batch on `NpgsqlException.IsTransient` (safe because commit ids make
  re-execution idempotent: already-committed requests come back as Duplicate).
- `AppendAsync`: validate stream id (non-empty, no NUL), default expected state/commit id/options, encode; empty
  events -> return without enqueueing (Mongo parity); reject `\u0000` in JSON payloads client-side so one caller's bad
  payload cannot fault a batch of strangers.

Documented behaviours: a request whose caller cancels or times out while queued is still committed when its batch runs
(Mongo parity); all writes must go through `append_events`; the store never disposes the data source.

## Read design

- `EventLogRow` internal record (position, stream id, stream position, event name, `PostgresEventData`, metadata
  string?, `DateTimeOffset` created at) shared by reads and the live feed; `ReadEventExtensions.Decode(row)` mirrors
  `src/DomainBlocks.EventStore.MongoDB/ReadEventExtensions.cs` (C# 14 `extension` block on the decoder).
- `EventLogSql` builds all SQL once per store from the validated schema name. `EventLogReader` executes it on the data
  source with **keyset pagination** (`PostgresEventStoreOptions.ReadBatchSize`, default 1000): a consumer-paced,
  unbounded `ReadAll` or catch-up must not pin a pooled connection for its whole duration, and positions are
  immutable and gap-free so keyset pages are exact. `MaxCount` caps the final page. Default `CommandBehavior`
  (not `SequentialAccess`) in v1; columns are read in ascending ordinal order so it can be switched on later.
- Direction x origin matrix after the `ProducesEmptyReadFrom` short-circuit (`At` is inclusive):

  | direction | origin | first key bound |
  |---|---|---|
  | Forward | Start / At(p) | `position > -1` / `position > p-1` ... `ORDER BY position LIMIT n` |
  | Backward | End / At(p) | `position < long.MaxValue` / `position < p+1` ... `ORDER BY position DESC LIMIT n` |

  Same on `stream_position` for `ReadStream` (served by the unique index). `IncludeMetadata=false` selects
  `NULL::jsonb AS metadata`.
- `StreamNotFoundBehavior.Throw`: `EXISTS` check on the edge-case path and when the result is empty, so it throws only
  when the stream genuinely does not exist (Mongo throws on any empty result; both pass the shared tests).
- `timestamptz` read as `DateTime` (Kind=Utc) and wrapped in `DateTimeOffset` with zero offset. jsonb normalises key
  order/whitespace; metadata round-trips as a dictionary, not byte-for-byte.

## Subscription design

### Live feed: logical replication with a temporary slot per store instance

Your proposed shape holds up and I recommend it as-is, with one important addition (the reset signal below).

- **Per store instance, on first subscriber (ref-counted)**: open a `LogicalReplicationConnection`, create a
  **temporary** pgoutput slot (`CreatePgOutputReplicationSlot(name, temporarySlot: true, LogicalSlotSnapshotInitMode.NoExport)`),
  `StartReplication(slot, new PgOutputReplicationOptions(publication, PgOutputProtocolVersion.V1, binary: true, streamingMode: Off))`.
  Slot name `dbx_<12 hex of instance guid>_<attempt>`. Slot creation returns a consistent point C; every transaction
  committing after C is streamed. `ConnectAsync` completes once `StartReplication` has returned and the pump task is
  running (correctness rests on C, not on pump timing).
- **Pump**: `RelationMessage` -> column map by name; `InsertMessage` for the event log -> read `NewRow` fully (Npgsql
  recycles messages; forward-only tuple) into `EventLogRow` and fan out immediately (pgoutput with streaming off only
  emits committed transactions in commit order, and commit order == position order, so per-insert delivery is already
  ordered); `CommitMessage` -> `SetReplicationStatus(commit.WalEnd)` so the server can release WAL. Observers must never
  block (`TryWrite`), otherwise keepalives stall and `wal_sender_timeout` kills the walsender.
- **Why temporary slots are the right call**: no server-side state to checkpoint, nothing retains WAL if an instance
  crashes (persistent slots are the classic disk-full outage), and the canonical resume point is the global position
  anyway. Costs: one walsender + slot per subscribing instance (`max_replication_slots`, `max_wal_senders` must be
  sized), slot creation waits for in-flight transactions, and events during a reconnect gap are recovered by a
  catch-up query instead of WAL replay. Those are acceptable for an event store whose positions are gap-free.
- **Server requirements** (documented in the project README): PG 14+, `wal_level = logical`, `REPLICATION` role
  attribute (or superuser), publication created by the admin initializer (needs table ownership / CREATE on database).

### Feed abstraction (so a LISTEN/NOTIFY feed can be added later)

`Feeds/` folder, copied from `src/DomainBlocks.EventStore.MongoDB/ChangeStreams/` with backend-neutral names and the
row type fixed to `EventLogRow` (generics dropped):

- `IEventLogObserver { OnNextAsync(row), OnResetAsync(), OnErrorAsync(ex) }` (`OnResetAsync` is new),
  `IEventLogFeed { Attach, ConnectAsync }`, `IEventLogFeedConnection { Completion }`, `IRefCountedEventLogFeed`,
  `RefCountedEventLogFeed` (verbatim port of `RefCountedChangeStreamSubject`), `CorrelationId` (verbatim).
- `IEventLogSession : IAsyncDisposable { Description, ReadRowsAsync(ct) }` + `EventLogSessionFactory(attempt, ct)`:
  the only backend-specific piece. Contract: once the factory completes, every row committed after the session's
  establishment point is yielded in commit order until fault/dispose. `ReplicationEventLogSession` is the pgoutput
  implementation; a future notify+tail-read session satisfies the same contract.
- `EventLogFeed` (port of `ChangeStreamSubject`): `ConnectionState` CAS registry, detach-on-throw, Polly retry
  (exponential + jitter, `EventLogFeedOptions { MaxRetryAttempts, RetryDelay, MaxRetryDelay }`),
  `EventLogFeedResumePolicy.CanResume` = `NpgsqlException.IsTransient` (08xxx, 53xxx, 57P01 from
  `pg_terminate_backend`, ...) plus IO/socket/timeout exceptions; non-transient (42704 missing publication, 42501,
  55000 wal_level) faults the feed with a clear error and `RefCountedEventLogFeed` builds a fresh feed on next attach.

### Reset semantics (the one addition to the Mongo algorithm)

A reconnect creates a new slot with a later consistent point C2; rows committed during the outage are never streamed.
So after reconnecting, the feed calls `OnResetAsync` on every attached observer **after the new session is streaming
and before any new-session row is pumped**. The subscription `Observer` treats a reset exactly like queue overflow:
cancel its restart token and complete its channel. `SubscriptionAsyncEnumerable` then yields `FellBehind` and
restarts: attach a new observer (before detaching the old one so the ref count never dips to zero and the slot is not
torn down), read HWM (now after C2), catch up `(lastDelivered, HWM]`, `CaughtUp`, live drain skipping `<= HWM`.
No loss (outage rows have `position <= HWM`), no duplicates (live filter), and the ordering rule prevents a
new-session row from advancing the resume position past the gap.

### SubscriptionAsyncEnumerable port

Same class for both subscriptions, parameterised by a `SubscriptionTarget<TPos>` (`AllStreamsTarget` /
`SingleStreamTarget(streamId)`: catch-up query, live filter, position selector). Catch-up SQL is bounded by HWM:
`position > $after AND position <= $hwm` or `stream_id = $1 AND stream_position > $after AND position <= $hwm`.
Cancellation: pre-cancelled token throws on first `MoveNextAsync` (gate wait in `AttachAsync`); live wait honours the
token via `ChannelReader.ReadAllAsync(ct)`; `EventLogFeed.ConnectAsync` disposes its connection if cancelled mid
connect (Mongo leaves it running; small hardening). `PostgresEventStore.DisposeAsync` disposes the appender and the
ref-counted feed (rejects further attaches) so a forgotten enumerator cannot keep a walsender alive.

## Step-by-step commit plan

Each step is one local commit, builds clean with `EnforceCodeStyleInBuild`, and its tests pass before moving on.

### Step 1 `build: add Npgsql and Testcontainers.PostgreSql`
- `Directory.Packages.props`: `Npgsql` 10.0.3, `Testcontainers.PostgreSql` 4.15.0.

### Step 2 `feat(postgres): add project skeleton and options`
- New `src/DomainBlocks.EventStore.PostgreSQL/` (`IsPackable`, refs `Npgsql`, `Microsoft.Extensions.Logging.Abstractions`,
  project ref Abstractions, `InternalsVisibleTo` test projects), slnx entry.
- `PostgresEventData`, `ObjectSerdeExtensions`, `PostgresEventStoreOptions`, `IPostgresEventStore`, `SqlNames`
  (schema validation + quoted identifiers), `JsonMetadataSerde` in Serialization.SystemTextJson.
- Unit tests (new `tests/DomainBlocks.EventStore.PostgreSQL.Tests.Unit`): `PostgresEventData_FromJson_IsJson`,
  `PostgresEventData_Json_ThrowsWhenBytes`, `SqlNames_InvalidSchema_Throws`, `JsonMetadataSerde_RoundTrips`.

### Step 3 `feat(postgres): add schema admin and test fixture`
- `PostgresEventStoreAdmin`, `PostgresEventStoreAdminOptions`, embedded `Sql/schema.sql`.
- New `tests/DomainBlocks.Testing.Integration.PostgreSQL/` (Testcontainers `PostgreSqlBuilder().WithImage("postgres:17")
  .WithCommand("-c", "wal_level=logical")`, `TestPostgresEventCodec.Create(eventTypeMap, eventFormat, contractMappers)`
  mapping Json -> `JsonObjectSerde`, Protobuf -> `ProtobufBytesObjectSerde` (bytea), and new
  `tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration/` with `SetUpFixture` (static `NpgsqlDataSource` with
  `MaxAutoPrepare`, `LoggerFactory`), slnx entries.
- Tests `PostgresEventStoreAdminTests`: `EnsureInitializedAsync_CreatesSchemaObjects`, `..._CalledTwice_IsIdempotent`,
  `..._ConcurrentCalls_AllSucceed`, `..._InvalidSchemaName_Throws`, `..._SeedsSequenceRowOnce`.

### Step 4 `feat(postgres): add append_events function`
- Embedded `Sql/append_events.sql`, `AppendProtocol`.
- Tests `AppendFunctionTests` calling the function through raw `NpgsqlCommand` array parameters (this also settles the
  `jsonb[]` vs `text[]` parameter question empirically): new stream from 0; existing stream continues from head;
  multiple requests contiguous global positions; sequence advanced by rows inserted; each conflict kind returns the
  right observed state; conflict in batch does not abort others; conflicts/duplicates do not advance sequence; existing
  commit id -> duplicate without write (also on a different stream); repeated commit id in batch writes first only;
  same stream twice in batch (DoesNotExist twice -> Appended then Conflict AtVersion(0)); json and bytes land in their
  columns; null metadata; created_at shared per batch and UTC; array length mismatch / zero count / AtVersion without
  version raise `22023`; empty batch returns no rows; call under REPEATABLE READ raises `25001`.
- Also time a large batch (500 requests x 10 events) to confirm array subscripting inside PL/pgSQL is not O(E) per
  request; if it is, switch the INSERT to a single `unnest ... WITH ORDINALITY` per request bounded by offsets, or copy
  parameters into local array variables once.

### Step 5 `feat(postgres): add unbatched append path`
- `AppendRequest`, `AppendBatchCommand`, `PostgresEventStore` + factory, `LogMessages`; `AppendAsync` executes a
  single-request batch directly (no loop yet). Reads/subscriptions throw `NotImplementedException`.
- Tests `PostgresEventStoreAppendTests` verifying via direct SQL: rows and columns written; stream position continues;
  each conflict kind throws `StreamAppendConflictException<StreamPosition>` with observed state; same commit id twice
  writes once; empty events writes nothing; json/bytes/metadata/null metadata columns; created_at UTC; NUL in stream
  id -> `ArgumentException`; sequence row locked elsewhere -> `TimeoutException` with `AppendOptions.Timeout = 500ms`.

### Step 6 `feat(postgres): batch appends with channel loop`
- `BatchingAppender`, options wiring, transient single retry.
- Tests `PostgresEventStoreConcurrencyTests` (verbatim port of
  `tests/DomainBlocks.EventStore.MongoDB.Tests.Integration/MongoEventStoreConcurrencyTests.cs`, three store instances)
  and `PostgresEventStoreBatchingTests`: `ConcurrentAppends_GlobalPositionsAreGapFree` (`count(*) = max(position)+1`
  and `sequences.next = count(*)`), `ConcurrentAppends_MixedOutcomesInBatch_CompleteIndividually`,
  `ConcurrentAppends_AreCoalescedIntoBatches` (`AppendBatchingDelay = 20ms`, count calls via
  `pg_stat_user_functions` with `track_functions = 'all'`), `DisposeAsync_WithQueuedRequests_FaultsThem`,
  `AppendAsync_AfterDispose_Throws`.

### Step 7 `feat(postgres): implement ReadStream`
- `EventLogRow`, `EventLogSql`, `EventLogReader` (keyset paging, `ReadStreamAsync`, `StreamExistsAsync`),
  `ReadEventExtensions`, `ReadStream` in the store, `ReadBatchSize` option.
- Enable shared fixtures: `PostgresEventStoreTests : EventStoreTests<StreamPosition, LogPosition>` (covers all append
  conflict tests and the ReadStream matrix) and `PostgresEventStoreEventRepresentationTests` (Json and Protobuf
  formats exercising both payload columns). Extra: `ReadStream_MoreRowsThanBatchSize_PagesWithoutGapsOrDuplicates`
  (`ReadBatchSize = 7`, 50 events), `ReadStream_MaxCountSmallerThanBatch_ReturnsMaxCount`,
  `ReadStream_IncludeMetadataFalse_ReturnsEmptyMetadata`, `ReadStream_AtPositionBeyondEndWithThrow_ReturnsEmpty`.

### Step 8 `feat(postgres): implement ReadAll`
- `ReadAllAsync` in the reader; `ReadAll` in the store.
- Tests `PostgresEventStoreReadAllTests`: forward gap-free ascending positions; backward descending; `At` inclusive in
  both directions; `MaxCount` across pages; multiple streams interleave in commit order; edge-case direction/origin empty.

### Step 9 `feat(postgres): add event log feed abstractions`
- `Feeds/`: observer/feed/connection interfaces, `RefCountedEventLogFeed`, `CorrelationId`, `IEventLogSession`,
  `EventLogSessionFactory`, `EventLogFeedOptions`, `EventLogFeedResumePolicy`, `EventLogFeed` with `ConnectionState`
  and `NotifyResetAsync`. Add `Microsoft.Extensions.Resilience` package ref (Polly, as Mongo).
- Unit tests with a fake session factory (mirror `tests/DomainBlocks.EventStore.MongoDB.Tests.Unit/ChangeStreams/*`):
  rows reach all observers; transient failure reconnects and `OnReset` is observed before the first post-reconnect
  row; normal stream end reconnects; non-transient failure faults `Completion` and notifies observers; retries
  exhausted faults; attach after fault throws; observer throwing in `OnNext`/`OnReset` is detached; `ConnectAsync`
  completes only after the session is established; dispose during backoff completes promptly; the six
  `RefCountedEventLogFeedTests`.

### Step 10 `feat(postgres): add logical replication session`
- `ReplicationEventLogSession`, `ColumnMap`, slot naming, replication sub-options (`UseBinaryProtocol`,
  `WalReceiverTimeout`, `WalReceiverStatusInterval`), store wiring of `RefCountedEventLogFeed`, admin publication.
- Integration tests `ReplicationEventLogFeedTests`: two observers see all 10 rows of one append in order; slot shows
  in `pg_replication_slots` with `temporary = true, active = true`; last detach drops the slot (poll until gone);
  binary decoding of json, bytes, null/non-null metadata, uuid and UTC timestamp columns. Fixture asserts
  `SHOW wal_level = 'logical'` and that `pg_replication_slots` is empty at teardown.

### Step 11 `feat(postgres): implement SubscribeToAll`
- `SubscriptionAsyncEnumerable`, `SubscriptionTarget`, `AllStreamsTarget`, `Observer`, HWM/catch-up queries.
- Tests `PostgresSubscribeToAllTests` (Postgres-specific copies of the `SubscribeToAll_*` cases that matter most:
  from start catch-up then live, default origin live only, after position exclusive, events appended during catch-up,
  queue overflow recovers, two subscribers). The shared fixture is enabled in step 12 because it cannot be split.

### Step 12 `feat(postgres): implement SubscribeToStream`
- `SingleStreamTarget`, `ReadCatchUpStreamAsync`, `SubscribeToStream`.
- Enable `PostgresEventStoreSubscriptionTests : EventStoreSubscriptionTests<StreamPosition, LogPosition>` (drops and
  recreates the schema per test, as Mongo drops the database); delete the step-11 duplicates it supersedes. Extra:
  `SubscribeToStream_AfterPosition_ResumesByStreamPositionAfterFellBehind` (capacity 1, other streams interleaved).

### Step 13 `feat(postgres): recover from replication feed loss`
- End-to-end reset path, `EventLogFeedResumePolicy` tuning, `RetryDelay` option (tests use 100 ms).
- Integration tests: walsender terminated via
  `SELECT pg_terminate_backend(active_pid) FROM pg_replication_slots WHERE slot_name LIKE 'dbx_%'` while 50 events are
  appended across the outage -> at least one `FellBehind`, then every event exactly once in order, then a live event;
  terminated while idle -> `FellBehind` then `CaughtUp`; two subscribers both recover; missing publication -> first
  `MoveNextAsync` throws, and a later subscription on the same store works once the publication exists.

### Step 14 `test(postgres): add benchmark fixture`
- `PostgresEventStoreBenchmarkTests : EventStoreBenchmarkTests<...>` (`[Explicit]`) plus an explicit
  `SubscribeToAll_MeasureLiveLatency`; add the Postgres store to `benchmarks/DomainBlocks.EventStore.Benchmarks`.

### Step 15 `docs(postgres): add README`
- `src/DomainBlocks.EventStore.PostgreSQL/README.md`: guarantees, schema, options table, server requirements for
  subscriptions, why temporary slots, resume semantics, documented limitations (NUL/`\u0000`, writes only via the
  function, cancellation after enqueue).

Logging (`LogMessages.cs`, source-generated like Mongo's) is added within the step that introduces each component
rather than as a separate commit.

## Verification

Docker Desktop must be running (Testcontainers pulls `postgres:17` on first run). The fixture reads the image from
`DBX_POSTGRES_IMAGE` (default `postgres:17`); run the suite once with `postgres:14` before finishing to prove the
minimum version claim.

```powershell
dotnet build DomainBlocks.slnx -warnaserror
dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Unit
dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration --filter "FullyQualifiedName~AppendFunctionTests"
dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration                       # everything, at the end
$env:DBX_POSTGRES_IMAGE = "postgres:14"; dotnet test tests/DomainBlocks.EventStore.PostgreSQL.Tests.Integration
dotnet test tests/DomainBlocks.EventStore.MongoDB.Tests.Integration                           # unchanged, sanity
```

Per step: run the tests named in that step, then `git add` only that step's files and commit with the given subject
and a body explaining the why (wrapped at 70), ending with the required `Co-Authored-By` / `Claude-Session` trailers.

## Risks to verify early (first steps that touch them)

- Step 4: PL/pgSQL subscripting of variable-length array parameters (`text[]`, `jsonb[]`) may be O(n) per access
  rather than O(1); time a 500 x 10 batch and fall back to copying into local variables or `unnest ... WITH ORDINALITY`.
- Step 4: `jsonb[]` parameters from `string?[]`; fall back to `text[]` with `::jsonb` casts.
- Step 10: binary-mode `ReplicationValue.Get<string>()` for jsonb and `Get<DateTime>()` for timestamptz on a
  replication connection; fall back to `binary: false` with text parsing.
- Step 10: Testcontainers `WithCommand("-c", "wal_level=logical")` replacing vs appending the image command
  (the postgres entrypoint prefixes `postgres` for `-`-args); the fixture asserts `wal_level`.
- Step 10: cancelling the replication enumeration and disposing the connection must return promptly, otherwise the
  last detach blocks; measured by the slot-drop test.
- Step 13: what `pg_terminate_backend` surfaces client-side (57P01 vs wrapped IOException); the resume policy handles
  both and the test pins it.
- Slot creation waits for in-flight transactions: append tests that hold a transaction open must not run concurrently
  with subscription fixtures (NUnit runs fixtures sequentially by default; keep it that way).

## Edge cases and documented limitations

- NUL in stream ids and `\u0000` in JSON are rejected client-side (`ArgumentException`); Postgres `text`/`jsonb` cannot store them.
- `AppendBatchSize` bounds requests, not events; a single huge request is passed as-is (1 GB parameter limit).
- Caller cancellation/timeout after enqueue does not un-commit the request.
- Function must not be called inside a caller-managed transaction (breaks lock/commit coupling); the store never does.
- Repeated commit id within one batch: second occurrence succeeds as a duplicate and writes nothing. Note: the MongoDB
  `AppenderPolicy` has a latent bug here (second occurrence is neither completed nor stamped); out of scope, worth a
  follow-up issue.

## Follow-ups (not in this plan)

- Consolidate the copied fan-out types (observer/subject interfaces, ref-counted subject, correlation ids, bounded
  observer) and the subscription enumerable into a shared `DomainBlocks.EventStore.Subscriptions` project used by both
  MongoDB and PostgreSQL; likewise a generic `BatchingAppender<TRequest>` shared with the sequencing library.
- LISTEN/NOTIFY + tail-read feed as a second `IEventLogFeed` for deployments without logical decoding.
- Run `tests/DomainBlocks.EventSourcing.Tests.Integration` and the BenchmarkDotNet project against PostgreSQL.
