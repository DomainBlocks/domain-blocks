# Catch-up subscriptions: failure modes

> **Generated note.** Written by Claude Code (Claude Opus 5.5) on 16 October 2026 at the request of the author, after
> reading the MongoDB r7.0.16 and PostgreSQL 17 sources. Where it disagrees with the code, the code is right.

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

This corrects the "later subscriber on a shared connection" case in `mongodb-subscription-review.md`, which assumed the
read and the stream are served by the same node.

### Solutions

- **Wake-up plus polling (recommended).** See [below](#recommended-design-wake-up-plus-polling). The live feed comes
  from reads, and the catch-up waits for the position floor.
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

- **Wake-up plus polling (recommended).** See [below](#recommended-design-wake-up-plus-polling). The wake-up is
  `NOTIFY`, which Postgres signals only after the commit is visible (`xact.c` 2356 before 2422). Listeners also skip
  notifications from transactions their snapshot still sees as running (`async.c` 2023-2044). Two costs: notifying
  commits serialize on a cluster-wide lock (`async.c` 905-922), which our appends already do on the sequence row but
  other applications in the same instance would feel; and a full notification queue makes appends fail (`async.c`
  942-945).
- **Visibility gate.** The replication session holds each row until a query on a side connection sees its position,
  caching the highest position seen so one query covers many commits.
- **Failover slots.** `synchronized_standby_slots` makes the walsender wait for the standbys, but only for a permanent
  failover slot. The feed can still be slightly ahead of visibility, so it also needs the floor.
- **Floor alone.** It fixes missed events but not phantoms.

### Remaining hole

If a backend's wait for the standby is cancelled at step 3, the commit becomes visible without the standby having it.
Reads and every option above then show it. Our appender must not cancel commits; an administrator's
`pg_cancel_backend` cannot be prevented.

## Recommended design: wake-up plus polling

Correctness comes only from reads. A wake-up signal only removes polling latency: a wake that arrives early, late,
twice or never costs at most one poll interval, never an event.

### The feed

One tail reader per store reads `position > lastRead` in batches and pushes the rows to the attached observers. It
reads on every wake and on a slow backup poll, which only covers lost wake-ups, so it can be seconds apart.

| Store      | Wake-up signal                                                                 |
|------------|--------------------------------------------------------------------------------|
| PostgreSQL | `pg_notify` in each append transaction, with an empty payload                  |
| MongoDB    | a change stream with `$match` on inserts, projected to `_id` (a ping, no data) |

If the signal fails for any reason, the reader reopens it from now with backoff, and polling covers the gap. A stale
read is harmless to the tail: positions commit in order, so a snapshot is a prefix of the log, and a stale read returns
fewer rows or none.

The feed's contract is: rows in position order with no gaps, or a terminal error. `lastRead` survives lost signals,
failed reads and reconnects, so the feed never restarts from the present and observers need no reset callback.

### The catch-up

A subscription attaches, catches up by reading, and switches to its queue, as today. To close the late-attacher gap:

1. The tail publishes the highest position it has delivered, the floor, with an interlocked write before notifying
   observers.
2. The attachment reads the floor after the observer has joined.
3. The catch-up reads the end in a read context that never goes backwards, retrying with bounded backoff until the end
   reaches the floor. The context is a causal session in MongoDB and one connection in PostgreSQL.
4. It then replays up to that end within the same context.

In PostgreSQL the first read always passes, because the tail delivers only visible rows. In MongoDB a retry covers the
new primary's stale commit point after a failover. A wait that does not end within a timeout fails the subscription.

### Abstractions

This is a sketch, not settled API. Each store provides a read context, a catch-up query and a wake signal:

```csharp
// Wakes the tail reader when new events may exist. Spurious and missed wakes are allowed, because the tail polls
// anyway. The signal handles its own reconnection, so it never fails the feed.
public interface IWakeSignal : IAsyncDisposable
{
    // Completes on a wake or when the timeout elapses, whichever comes first.
    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

// A read context that never goes backwards: a causal session in MongoDB, one connection in PostgreSQL.
public interface IEventLogReadContext<TRow> : IAsyncDisposable
    where TRow : IFeedRow
{
    ValueTask<LogPosition?> ReadLogEndAsync(CancellationToken cancellationToken);

    // Unfiltered and in position order. The tail reader uses this.
    IAsyncEnumerable<TRow> ReadLogAfterAsync(LogPosition? after, int limit, CancellationToken cancellationToken);
}

// Built per subscription. It knows the target (all events or one stream), the filter and the kind of position.
public interface ICatchUpQuery<TEvent, TPos>
    where TPos : struct, IPosition<TPos>
{
    ValueTask<TPos?> ReadEndAsync(IEventLogReadContext context, CancellationToken cancellationToken);

    IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        IEventLogReadContext context, TPos? after, TPos endInclusive, CancellationToken cancellationToken);

    TPos GetPosition(ReadEventContext<string, StreamPosition, LogPosition> context);
}

public interface ISubscriptionStore<TRow>
    where TRow : IFeedRow
{
    ValueTask<IEventLogReadContext<TRow>> OpenReadContextAsync(CancellationToken cancellationToken);
    IWakeSignal CreateWakeSignal();
}
```

`IFeedRow` is an `IFilterableEvent` with a `LogPosition` that can produce a `ReadEvent`. It is valid only during
`OnNext`.

| Store      | Read context     | `ReadLogAfterAsync`                           | Wake signal                          |
|------------|------------------|-----------------------------------------------|--------------------------------------|
| PostgreSQL | one connection   | `position > @after ORDER BY position LIMIT n` | a dedicated `LISTEN` connection      |
| MongoDB    | a causal session | `Find(position > after).Sort().Limit(n)`      | an inserts change stream, `_id` only |

The shared code provides the feed and the subscription:

```csharp
public interface IFeedObserver<in TRow>
{
    void OnNext(TRow row);           // must not block, and must not keep the row
    void OnError(Exception error);   // terminal
}

public interface IFeedAttachment : IAsyncDisposable
{
    // The highest position delivered before this observer joined, or the feed's starting end.
    LogPosition? Floor { get; }
}

// One per store, ref-counted: it starts on the first attach and stops on the last detach.
internal sealed class TailFeed<TRow> where TRow : IFeedRow
{
    public ValueTask<IFeedAttachment> AttachAsync(IFeedObserver<TRow> observer, CancellationToken cancellationToken);
}
```

The tail loop:

```csharp
var lastRead = await context.ReadLogEndAsync(ct);
Volatile.Write(ref _floor, lastRead);

while (true)
{
    var count = 0;

    await foreach (var row in context.ReadLogAfterAsync(lastRead, _batchSize, ct))
    {
        Interlocked.Exchange(ref _floor, row.Position);  // publish before notifying
        foreach (var observer in Volatile.Read(ref _observers)) observer.OnNext(row);
        lastRead = row.Position;
        count++;
    }

    if (count < _batchSize)
        await signal.WaitAsync(_pollInterval, ct);
}
```

Transient read errors retry from the same `lastRead` with bounded backoff. Other errors call `OnError` on every
observer and fault the feed, and the next attach starts a new one.

Each subscription cycle:

1. Attach a new queue observer, then detach the previous one, so the feed is never stopped between cycles.
2. Open a read context, and read `ReadLogEndAsync` until it reaches `attachment.Floor`.
3. Read the end with `query.ReadEndAsync`, replay up to it with `query.ReadAsync`, and yield `CaughtUp`.
4. Drain the queue, skipping positions the replay covered.
5. On overflow, yield `FellBehind` and start the next cycle. On an error, throw it.

The subscription opens its own read context, so the feed knows nothing about catch-ups. If PostgreSQL uses the same
`string`, `StreamPosition` and `LogPosition` types as MongoDB, the shared generics shrink to `TEvent`, `TPos` and
`TRow`.

### What goes away

- Logical replication in PostgreSQL: the slot, the publication, `wal_level = logical`, the REPLICATION privilege and
  pgoutput decoding.
- Change-stream precision in MongoDB: the `hello` optime, the start anchor, resume-token seeding and
  `ChangeStreamHistoryLost` handling.
- The reset callback and its stop reasons. The observer contract shrinks to `OnNext` and `OnError`.
- `ChangeStreamSubject`, `RefCountedChangeStreamSubject`, `EventLogFeed`, `RefCountedEventLogFeed` and
  `ReplicationEventLogSession`, replaced by `TailFeed` and two small wake signals.

### Durability of reads

The design is only as safe as its reads: the feed is made of reads, so a read that a failover can undo is a phantom.

**MongoDB guarantees it.** A majority read returns only data that a majority of voting members have, and a
majority-committed write is never rolled back, because any node that can win an election has it. The store already
reads with majority read concern from the primary (`MongoEventStore.cs` line 99). This rests on two server defaults
and limits:

- `writeConcernMajorityJournalDefault: true`, so a majority commit is journaled. If it is turned off, a write held
  only in memory on a majority is lost if those members crash together.
- No simultaneous data loss on a majority of members, which no replication setting can survive.

**PostgreSQL needs the deployment to guarantee it.** A read on the primary returns whatever is visible there, which
survives a failover only if every visible commit is already on the node that gets promoted:

1. Appends use `synchronous_commit = on` or `remote_apply`. `remote_write` is not enough, because a standby OS crash
   loses an unflushed write. Sessions can lower the setting, so the appender should set it in its own transaction
   (`SET LOCAL synchronous_commit = on`) rather than rely on the server default.
2. `synchronous_standby_names` names a synchronous standby. If it is empty, `on` waits only for the local flush.
3. Failover tooling promotes only a standby that confirmed the commit. With `ANY 1 (a, b)` only one of them may have
   it. Patroni's `synchronous_mode` handles this, but without `synchronous_mode_strict` it falls back to asynchronous
   replication when no synchronous standby is available, which silently drops the guarantee.
4. No standby wait is cancelled. See the remaining hole above.

The store can check items 1 and 2 at startup and warn. Items 3 and 4 depend on how the cluster is operated, so they
can only be documented.

### Costs and prerequisites

- One shared read per wake batch. In MongoDB the change stream used to carry the documents for free. The reads are by
  indexed position on recent documents, which should be cheap, but should be measured against the subscription
  benchmarks.
- Latency of one wake plus one read round trip after a commit becomes visible.
- Positions must commit in order. PostgreSQL appends serialize on the sequence row. In MongoDB the counter document's
  write conflicts imply it, as `mongodb-subscription-review.md` argues; confirm it once in the Sequencing source.
- PostgreSQL needs the deployment in [Durability of reads](#durability-of-reads) to rule out phantoms.
