# Append sequencing: MongoDB, PostgreSQL and Marten

> **Generated note.** Written by Claude Code (Claude Opus 5.5) on 24 September 2026 at the request of the author, on
> branch `feature/filtering2` at `60eee82`. It covers three questions from one session: how the MongoDB and PostgreSQL
> append paths provide gapless global sequencing across concurrent appenders, how to keep PostgreSQL readers from
> seeing commits a failover could lose, and how the PostgreSQL approach compares with Marten's. Everything here comes
> from reading code; nothing was run against a database. Where it disagrees with the code, the code is right.

Sources read:

- MongoDB: `src/DomainBlocks.EventStore.MongoDB`, and the `DomainBlocks.MongoDB.Sequencing` 1.0.0-rc.2 source in the
  sibling `mongodb-sequencing` repository (its `src` is unchanged between the `v1.0.0-rc.2` tag and `HEAD`).
- PostgreSQL: `src/DomainBlocks.EventStore.PostgreSQL`, chiefly `BatchingAppender.cs`, `AppendBatchCommand.cs` and
  `Sql/append_events.sql`, `Sql/append_helpers.sql`, `Sql/schema.sql`.
- Marten: `JasperFx/marten` at `6ba8f01` (Release 9.39.0).

## Part 1: MongoDB and PostgreSQL compared

### Verdict

Both stores meet the core goal. Global positions have no gaps and are assigned in commit order, so no reader can see
position N+1 before N. That holds for any number of appenders in one process or across processes, for the same
reason in both: every append claims one counter row, holds it until commit, and advances it in the same transaction as
the inserts.

The two differ in everything around that core: per-stream version checks, retries, idempotency and behaviour under
contention. PostgreSQL is the more robust design. The MongoDB side has one real bug and several weaker guarantees.

### How each works

| | MongoDB | PostgreSQL |
|---|---|---|
| Counter | `$inc` on a counter document via `findOneAndUpdate`, first statement of a snapshot transaction | `SELECT … FOR UPDATE` on the `sequences` row, then an `UPDATE` in the same transaction |
| When appenders collide | The second fails at once with a write conflict (`TransientTransactionError`), aborts and retries straight away | The second waits in the row lock queue |
| Why commit order holds | Snapshot isolation, first committer wins: a transaction can only claim the counter once the previous holder's commit is in its snapshot | The lock is released only after the commit is visible, so the next holder commits later |
| Stream version check | Optimistic, outside the transaction: a read before it (`PreCommitQuery`), backed by the unique `(streamId, streamPosition)` index | Pessimistic, inside the lock: stream heads are read after the lock is taken, so the check is exact |
| Idempotency (commit id) | Checked before the transaction; the `commitId` index is not unique | Checked under the lock; unique partial index `event_log_commit_id_idx` as a backstop |
| One request conflicting in a batch | The whole transaction aborts and the batch is re-run (after 100 ms when retrying) | Reported per request in the result rows; the batch still commits |
| Repeated commit id in one batch | Not handled (finding 1) | First occurrence wins, later ones report as duplicates (`RepeatedCommitIdInBatch_WritesFirstOnly`) |
| `createdAt` | Client clock, taken before the transaction | `clock_timestamp()` after the lock, so it rises with position |
| Durability of what readers see | Majority read concern plus `w:majority, j:true`: readers only see writes that survive failover | Readers see local commits, which may not survive failover to an async replica (part 2) |

MongoDB detail. The appender (`MongoSequencedAppender`) drains a bounded channel into batches of up to `MaxBatchSize`
requests. For each batch the policy (`AppenderPolicy.OnBatchCommittingAsync`) runs outside the transaction: it reads
existing commit ids and stream heads, completes duplicates, fails expected-state mismatches and stamps stream
positions. Then one transaction claims `count` positions from the counter, stamps `_id`, runs an ordered `insertMany`
and commits, retrying the commit on `UnknownTransactionCommitResult`. A duplicate-key error on the stream index goes to
`OnConflict`, which retries `Any`/`Exists` requests and fails the rest.

PostgreSQL detail. `BatchingAppender` drains the same kind of channel and makes one autocommit call to
`append_events` per batch. The function checks the isolation level is `READ COMMITTED`, validates the arrays, takes
the sequence row lock, probes existing commit ids, then runs a single statement: it reads every stream head, decides
each request through a recursive CTE that carries heads forward per stream, inserts the accepted events with
contiguous positions, advances the sequence by exactly the rows inserted and returns one result row per request.

### Findings, most severe first

**1. MongoDB: a repeated commit id in one batch writes a duplicate event that breaks reads.**

- `AppenderPolicy.cs:38` skips the second request with the same commit id with `continue`. It is neither completed nor
  given a stream position.
- `PrepareCommitAsync` (`MongoSequencedAppender.cs:263-275` in `mongodb-sequencing`) commits every request that is not
  completed, so its documents are inserted with a global position but no `streamPosition`.
- For a single-event request nothing catches this, because the `commitId` index is not unique.
  `EventLogDocument.StreamPosition` (`EventLogDocument.cs:34`) then throws on that document in every read or
  subscription that reaches it.
- A multi-event duplicate instead collides with itself on `(streamId, null)`, which triggers three whole-batch retries
  and then an `AppendConflictException`.
- The realistic trigger is a caller that times out and retries with the same commit id while the original request is
  still queued, since a timeout does not remove it from the channel.
- The contract test `AppendAsync_SameCommitIdTwice_WritesOnce` covers two appends in sequence only, so it does not
  catch this.

**2. MongoDB: the idempotency check uses two separate reads, with no backstop.**

- `PreCommitQuery.cs:54-65` runs the commit-id `distinct` and the stream-head `$group` as two parallel queries, so they
  can see different points in time.
- If another process commits the same commit id between the two reads, the appender misses the duplicate but sees the
  new head, stamps positions after it, and inserts the events a second time.
- PostgreSQL avoids this by checking under the lock with a unique index behind it.
- The window is narrow and needs a retry across processes. A unique `commitId` index on `commitIndex: 0` documents
  would close it, as PostgreSQL does.

**3. MongoDB: an append with expected state `Any` can fail under contention.**

- Because the stream check is optimistic, a concurrent writer on the same stream causes a unique-index conflict.
  `OnConflict` retries `Any`/`Exists` appends only `MaxConflictRetries` (3) times, after which the request fails with a
  generic `AppendConflictException` (`MongoSequencedAppender.cs:361-371`).
- Each retry holds back the whole batch (up to 500 requests) by `ConflictRetryDelay` (100 ms).
- In PostgreSQL, `Any` never conflicts and a conflict never delays other requests.

**4. MongoDB: appenders in different processes spin rather than queue.**

- The `TransientTransactionError` loop (`MongoSequencedAppender.cs:286-309`) has no limit and no backoff. While one
  appender holds the counter through its insert and majority commit, every other appender repeatedly starts a
  transaction, fails to claim the counter and aborts.
- Ordering stays correct, but load on the primary grows with the number of appenders. The sequencing README's
  benchmark shows it: throughput falls as appender instances increase.
- PostgreSQL appenders wait in the lock queue instead.

**5. PostgreSQL: with async replication, the no-gaps promise only holds while the primary survives.**

- The live feed and reads see a batch as soon as it commits locally. If the primary fails over to an async standby,
  positions consumers already saw can be lost and later reused with different events.
- MongoDB's majority read and write concerns rule this out.
- This is a deployment issue rather than a code bug. Part 2 sets out the fix.

**6. Smaller points.**

- **PostgreSQL, transaction enlistment.** The rule that the append command is never enlisted in a caller's transaction
  (`AppendBatchCommand.cs:11-13`) is not enforced. Npgsql enlists by default, so a store built inside an async-flowing
  `TransactionScope` would give the append loop that ambient transaction, and the sequence lock would be held until it
  ends. `Enlist=false`, or suppressing flow when the loop starts, would close this.
- **PostgreSQL, transient retry.** After an unknown outcome the batch is retried once (`BatchingAppender.cs:179-185`).
  Requests already appended come back as duplicates, which is correct. A request that conflicted the first time is
  re-checked against the new head and may now append. That is consistent with optimistic concurrency, just not the
  first outcome.
- **Both, timeouts.** A timeout or cancellation does not dequeue the request, so it may still commit. `IAppender`
  documents this for PostgreSQL; MongoDB does not, and it is what triggers finding 1.
- **MongoDB, head lookup cost.** `$match` plus `$group $max` reads every event of each stream. PostgreSQL's
  `get_stream_heads` (lateral `max()` over the index) costs the same per stream whatever its length. This is cost, not
  correctness.
- **MongoDB, counter setup.** `EnsureInitializedAsync` creates neither the sequences collection nor the counter
  document; the first append creates them inside its transaction. PostgreSQL creates the row in `schema.sql`.

### Not problems

- **Gaps from failed transactions (both).** Aborted or conflicting transactions leave no gap. MongoDB rolls back the
  counter increment with the inserts. PostgreSQL advances the counter by exactly the rows inserted and leaves it alone
  when a batch appends nothing.
- **Position uniqueness (both).** It is enforced independently of the counter (`_id` in MongoDB, the primary key in
  PostgreSQL), so a drifted counter fails loudly instead of corrupting the log.
- **Isolation level (PostgreSQL).** The `READ COMMITTED` guard in `append_events` correctly protects the
  wait-then-reread behaviour of `FOR UPDATE`.

Fix findings 1 and 2 first. Moving `RepeatedCommitIdInBatch_WritesFirstOnly` into the shared contract suite would
catch finding 1 against MongoDB.

## Part 2: keeping PostgreSQL readers off commits a failover could lose

MongoDB gets this from majority reads and writes. PostgreSQL needs the equivalent in three places, table reads, the
logical live feed, and a few code paths in the store that can undo it.

### 1. Table reads: synchronous replication (configuration only)

With `synchronous_standby_names` set and `synchronous_commit = on`, PostgreSQL does not make a transaction visible to
other sessions until a synchronous standby has flushed it. `ReadAll`, `ReadStream`, the catch-up high-water mark and
the `AppendAsync` acknowledgement then only see or report batches that survive failover.

- Use quorum mode, for example `ANY 1 (s1, s2)`, so losing one standby does not block appends.
- `on` is enough. `remote_apply` only matters if readers query a standby.
- The failover tool must only promote a synchronous standby and must not fall back to async replication when none is
  available: Patroni `synchronous_mode: true` with `synchronous_mode_strict: true`; CloudNativePG
  `dataDurability: required`.

### 2. The live feed: not covered by synchronous replication

A logical walsender streams everything flushed to local disk, including a commit still waiting for the standby. The
live feed can therefore deliver a batch before it is replicated, and even before readers on the primary can see it.

- **PostgreSQL 17+.** Set `synchronized_standby_slots` to the synchronous standbys' physical slots. The walsender then
  holds changes back until those standbys confirm, with no code change. It waits for every listed slot rather than a
  quorum, so a dead standby stalls the feed, though not appends.
- **Any version (a code change).** Gate the live feed on visibility. Before delivering a received transaction, check
  its last position is visible on the primary, for example
  `SELECT EXISTS (SELECT 1 FROM event_log WHERE position = $1)`. Commits become visible in position order, so one
  primary-key lookup covers everything below that position. It costs one round trip per feed batch and applies the same
  rule to the feed as to reads.

### 3. Code paths that would undo the protection

If a backend waiting for the standby is cancelled, PostgreSQL logs that the transaction "has already committed locally,
but might not have been replicated" and makes the commit visible anyway. Two places can send that cancel:

- **Npgsql's command timeout.** `AppendBatchCommand` does not set `CommandTimeout`, so the 30 s default applies, and on
  timeout Npgsql sends a cancel to the server. A standby that stalls for 30 s would publish unreplicated batches. Set
  `CommandTimeout = 0` on the append command; the caller-facing limit is already `AppendOptions.Timeout`, which only
  stops waiting.
- **The stop token.** `BatchingAppender.ProcessBatchAsync` passes the stop token into `ExecuteReaderAsync`, so
  disposing the store mid-commit sends the same cancel. Do not pass it to the database call. On dispose, stop taking
  requests and let the in-flight batch finish.

### 4. Fail loudly if it happens anyway

Replication slots are temporary, and subscriptions resume from the last position they delivered. If positions are lost
in a failover and then reused, a subscriber silently skips the new events at those positions and keeps having processed
events that no longer exist.

When the feed is re-established, the subscription could check the event at its last delivered position still has the
same `commit_id`, and fail if it does not. It already knows that commit id, so the check is cheap. Consumers that keep
their own checkpoints would need to store the commit id with the position to do the same.

Do sections 1 and 3 regardless. For the feed, use `synchronized_standby_slots` if PostgreSQL 17 can be required;
otherwise the visibility gate is the version-independent fix. Section 4 is a cheap safeguard against misconfiguration.

## Part 3: the PostgreSQL append compared with Marten

### How Marten appends

Marten's global order is `mt_events.seq_id`, taken from a PostgreSQL sequence, `mt_events_sequence`, with `nextval`.
There is no global lock. Appends to a stream serialize on that stream's `mt_streams` row and the unique
`pk_mt_events_stream_and_version` index. Appends to different streams run fully in parallel.

- **Rich mode** (`RichEventAppender`) reserves every sequence number up front in a separate query,
  `select nextval(...) from generate_series(1, n)` (`EventSequenceFetcher.cs:22`), works out stream versions in the
  client, then writes one `INSERT` per event and checks the stream version with
  `update mt_streams set version = … where id = … and version = …`.
- **Quick mode** (`QuickAppendEventFunction.cs`) makes one `mt_quick_append_events` call per stream. The function reads
  the stream row, optionally checks an expected version, then calls `nextval` and inserts once per event and updates
  the stream version. `UseExclusiveLockOnConcurrentAppends` adds `FOR UPDATE` to the version read.
- Appends are part of the session's unit of work, so events, documents and inline projections commit in one
  transaction that the application controls.
- There is no commit-level idempotency. An optional unique index on event id (`EnableUniqueIndexOnEventId`) turns a
  retried append into an error rather than a no-op.

A sequence is not transactional, and numbers are handed out before commit. So `seq_id` has gaps wherever a transaction
rolls back after drawing numbers, and commits can land out of order: 11 can be visible while 10 is still in flight.
Marten's own code documents both. The comment at `QuickAppendEventFunction.cs:160-170` describes a losing concurrent
append that "fired `nextval` … before the 23505 raised … leaving a permanent gap that stalls the async daemon's
high-water detector forever (#4749)". The doc comment on `UseExclusiveLockOnConcurrentAppends` describes the same race.
The Quick function now checks versions before `nextval` where an expected version is given, but an append without one
that loses on the unique index still leaves a gap, as does any rolled-back transaction.

Marten handles this on the read side. The async daemon's `HighWaterDetector` (about 1,200 lines) advances a "high water
mark" only through contiguous `seq_id`s and holds at a gap. Since #4953 it skips a gap only after it has been stale for
`StaleSequenceThreshold` and `GapLivenessProbe` finds no transaction that could still fill it. The probe looks at
`pg_locks`, `pg_stat_activity` and `pg_current_snapshot()`, with exceptions for idle sessions and the daemon's own
advisory locks. `SkipStaleGapsDespiteLiveTransactionsAfter` knowingly skips past live transactions, and the detector
logs that "events committed later inside the skipped range will NOT be projected". The pre-#4953 wall-clock skipping
is kept behind `UseTransactionEvidenceForGapSkipping = false`.

### Side by side

| | DomainBlocks PostgreSQL | Marten |
|---|---|---|
| Global order source | One row, locked until commit, advanced in the append transaction | A PostgreSQL sequence (`nextval`), outside transactions |
| Gaps | None, by construction | Yes, from rollbacks and lost races |
| Order of visibility | Commit order equals position order | Positions can become visible out of order |
| What a global-order reader needs | `position > checkpoint` | High-water detection, gap holding, evidence-based skipping |
| Write concurrency | All appends serialized; batching recovers throughput | Parallel across streams; per-stream row contention only |
| Commit cost | One WAL flush (and standby round trip, if synchronous) per batch, inside the lock | Concurrent commits share WAL flushes (group commit) |
| Appending with other writes in one transaction | Not possible: autocommit on a background loop, never enlisted | Yes: events, documents and inline projections in one unit of work |
| Stream version check | Under the global lock, exact; `Any` never conflicts | Per stream, optimistic by default; `FOR UPDATE` is opt-in |
| Idempotency | Commit id, checked under the lock, unique partial index | None built in; unique event id is opt-in and errors on retry |
| Partial failure in a batch | Per-request results; the batch still commits | A session's transaction succeeds or fails as a whole |
| Privileges for reliable reads | None | `pg_stat_activity` visibility across roles (`pg_read_all_stats`) for a full liveness probe |

### Where we have the advantage

- **Correctness is structural, not inferred.** Every committed position is final and in order, so a checkpoint is exact
  and the catch-up-to-live handoff tiles with no overlap or loss (`SubscriptionAsyncEnumerable.cs`, remarks). Marten's
  readers must guess whether a gap is a slow transaction or a dead one. They choose between stalling and risking loss,
  and depend on monitoring views that role redaction can hide.
- **No projection stalls from slow writers.** In Marten, one long transaction holding a reserved `seq_id` holds back
  every projection until it ends; `idle_in_transaction_session_timeout` is the recommended backstop. Here a slow caller
  cannot hold anything, because the lock only exists for the duration of one `append_events` call.
- **Simpler consumers everywhere.** Anything reading the log by position, whether our subscriptions, ad hoc SQL or an
  external CDC consumer, gets the guarantee for free. With Marten only the daemon has the gap logic.
- **Append semantics.** Idempotent commit ids, per-request outcomes that do not abort a batch, and an `Any` append that
  cannot fail on concurrency.

### Where Marten has the advantage

- **Write scalability.** Marten appends to different streams never wait for each other, and their commits share WAL
  flushes. Ours are serialized globally, and every batch's WAL flush, plus the standby round trip once synchronous
  replication is on, sits inside the lock. Throughput is capped at roughly the batch size divided by that critical
  section. Batching is our stand-in for group commit: fine for one busy process, weaker when many processes each send
  small batches that queue on the lock.
- **Transactional composition.** Marten appends atomically with documents and projections in the caller's transaction,
  which is what outbox-style integrations rely on. Ours cannot join a caller's transaction without holding the global
  lock for that transaction's lifetime, so it deliberately never does.
- **Latency for a single append.** A Marten append is its own round trip. Ours also waits in the queue and, under
  load, for the lock.
- **Maturity.** Marten's gap handling, however complex, is exercised by a large user base. Ours is new.

### Verdict

For what this store sets out to be, a log whose global order consumers can trust with a plain checkpoint, the design is
an advantage. Marten gets write parallelism and transactional composition by giving that guarantee up, then spends a
large and still-changing amount of machinery trying to win it back on the read side, with a trade-off between stalls
and possible loss that never fully goes away. We pay once, on the write path, with a global serialization point.

That cost is real and has a ceiling. The work that decides whether it matters is to measure the lock's critical section
per batch, especially with synchronous replication enabled (part 2), and throughput with several processes appending
at once. If the ceiling is too low for a target workload, the options that keep the guarantee are larger or
time-coalesced batches (`AppendBatchingDelay`), or one appender per database shared by all processes. A Marten-style
sequence would give up the guarantee.
