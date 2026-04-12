# Mongo Event Store Design

Provides a globally ordered event log backed solely by MongoDB - no additional infrastructure required. Any application
node may accept writes; exactly one leader node establishes canonical order.

## Goals

- No services beyond MongoDB.
- Any node may accept write requests.
- Exactly one node assigns global ordering.
- Readers observe only durably committed history.
- Correct under partial writes, node crashes, and leader failover.

---

## Collections

### `dbx_requests` - proposed appends (staging area)

Written by any node. Each document is a single proposed append: a `commitId`, `streamId`, `expectedStreamState`, and
the event payloads to write. Documents are TTL-expired automatically.

### `dbx_event_log` - the authoritative event log

Written by the leader only. Each document is a single committed event, assigned a monotonic global `position` (`_id`)
and an `epoch` stamp. The position sequence is the global order. Readers and projections tail this collection.

### `dbx_leases` - leader lease

A single document. Holds the current leader's `holderId`, a monotonically incrementing `epoch`, an expiry, and a
`commitPosition` - the high-water mark up to which the leader has durably written and confirmed.

---

## Write Path

1. A client node serialises the append into an `AppendRequest` document (commitId, streamId, expectedStreamState,
   events) and writes it to `dbx_requests`. It then registers a `TaskCompletionSource` keyed by `commitId` and waits on
   it.

2. The leader's `RequestFeeder` picks up the request - either via a catch-up query (on startup, to drain any backlog) or
   via a live change stream feed thereafter.

3. The leader processes requests in batches. A `Prepare` step queries `dbx_event_log` for any already-committed
   `commitId`s (duplicate detection) and the current head version of each affected stream. These reads run concurrently
   with the previous batch's `commitPosition` advancement to keep the pipeline saturated.

4. With the stream versions in hand, the leader checks each request's `expectedStreamState` in memory. Conflicts are
   collected; accepted events are assigned monotonic global positions and written to `dbx_event_log` via a bulk upsert.
   Any duplicates or conflicts are recorded as sentinel entries in the same batch so clients can be notified.

5. After the bulk write, the leader advances `commitPosition` on the lease document via an epoch-fenced update. The
   write is conditional on the lease still being held by the same leader at the same epoch, so a stale leader cannot
   advance the position.

6. The client's `CommitTracker` is watching the `dbx_event_log` and `dbx_leases`change streams. When `commitPosition`
   advances, it flushes all buffered entries up to that position in order, completing (or faulting) the waiting
   `TaskCompletionSource`.

7. `AppendToStreamAsync` returns to the caller.

---

## Leader Election

Nodes compete for the lease via an atomic `findOneAndUpdate` with a CAS filter (`expiresAtUtc ≤ now`). On success,
`epoch` is incremented atomically. The winner becomes the leader for the duration of the lease and renews it
periodically. If renewal fails (e.g. due to expiry), the lease is considered lost and the leader session tears down.

---

## Epoch-Guarded Writes (Split-Brain Protection)

Every event log write is a `ReplaceOneModel` upsert with the filter:

    { _id: <position>, epoch: { $lt: <currentEpoch> } }

This means a position is only written if it does not yet exist, or if it was written by a stale leader with a lower
epoch. Above `commitPosition`, a new leader can safely reclaim any positions left behind by a predecessor. Stale leaders
cannot overwrite positions already claimed by the current epoch.

---

## Commit Acknowledgement

`CommitTracker` runs on client nodes and reacts to a single database change stream filtered on two collections:

- **`dbx_event_log`** - buffers incoming events in a `SortedDictionary<position, entry>`. Entries from stale epochs are
  ignored or evicted.
- **`dbx_leases`** - on `commitPosition` advance: flush all buffered entries up to that position in order, completing
  the relevant `TaskCompletionSource`. On epoch change: purge any buffered entries from the old epoch.

Using `commitPosition` as the flush gate means clients never observe a partially written batch. The TCS wired to
`AppendToStreamAsync` completes (or faults with a conflict exception) only once the leader has declared the batch
durable.

---

## Read Path

`ReadStreamAsync` queries `dbx_event_log` filtered by `streamId` and `position ≤ commitPosition`, sorted by
`streamVersion`. The `commitPosition` is cached in memory on the client (kept current by `CommitTracker`) and falls back
to a live lease read on first access.

---

## Node Roles

| Role           | Behaviour                                                                                            |
|----------------|------------------------------------------------------------------------------------------------------|
| `Client`       | Submits append requests and tracks commit outcomes via the change stream.                            |
| `Leader`       | Competes for the lease and, when held, handles append requests by writing ordered events to the log. |
| `ClientLeader` | Default. Both `Client` and `Leader` roles in a single process.                                       |
