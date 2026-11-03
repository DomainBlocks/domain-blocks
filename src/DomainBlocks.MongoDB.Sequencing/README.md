# DomainBlocks.MongoDB.Sequencing

A .NET library for appending documents to a MongoDB collection with a contiguous, strictly increasing sequence number -
guaranteeing total order across historical reads and change stream observations.

## Table of contents

- [Prerequisites](#prerequisites)
- [The problem](#the-problem)
- [How it works](#how-it-works)
- [Use cases](#use-cases)
    - [Why not change stream resume tokens?](#why-not-change-stream-resume-tokens)
    - [Example: implementing a simple event store](#example-implementing-a-simple-event-store)
- [Customizing append behavior](#customizing-append-behavior)
- [Benchmarks](#benchmarks)
    - [Peak throughput](#peak-throughput)
    - [Latency](#latency)

## Prerequisites

- MongoDB 7.0+
- MongoDB replica set deployment
- .NET 10

## The problem

Out of the box, MongoDB does not provide a way to assign a globally ordered sequence number to documents that reflects
true insertion order across multiple concurrent writers. This makes it difficult to:

- Know that a historical read (sorted by sequence) and a change stream share the same total order, so a consumer can
  seamlessly transition from one to the other without gaps or ambiguity.
- Reason about causality - if B has a higher sequence number than A, then B causally follows A.

Without this, event-driven processing becomes unreliable - consumers cannot checkpoint their position with confidence,
and the boundary between catching up historically and tailing live cannot be reasoned about correctly.

## How it works

[MongoSequencedAppender](MongoSequencedAppender.cs) solves this by:

1. Atomically claiming a range of sequence numbers from a counter document using `findOneAndUpdate` inside a MongoDB
   multi-document transaction.
2. Stamping the claimed sequence numbers onto a target field on each document being appended.
3. Appending the documents into the target collection within the same transaction, using an ordered `insertMany`.
4. Batching concurrent append requests into a single transaction, so that throughput scales under load without
   sacrificing ordering guarantees.

Because the counter increment and the inserts are committed atomically, sequence numbers are guaranteed to be contiguous
and globally ordered - even across multiple concurrent appenders. When two transactions attempt to increment the counter
simultaneously, MongoDB's write conflict detection ensures only one can commit; the other fails with a write conflict
and retries. This forces appends to serialize, eliminating any possibility of out-of-order sequence number assignment.
The result is that sorting by sequence number reflects true insertion order across the entire collection.

## Use cases

This library is particularly useful for introducing global ordering into a MongoDB-based event store, where events
(represented by documents) must be consumable in a guaranteed total order by downstream event handlers and projections.

Such global sequencing enables reliable checkpointing when tailing an event collection via a change stream. Because
historical reads and change stream observations share the same total order, transitioning from catch-up to live
processing is straightforward to reason about and implement correctly.

### Why not change stream resume tokens?

Change stream resume tokens have the following limitations:

- Can become stale - if a consumer is offline long enough, the corresponding oplog entry may roll off, making resumption
  impossible without replaying from scratch.
- Resuming with a different pipeline or options may lead to unpredictable behavior or prevent resumption entirely.
- Have no meaningful correlation to position within the collection itself.

A sequence number, by contrast, survives oplog rotation, is easy to reason about across historical reads and change
streams, and always reflects a document's true position in the global order.

To be clear, this library does not attempt to replace change stream resume tokens - they remain the mechanism for
resuming a change stream itself. Rather, a sequence number provides a reliable, durable way to track your position
within a collection that's being used as an append-only log, independent of the change stream infrastructure.

### Example: implementing a simple event store

See [SimpleEventStore](../../examples/DomainBlocks.MongoDB.Sequencing.Examples/SimpleEventStore/SimpleEventStore.cs) for
an example showing how to build a simple, globally ordered event store using this library.

## Customizing append behavior

An implementation of `IMongoSequencedAppenderPolicy<TContext>` can be provided to hook into the append pipeline at two
points:

- **Before each transaction attempt** - runs outside the transaction, before the sequence counter is incremented and
  documents are inserted. Fires on every attempt, including retries after a duplicate key conflict.
- **After a duplicate key conflict** - runs outside the transaction, after it has been aborted due to a duplicate key
  conflict on the target collection.

Uses include:

- Stamping per-stream version numbers onto event documents before commit.
- Implementing idempotent commits by detecting and skipping already-applied requests.
- Completing requests early based on pre-commit validation.
- Implementing optimistic concurrency checks.

For an example implementation,
see [AppenderPolicy](../../examples/DomainBlocks.MongoDB.Sequencing.Examples/SimpleEventStore/AppenderPolicy.cs).

## Benchmarks

Benchmarks were run against a local single-node MongoDB replica set running in Docker, on two machines:

|         | Windows PC                                   | MacBook Air                    |
|:--------|:---------------------------------------------|:-------------------------------|
| CPU     | AMD Ryzen AI MAX+ 395, 32 logical processors | Apple M3, 8 logical processors |
| OS      | Windows 11 Pro                               | macOS 26.6.2                   |
| Docker  | Docker Desktop on WSL 2                      | Docker Desktop                 |
| MongoDB | 7.0.40                                       | 7.0.32                         |

Each machine has its own column in the tables below. The columns show what to expect in each environment, not a
ranking. Real-world deployments with network latency between the application and MongoDB will see higher latency and
lower throughput.

The [benchmarks](../../benchmarks/DomainBlocks.MongoDB.Sequencing.Benchmarks/MongoSequencedAppenderBenchmarkTests.cs)
are included in the repository and can be run against your own environment to get representative numbers for your
setup.

### Peak throughput

Peak throughput measured over 15 seconds after a 5-second warm-up, with each operation appending a single document. The
following parameters were used:

- Max in-flight operations: 1,000 total, spread evenly across the appender instances
- Batch size: 1,000 (maximum append operations committed in a single transaction)
- Queue capacity: 1,000 (maximum append operations buffered before callers block)
- Batching delay: 5 ms (maximum time to wait for more append operations before committing a batch)

| Appender instance count* | Windows PC (ops/sec) | MacBook Air (ops/sec) |
|-------------------------:|---------------------:|----------------------:|
|                        1 |               87,073 |               126,814 |
|                        3 |               53,365 |               103,949 |
|                        5 |               46,126 |                90,718 |

\* Values greater than 1 indicate multiple appender instances issuing append operations concurrently.

Throughput decreases as the number of concurrent appenders increases due to greater write conflict contention on the
counter document, causing more transactions to abort and retry.

On Windows, .NET timers depend on the system clock, which has a resolution of
[about 15 ms](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.delay) by default. The 5 ms
batching delay therefore waits about 15 ms. With more than one instance, each instance receives only its share of the
1,000 in-flight operations. Its batches never reach the batch size, so they always wait for the full delay, which lowers
the Windows figures for 3 and 5 instances.

Throughput therefore drops further on Windows as instances are added. The table below gives the drop from one instance.
The Windows PC and MacBook Air columns come from the throughput table above. The 1 ms timer column comes from separate
runs that raised the timer resolution to 1 ms with `timeBeginPeriod`, using the median of three runs for each instance
count. Raising the resolution removes part of the difference, but not all of it.

| Appender instance count | Windows PC (drop) | Windows PC, 1 ms timer (drop) | MacBook Air (drop) |
|------------------------:|------------------:|------------------------------:|-------------------:|
|                       3 |               39% |                           24% |                18% |
|                       5 |               47% |                           36% |                28% |

Deployments with a multi-node MongoDB cluster will see reduced throughput due to replication overhead, as
`w: "majority"` write concern requires acknowledgement from a majority of members before a transaction can commit.

### Latency

Single-appender latency with one append in flight and one document per operation, using the default appender options.
Warm-up runs for at least 2 seconds and 1,000 appends. Measurement then stops after 10,000 appends or 30 seconds,
whichever comes first: 8,875 appends on the Windows PC and 10,000 on the MacBook Air.

| Metric | Windows PC (ms) | MacBook Air (ms) |
|-------:|----------------:|-----------------:|
|    p50 |            3.18 |             0.64 |
|    p90 |            4.09 |             0.95 |
|    p99 |            5.20 |             1.58 |
|    min |            2.52 |             0.55 |
|    max |            8.92 |             3.78 |
|   mean |            3.38 |             0.71 |