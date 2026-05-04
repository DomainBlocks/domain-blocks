# Mongo Event Store Design

Provides a globally ordered event log backed by a MongoDB replica set.

## Goals

- No services beyond MongoDB.
- Globally ordered event positions assigned atomically, providing a total order across historical replays and change
  stream observations.
- Correct under concurrent writes, duplicate submissions, and optimistic concurrency conflicts.

---

## Collections

### `dbx_event_log` – the authoritative event log

Every committed event is stored here. The `_id` field is the global position - a contiguous, strictly increasing integer
assigned atomically to each event when committed. Each document also carries a `streamId`, `streamVersion` (per-stream
sequence number), `commitId`, `commitIndex`, `eventName`, `eventData`, `metadata`, and `writtenAtUtc`.

Two indexes are maintained:

- **`event_log_stream_id_stream_version_ux`** - a unique compound index on `(streamId, streamVersion)`. This is the
  mechanism by which optimistic concurrency conflicts are detected.
- **`event_log_commit_id_ix`** - an index on `commitId` for efficient duplicate detection.

### `dbx_sequences` – sequence counters

A collection of sequence counter documents, each identified by a string `_id`. The event store uses a single counter
document (`_id: "event_log_position"`) to track the next available global position. Each time a batch is committed, the
counter is incremented atomically using `findOneAndUpdate` with a `$inc` operator, which returns the previous value.
This returned value is used as the starting position for the range of global `_id` values assigned to the events in that
batch.

---

## Write Path

1. A caller invokes `AppendToStreamAsync` with a `streamId`, events, and an optional `ExpectedStreamState`
   (e.g. `StreamDoesNotExist`, `StreamExists`, a specific version, or `Any`). The client encodes the events into BSON
   documents and enqueues them as an `AppendEntry` onto an internal bounded channel, then awaits a
   `TaskCompletionSource` for the result.

2. `MongoSequencedAppender` runs a single background append loop that drains the channel in batches.

3. **Pre-commit (`OnBatchCommittingAsync`)** - before each batch is committed, `AppendToStreamPolicy` runs a pre-commit
   query against `dbx_event_log` (using `ReadConcern.Majority`, `ReadPreference.Primary`):
    - A `Distinct` query collects any `commitId` values that already exist - duplicate detection.
    - An aggregation pipeline (`$match` + `$group` with `$max`) retrieves the current head `streamVersion` for each
      affected stream.

   These two queries run concurrently.

4. With the query results in hand, the policy inspects each entry in the batch:
    - **Duplicate** (`commitId` already exists): the entry's `TaskCompletionSource` is completed successfully
      immediately; the documents are excluded from the commit.
    - **Expected stream state mismatch** (`ExpectedStreamState` does not match the actual stream state): the entry's
      `TaskCompletionSource` is faulted with a `StreamAppendConflictException`; the documents are excluded from the
      commit.
    - **Accepted**: `streamVersion` and `writtenAtUtc` fields are stamped onto each document in memory. Per-stream
      versions are tracked across the batch so multiple appends to the same stream in one batch are assigned
      consecutive versions.

5. **Commit** - a MongoDB multi-document transaction is opened (`ReadConcern.Snapshot`, `ReadPreference.Primary`,
   `WriteConcern.WMajority` with journaling):
    - A range of sequence numbers is claimed atomically via `findOneAndUpdate` (`$inc`) on the sequence counter
      document, inside the transaction. Any concurrent transaction that also modifies the sequence document will trigger
      a write conflict, causing one of them to be retried. **This serializes concurrent commits through the sequence
      counter, and is the mechanism by which we achieve a total order across historical replays (sorted by `_id`) and
      real-time change stream observations.**
    - Each document is assigned its global `_id` (position) from the claimed sequence range.
    - All documents are inserted via `InsertMany` (ordered) into `dbx_event_log`.
    - The transaction is committed, with automatic retry on transient errors.

6. On success, all accepted `TaskCompletionSource` instances are completed. `AppendToStreamAsync` returns to the
   caller.

7. **Conflict handling** - if `InsertMany` raises a duplicate key error:
    - If the conflicting index is `event_log_stream_id_stream_version_ux` and the `ExpectedStreamState` is `Any` or
      `StreamExists`, the `AppendEntry` is eligible for retry. The policy returns `ConflictResolution.Retry`; the batch
      is retried with the conflicting entry up to a configurable limit with a delay between attempts. On each retry the
      pre-commit query re-reads stream versions so the correct next version is used.
    - Otherwise, the entry is faulted with an appropriate exception and removed from the batch.
    - Transient transaction errors (labelled `TransientTransactionError` by the driver) cause the entire batch to be
      retried transparently.

---

## Read Path

`ReadStreamAsync` queries `dbx_event_log` filtered by `streamId`, optionally bounded by a specific `streamVersion`.
Results are sorted ascending or descending by `streamVersion` according to the requested direction. The collection is
read with `ReadConcern.Majority` and `ReadPreference.Primary`. Reads are bounded by an optional `MaxCount` limit.

If `StreamNotFoundBehavior.Throw` is set and no events are found, a `StreamNotFoundException` is raised.

---

## Extensibility - `IMongoSequencedAppenderPolicy`

The sequencing machinery (`MongoSequencedAppender`) is general-purpose and decoupled from event store concerns.
Application-specific pre-commit logic and conflict resolution are injected via
`IMongoSequencedAppenderPolicy<TContext>`:

- **`OnBatchCommittingAsync`** - inspect or mutate documents before the transaction, and short-circuit individual
  entries (success or failure) where applicable.
- **`OnConflict`** - decide whether a duplicate key error should be retried or faulted.

`AppendToStreamPolicy` is the event store's implementation of this interface.