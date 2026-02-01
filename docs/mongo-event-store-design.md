# Mongo Event Store Design

This document describes the high-level design of the MongoDB-backed commit log used by DomainBlocks. This goal is to
provide globally-ordered, event-sourced history without requiring additional infrastructure beyond MongoDB, while
remaining robust to partial writes, leader failover, and restart scenarios.

## Goals

- No additional services beyond MongoDB (no dedicated appender service).
- Any node may accept writes.
- Exactly one node establishes canonical global order.
- Readers and projections observe only confirmed history.
- Correct under:
    - non-atomic `insertMany`
    - node crashes mid-write
    - leader restarts
    - loss of change stream resume tokens

## Collection 1: `dbx_events` (ingress + payload)

### Purpose

Store the data that makes up a proposed commit:

- advisory ingress marker for a commit (declares intent and expected event count)
- the actual domain event documents

This collection is *not* considered globally ordered truth. It is a durable log of proposed work and the backing store
for event payloads.

### Written by

Any node (all application instances).

### Read by

- The leader to verify completeness before confirming commit
- Historical readers, projections, subscribers to hydrate domain events after observing a confirmed event. This can be
  abstracted away by the library.

### Notes

- Immutable
- Never individually marked as "committed"
- May be partially present if `insertMany` fails mid-batch

### Documents

- All documents share a `commitId` and an `eventName` discriminator.
- `CommitProposed` event (advisory ingress): Declares that a commit of N events is proposed.
- Any arbitrary event that is part of the commit, e.g. domain events.

## Collection 2: `dbx_commits` (authoritative control log)

Provides the single **causally ordered log** that defines:

- Leadership epochs (fencing boundary)
- Commit outcomes (confirmed / rejected)
- Global ordering (via monotonically assigned positions)

This collection is the only collection that projections and subscriptions must tail.

### Written by

**Leader only** - the node holding leadership lease.

### Read by

- All nodes to ack requests, drive subscriptions/projections, and read historical events ordered by global position.
- The leader itself to detect newer leadership and self-correct.

### Documents

- `LeaderElected` event (epoch boundary): Defines the active epoch for subsequent commit outcomes.
- `CommitConfirmed` event: Marks a commit as canonical and assigns global position range.
- `CommitRejected` event: Marks a commit as not canonical.

### Invariants:

- The leader must emit `LeaderElected(epoch=E)` before emitting any outcomes with epoch `E`.

### Ordering and split-brain fencing

Because `LeaderElected` and commit outcomes share one ordered collection, consumers can reject stale/out-of-epoch
outcomes.

A commit is valid **iff** its epoch matches the most recent `LeaderElected` event.

This makes stale leaders self-identifying: any outcome written with an older epoch after a newer `LeaderElected` is
automatically invalid.

## Leadership handshake

1. Acquire leadership via atomic CAS `findOneAndUpdate` operation on lease document
    - Epoch is atomically incremented to `E` on successful acquisition
2. Within a transaction:
    1. Read current lease state with `readConcern: majority`
    2. Assert `epoch == E && holderId == me` via a conditional lease “touch” (CAS) so the transaction cannot commit
       successfully if the lease is concurrently modified.
    3. Reserve the next position `P` atomically (CAS sequences document)
    4. Insert `LeaderElected(epoch=E, position=P, holderId=me)` into commits
    5. Commit
3. Only start confirming/rejecting commits after observing `LeaderElected(epoch=E, position=P, holderId=me)` through
   a change stream watcher.

If the lease document changed at any point during step 2 (i.e. new leader), the transaction fails and
`LeaderElected` with a stale epoch is never published.

Because all `LeaderElected` events are appended to `dbx_commits` only after a fenced leadership handshake
(transactionally asserting the current lease epoch and holder) and are assigned a monotonic `position` at insertion
time, a historical replay of `LeaderElected` events ordered by `position` will match the order in which those same
events were observed by change stream watchers.

## Cheap commit outcomes (no per-commit fencing)

We deliberately **do not** perform the full transactional leadership handshake for every commit outcome (e.g.
`CommitConfirmed` / `CommitRejected`) for throughput and cost reasons.

Instead, we rely on the following properties:

- `dbx_commits` is a single, ordered control log where every record has a monotonic `position`.
- `LeaderElected` establishes an epoch boundary in that same ordered log.
- Every commit outcome includes the `epoch` under which it was produced.

A stale leader may occasionally continue to write commit outcomes briefly after losing leadership (e.g. due to
observation lag), but this does not compromise correctness. Consumers validate every commit outcome against the
leadership boundaries recorded in `dbx_commits`, and ignore any outcome whose epoch does not match the active
`LeaderElected` boundary at that position.

This design intentionally trades rare, harmless noise (stale outcomes that are deterministically ignored) for higher
steady-state throughput, while keeping the canonical history fully deterministic, replayable, and defined solely by the
ordered contents of `dbx_commits`.
