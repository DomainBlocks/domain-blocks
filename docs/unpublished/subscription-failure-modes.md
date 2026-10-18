# Catch-up subscriptions: failure modes

> **Generated note.** Written by Claude Code (Claude Opus 5.5) on 16 October 2026 at the request of the author, after
> reading the MongoDB r7.0.16 and PostgreSQL 17 sources, and updated on 17 October 2026 to describe the design as
> built. Where it disagrees with the code, the code is right.

## The handover

Every subscription makes the same handover from history to live events:

1. Attach an observer to the store's shared live feed.
2. Read history from the subscription's position up to an end mark.
3. Switch to the observer's queue, dropping events at or below the mark.

Two things must hold. Every event the feed delivered before step 1 must be visible to the read in step 2, or it is
**missed**. Every event the feed delivers must survive a failover, or it is a **phantom**. Both failures are silent.

In both stores the feed and the reads are separate sources that can disagree. The failure modes come from how they
disagree.

## MongoDB

The sources are the change stream and majority reads. Both only show majority-committed writes, so there are no
phantoms. On one primary they agree: the stream never passes the node's commit point, and reads use that point or a
later one. They can disagree only across a failover.

### Failure: late attacher after a failover (missed event)

1. The shared stream opens on primary P1 at optime T0. Every attachment gets T0 as its catch-up anchor.
2. The stream delivers event e at T1, after T0.
3. P1 fails over to P2. The driver resumes the stream on P2 from its resume token without telling us.
4. Subscription S attaches. e was delivered before it joined, so its queue does not have e.
5. P2 has the data, but its commit point is still the one it learned as a secondary, say T0.5. It has written its
   new-term no-op and accepts reads, but cannot advance the commit point until a majority has that no-op.
6. S's catch-up reads with `afterClusterTime = T0`, which T0.5 already satisfies. The snapshot does not contain e, so
   S never sees it.

The window lasts until the no-op is majority replicated, normally milliseconds. Sources: `topology_coordinator.cpp`
2703-2705 (commit point frozen on election) and 2961-2971 (no commit before the term's first optime);
`replication_coordinator_impl.cpp` 1420-1443 (writable before the no-op commits) and 1693-1697 (no wait without
`afterClusterTime`).

This corrects the "later subscriber on a shared connection" case in an earlier review of the change stream design,
which assumed that the read and the stream are served by the same node.

### Solutions

- **Wake-up plus polling (built).** See [the design as built](#the-design-as-built). The live feed comes from reads,
  and a catch-up's session is advanced to the operation time of the reads that delivered events, so the server waits
  until its commit point has passed them.
- **Keep the change-stream feed and anchor on the latest delivered time.** The subject publishes each change's
  `ClusterTime` with an interlocked write before notifying observers. The attachment reads it after the observer has
  joined, and the catch-up waits for it with `afterClusterTime`. A change event's `clusterTime` is the `ts` of the oplog
  entry that committed it, which for a transaction is the commit entry, so waiting for it does not stall. Sources:
  `change_stream_event_transform.cpp` 115, 157 and 422; `document_source_change_stream_unwind_transaction.cpp` 250-254
  and 397. This option keeps the restart path, and errors that escape the driver's own resume still fault
  subscriptions instead of restarting the stream.

## PostgreSQL

The sources are the logical replication feed and queries. A commit goes through these steps in `xact.c`:

1. The commit record is flushed to local disk (1492). The feed can send the commit from here (`walsender.c` 3523).
2. The commit log is updated.
3. The backend waits for the synchronous standby, if there is one (1547).
4. The transaction becomes visible to queries (2356).

The feed can therefore be ahead of reads on a single node, and ahead of the standby.

### Failure 1: late attacher (missed event, any setup)

1. The feed delivers e at step 1.
2. Subscription S attaches. Its queue does not have e.
3. S's catch-up query runs before step 4, so it cannot see e either. S never sees it.

Without a synchronous standby the window is microseconds. With one, it is a standby round trip.

### Failure 2: phantom and position reuse (with standbys)

1. The feed delivers e at position p at step 1, before the standby has it.
2. The primary fails before step 3 completes. The standby is promoted without e.
3. Subscribers have seen an event that no longer exists.
4. The next append on the new primary takes position p. Subscribers that saw e skip the real event.

Nothing in our code can fix this without the right deployment. With asynchronous standbys a failover loses
acknowledged writes anyway. Every solution below therefore assumes `synchronous_commit = on`, a synchronous standby in
`synchronous_standby_names`, and failover tooling that only promotes a synchronous standby. With those, a commit is
visible only once the standby has it.

### Solutions

| Option                             | Missed | Phantoms | Cost                                                   |
|------------------------------------|--------|----------|--------------------------------------------------------|
| Wake-up plus polling               | fixed  | fixed    | one shared query per wake; a commit-time `NOTIFY` lock |
| Visibility gate on the feed        | fixed  | fixed    | one end-of-log query per round trip of commits         |
| PG17 failover slots plus the floor | fixed  | fixed    | permanent slots, PG17, server configuration            |
| Position floor alone               | fixed  | no       | a wait at attach                                       |

- **Wake-up plus polling (built).** See [the design as built](#the-design-as-built). The wake-up is
  `NOTIFY`, which Postgres signals only after the commit is visible (`xact.c` 2356 before 2422). Listeners also skip
  notifications from transactions their snapshot still sees as running (`async.c` 2023-2044). Two costs: notifying
  commits serialize on a cluster-wide lock (`async.c` 905-922), which our appends already do on the sequence row but
  other applications in the same instance would feel; and a full notification queue makes appends fail (`async.c`
  942-945).
- **Visibility gate.** The replication session holds each row until a query on a side connection sees its position,
  caching the highest position seen so one query covers many commits.
- **Failover slots.** `synchronized_standby_slots` makes the walsender wait for the standbys, but only for a permanent
  failover slot. The feed can still be slightly ahead of visibility, so it also needs the floor.
- **Floor alone.** The feed publishes the highest position it has delivered, and a catch-up retries its end read until
  the end reaches that floor. It fixes missed events but not phantoms.

### Failure 3: a commit visible before the standby has it

At step 3 the commit record is already in the primary's WAL, so the transaction can no longer roll back. If the
backend is interrupted while it waits for the standby, PostgreSQL stops waiting rather than hang (`syncrep.c`
288-325):

- **A query cancel.** The client gets the warning "canceling wait for synchronous replication due to user request. The
  transaction has already committed locally, but might not have been replicated to the standby", and `COMMIT` succeeds.
- **A terminate.** The same warning is sent, then the connection closes, so the client does not know the outcome.

Either way the backend goes on to step 4 and the transaction becomes visible. The failure then unfolds like this:

1. An append of e at position p is interrupted while it waits for the standby. e becomes visible.
2. A read returns e, and subscribers process it.
3. The primary fails before the standby has received e. The standby is promoted without it.
4. Subscribers have seen a phantom, and the next append reuses p.

The standby normally receives the WAL moments later, because the cancel stops only the waiting, not the replication.
The danger is a failover in between, which is most likely when waits are long, that is, when the standby is slow.

Causes:

- **Disposing our appender while a commit waits.** The caller's token never reaches the commit, only the appender's
  stop token does. `DisposeAsync` cancels that token (`BatchingAppender.cs` 47 and 76-85), and Npgsql then sends a
  cancel request.
- **A command that times out.** Npgsql's `CommandTimeout`, 30 seconds by default, sends a cancel request too. The
  command then returns normally, and the warning reaches the connection's `Notice` event before it does.
- **An administrator's `pg_cancel_backend` or `pg_terminate_backend`, or a fast shutdown.**
- **A failover manager emptying `synchronous_standby_names`.** That releases every waiting commit with no warning at all
  (`syncrep.c`, `SyncRepUpdateSyncStandbysDefined`). Non-strict Patroni, CloudNativePG with `preferred` and
  pg_auto_failover by default all do this when they lose the last standby, and pg_auto_failover also cancels the
  blocked commits.
- **A primary that restarts while commits wait.** It marks them committed during recovery.

Not a cause: `statement_timeout` is disabled before the commit (`postgres.c`, `finish_xact_command`). A client
disconnect isn't either, because the wait doesn't check for one.

#### Spike results

Run on PostgreSQL 17.11 with `synchronous_standby_names` naming a standby that does not exist:

| Statement                                             | Waits  | Cancelled             | Terminated               |
|-------------------------------------------------------|--------|-----------------------|--------------------------|
| `insert into t values (1)`                            | yes    | warning, `INSERT 0 1` | not run                  |
| `select pg_current_xact_id()`                         | **no** | returned at once      | not run                  |
| `select pg_logical_emit_message(true, 'barrier', '')` | yes    | warning, then result  | warning, connection lost |
| `update barrier set n = n + 1 where id = 1`           | yes    | warning, `UPDATE 1`   | warning, connection lost |
| the update with `synchronous_commit = local`          | no     | returned at once      | not run                  |

While the insert waited, other sessions could not see its row. After the cancel they could, which confirms the
failure above. `pg_logical_emit_message` works at `wal_level = replica` and for a role without superuser. Plain
reads and `select 1` never waited.

A second run used a real synchronous standby (`synchronous_standby_names = 'standby1'`, streaming, `sync`). Appends
took their position from a counter row in the same transaction, as the store does.

- **The barrier covers an earlier cancelled commit.** With the standby paused, append e waited and was cancelled, and
  a plain read on the primary returned it. A barrier started next waited for as long as the standby stayed paused, and
  returned only after it resumed. At that point the standby's flush position was past e's WAL, and the standby's log
  contained e.
- **Without the barrier, a failover produces a phantom and reuses its position.** With the standby disconnected,
  append e2 at position 3 was cancelled and a plain read on the primary returned it. A barrier started next was still
  waiting when the primary was killed, so it never reported success. After promotion, the new primary's log ended at
  position 2, and its next append took position 3 for a different event. A subscriber reading without the barrier
  would have delivered e2 and then skipped the real event at position 3.

#### Options

- **Never cancel commits in the appender, and log the warning.** Do not pass the caller's token to the commit, and do
  not let a statement timeout cover it. Watch Npgsql's `Notice` event for the warning. This removes our own cause but
  not an administrator's.
- **A replication barrier before delivering (built).** After a read and before delivering its rows, commit
  one tiny transaction that writes WAL and sets its own `synchronous_commit = on` with `SET LOCAL`, such as
  `SELECT pg_logical_emit_message(true, 'barrier', ''), current_setting('synchronous_standby_names')`. Its commit
  waits for the synchronous standbys to flush its commit record (`xact.c` 1488-1490 and 1546-1547). Every commit the
  read saw wrote its commit record before it became visible, and so before the barrier's record. The standby flushes
  WAL in order, so once the barrier returns, the standbys have everything the read returned, including commits whose
  own wait was cancelled, and even commits made with `synchronous_commit = off`. The barrier is our own statement, so
  if it is cancelled we see the warning, and if it is terminated we see the error. Either way we retry it and deliver
  nothing until it succeeds. This gives subscribers the same guarantee as a MongoDB majority read.

  The barrier must write WAL before its commit record. Taking an xid alone is not enough: `wrote_xlog` is computed
  before the commit record is written (`xact.c` 1338), so a transaction that only took an xid commits without
  flushing or waiting.

- **Compare standby LSNs instead.** Read `pg_current_wal_insert_lsn()` after the read, then wait until the synchronous
  standbys' `flush_lsn` in `pg_stat_replication` passes it. This writes no WAL and uses no xids, so it also works for a
  read-only role, which makes it the fallback if the subscription's role must be read-only. But it needs monitoring
  privileges to see the LSNs, has to reimplement the `FIRST n` and `ANY n` rules that the barrier gets for free, and
  has to poll, because PostgreSQL has no SQL function that waits for a standby to reach an LSN.
- **Detect it on resume.** Checkpoint the event's id with its position, and when resuming, check that the event at
  that position still has that id. A mismatch means the history changed under the subscriber, so fail loudly instead
  of skipping. This detects every residual case, including hardware failure beyond what replication covers, but
  prevents none.

The barrier costs one xid, a few bytes of WAL and one standby round trip per tail batch and per catch-up. While no
synchronous standby confirms, it blocks, so subscribers stall instead of seeing data that could be lost, as in MongoDB
when no majority is available.

With no standby configured, the barrier's commit would only flush the WAL locally. That matters only for appends made
with `synchronous_commit = off`, which a crash could undo after a subscriber saw them. As built, appends set
`synchronous_commit` on themselves, so they are flushed before they become visible, and the barrier runs only when
synchronous standbys are configured. An earlier version had an option that refused an empty
`synchronous_standby_names`. It was removed, because blocking or carrying on without a standby is the failover
manager's choice, and the option could not see a manager empty the setting during its wait.

## The design as built

Correctness comes only from reads. A wake-up signal only removes polling latency: a wake that arrives early, late,
twice or never costs at most one poll interval, never an event. Both stores share one implementation in
`DomainBlocks.EventStore.Subscriptions`. A store provides reads and a wake signal through one public interface.

### What a store implements

```csharp
public interface IEventSubscriptionSource<TEvent, TStreamId, TStreamPos, TLogPos> : IAsyncDisposable
{
    ValueTask<TLogPos?> ReadEndLogPositionAsync(CancellationToken cancellationToken);

    ValueTask<TStreamPos?> ReadEndStreamPositionAsync(TStreamId streamId, CancellationToken cancellationToken);

    // A null end reads to the end of the log.
    IAsyncEnumerable<TEvent> ReadAllAsync(
        TLogPos? fromExclusive, TLogPos? toInclusive, EventFilter filter, CancellationToken cancellationToken);

    IAsyncEnumerable<TEvent> ReadStreamAsync(
        TStreamId streamId, TStreamPos? fromExclusive, TStreamPos toInclusive, EventFilter filter,
        CancellationToken cancellationToken);

    // The position of the last event in each append the store hears of.
    IAsyncEnumerable<TLogPos> ListenForAppendsAsync(CancellationToken cancellationToken);

    bool IsTransientError(Exception exception);
}
```

Each pair of reads uses one kind of position: the log's, or the stream's. The reads never wait and keep no position.
The shared code creates one source for each reader, so one for the tail and one for each catch-up, and makes one read
at a time on a source. A source reuses the event it hands out, which is valid until the next one is read.

A source must guarantee one thing: a read, including a read of an end position, sees every event that another of the
store's sources has already returned. Without it, a catch-up that begins after the tail delivered an event could miss
that event.

A source event is an event as its store holds it. A filter evaluates it before it is decoded, and its payload and
metadata are decoded together on first access:

```csharp
where TSourceEvent : IFilterableEvent, IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
```

### The tail

`EventLogTail` reads a store's log once for all of its live subscriptions. It starts on the first attach and stops on
the last detach, and a tail that has failed is replaced on the next attach. It reads the end of the log when it starts,
then reads the rest of the log in order through `EventLogCursor`, handing each event to every attached observer.

- **Waiting.** Once it has read every event, the cursor waits for an append after the later of its own position and
  the highest position heard of before it read. It also reads again every `PollInterval`, 1 second by default, in case
  a wake was lost.
- **Transient errors.** The cursor retries from the last event it handed on, with backoff from `RetryDelay` (1 second)
  to `MaxRetryDelay` (1 minute), and logs each retry. It gives up once it has gone `RetryTimeout` (5 minutes) without
  handing on an event or completing a read, so a store that stays unreachable fails the subscriptions instead of
  stalling them, while a connection that keeps dropping but still delivers events is not given up on. A failure that
  is given up on, and any error that is not transient, is passed to every observer and fails the tail.
- **The wake signal.** `AppendSignal` runs the store's listener, keeps the highest position it has heard of, and
  wakes any number of waiters. It never fails. It restarts the listener with backoff after any error or end, and its
  backoff starts again once the listener has heard something. It starts listening before the tail reads the end, so
  that no append between the two goes unheard.
- **Observers.** The observers are held in an immutable array that is replaced by compare-and-swap, so the read loop
  never blocks. An observer that throws is detached.

Since the tail's position survives lost signals, failed reads and reconnects, it never restarts from the present, and
observers need no reset.

### The subscription

`CatchUpSubscriptionFactory` is the public facade that a store composes, with `SubscribeToAll` and
`SubscribeToStream`. Each subscription runs cycles of `CatchUpSubscription`:

1. Attach a new queue to the tail, then detach the previous one, so that the tail never stops between cycles.
2. Read the end position of the log or of the stream, through a source of the subscription's own.
3. Replay from the resume position up to that end. On the first cycle of a subscription from the end, skip the replay
   and resume from the end instead.
4. Yield `CaughtUp`, then deliver the queued live events, skipping positions that the replay covered.

The end is read after the queue is attached, so together they cover every event. When the queue overflows, the replay
still runs to its end, so every cycle makes progress. The cycle ends once the queue is drained. If the subscriber had
caught up, it gets `FellBehind`, and the next cycle resumes after the last event that was delivered or covered.
Losing a connection never causes `FellBehind`. A catch-up retries a transient error in its reads as the tail does, with
the same delays and the same timeout. A replay resumes after the last event it handed on, up to the same end. Any other
error fails the subscription.

### PostgreSQL

- **Appends set their own durability.** `append_events` commits with `synchronous_commit` on, whatever the server,
  database, role or session sets. PostgreSQL makes a transaction visible only after the commit's flush and its wait for
  the synchronous standbys, so an acknowledged append is flushed, and is on the standbys if any are configured. No
  other session can see it before then.
- **Appends are acknowledged after the commit.** The server sends a batch's result rows before it commits, and flushes
  them once they fill its 8 KB output buffer, so the appender reads every row first and completes the requests only
  once the server reports the commit. A batch that does not commit completes nothing.
- **A commit whose wait was cancelled is not acknowledged.** It stands on the primary and sends only a warning. The
  warning has the generic warning code and translated text, so it is recognized by the server function that raised it,
  `SyncRepWaitForLSN`, which the server reports untranslated. Other warnings, such as one raised by a trigger, are
  ignored. A warning that reports no function at all counts, so that a server that stops reporting it fails safe. The
  appender then runs the batch again, which reports the requests as duplicates. That batch still locks the sequence
  row, which writes WAL, so it commits only once the standbys have it, and with it the earlier commit. A subscription
  reads again. The function's name is a server internal that has not changed since PostgreSQL 9.1, and the tests
  cancel a real wait, so a rename would fail them.
- **The wake signal.** `append_events` notifies the channel named after the schema, with the position of its last
  event as the payload, while it still holds the sequence row's lock. The source listens on a connection of its own,
  and runs `UNLISTEN *` before returning it to the pool, because Npgsql resets a pooled connection only when it is
  next used.
- **The replication barrier.** Every read whose result is delivered or used as a position runs in one batch with the
  barrier, which is one implicit transaction: the tail's pages, its start, and each catch-up's end read. A catch-up
  reads only up to an end that a barrier followed, so its replay needs none. The barrier is:

  ```sql
  SELECT set_config('synchronous_commit', 'on', true), pg_logical_emit_message(true, 'dbx_barrier', '')
  WHERE current_setting('synchronous_standby_names') <> '' AND EXISTS (...)
  ```

  Its commit waits until the synchronous standbys have flushed it, and with it every commit the read saw, including
  commits whose own wait was cancelled. The tail hands on a page only once its batch completes, because rows can reach
  the client before the commit waits. A cancelled wait, recognized as above, or a terminated backend fails the read,
  which is retried.
- **Without synchronous standbys, the barrier writes nothing.** Every visible append has already been flushed, so there
  is nothing for it to wait for. That is the case on a single server, and on services whose storage replicates the
  WAL. With synchronous standbys it closes the cases in failure 3. It decides on each read, so it follows the topology
  as a failover manager changes it.

### MongoDB

- **Reads.** Each source reads with majority read concern on the primary, in a causally consistent session of its own.
- **The high-water mark.** The store's sources share a high-water mark of operation times. A source advances the mark to
  each response's `operationTime` before it hands on that response's documents, and advances its session to the mark
  before each read. The server then holds a read until its node's majority commit point has passed the mark. This
  replaces the position floor and retry loop recommended earlier for the late attacher after a failover: the server
  waits, so there is no client loop and no extra round trip.
- **The wake signal.** A change stream of inserts that carries only each document's key, its log position. The source
  yields the last position of each batch.
- **Transient errors.** The errors that retryable reads retry: network errors, a primary stepping down or shutting
  down, a lost cursor, a timeout, and the retryable read error codes. A failed authentication surfaces.

## Deployment

Each store sets durability per operation. The deployment owns only its topology, and the store must talk to the
primary.

- **MongoDB** needs a replica set. Majority reads and the high-water mark are set by the store, and the sequencing
  appender commits with `w: majority` and journaling. Elections promote only a member that has every majority-committed
  write. MongoDB Atlas is an ordinary replica set. Amazon DocumentDB and Cosmos DB's MongoDB API are different engines
  and are untested.
- **PostgreSQL on a single server, or on a service whose storage replicates the WAL** needs nothing more. In Aurora,
  RDS Multi-AZ with one standby, and Cloud SQL HA, the local flush is itself the durable cross-zone write, so a visible
  append survives failover. For Aurora this rests on a commit becoming visible only after its storage quorum has it,
  which AWS documents for the acknowledgement but not for visibility. A test on Aurora PostgreSQL 17.11 confirmed it
  ([Cloud durability checks](postgresql-cloud-durability-checks.md)). Use the writer or cluster endpoint. On a reader
  endpoint, a catch-up could read a replica that lags behind the tail.
- **PostgreSQL with streaming replication** needs synchronous standbys in `synchronous_standby_names` and a failover
  manager that promotes only a standby that has the commits:
  - Patroni with `synchronous_mode`, which promotes only the synchronous standby, or a quorum member that is not behind;
  - CloudNativePG, which promotes the most advanced replica;
  - pg_auto_failover, which promotes by priority and then by position.

  Managed services built this way, such as RDS Multi-AZ DB clusters, Azure Flexible Server HA and Neon, set this up
  themselves.
- **Losing every synchronous standby.** A strict setup blocks commits until a standby returns: Patroni
  `synchronous_mode_strict`, CloudNativePG `dataDurability: required`, and pg_auto_failover with
  `number_sync_standbys` at least 1. A non-strict setup empties `synchronous_standby_names` and carries on unreplicated,
  which releases waiting commits without a warning. Non-strict Patroni then promotes nothing automatically, while
  CloudNativePG `preferred` accepts the risk. The choice is the failover manager's, as it is a replica set's in MongoDB.
- **Asynchronous standbys alone** can lose acknowledged appends in a failover, and subscribers may have seen them.
  Nothing in the store can prevent that.

## Costs

- **One read per wake.** In MongoDB the change stream used to carry each document. With one append in flight, live
  latency rose from about 6.0 ms to 7.0 ms, of which about 0.7 ms is the find's round trip (the same as a `ping` to
  the server) and about 0.1 ms the wake. In PostgreSQL it rose from about 1.0 ms to 1.55 ms.
- **The barrier,** only with synchronous standbys and only for reads that found events: an xid, a few bytes of WAL and
  a standby round trip, in the same round trip as the read.
- **Positions must commit in order.** PostgreSQL appends serialize on the sequence row. In MongoDB the Sequencing
  package claims positions from the counter document and inserts the events in one transaction
  (`MongoSequencedAppender.cs` 288-303 and 393-412 in `mongodb-sequencing` 1.0.0-rc.2). A second transaction that
  claims from the counter gets a write conflict until the first has committed, so it commits later, and its positions
  become majority-visible later.
- **`pg_notify` serializes notifying commits on a cluster-wide lock** (`async.c` 905-922). Our appends already
  serialize on the sequence row, but other applications in the same instance would feel it. A full notification queue
  makes appends fail (`async.c` 942-945).
