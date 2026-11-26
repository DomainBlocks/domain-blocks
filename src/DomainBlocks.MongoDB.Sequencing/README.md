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
    - [Sustainable throughput](#sustainable-throughput)
    - [Latency](#latency)
    - [Batching delay](#batching-delay)

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

An implementation of `IMongoSequencedAppenderPolicy<TContext>` can customize appends. Both of its methods run outside
the transaction.

- `OnBatchCommittingAsync` can mutate the documents of a batch's requests, and complete requests early. It's called:
    - Before the first commit attempt.
    - After a duplicate key conflict, with the requests still pending.
    - After a transient error, such as a write conflict, if queued requests have joined the batch.
- `OnConflict` decides whether to retry or fail the request that caused a duplicate key conflict on the target
  collection.

Requests appended with the same `requestId` in one batch reach the policy once: it receives the first, and the others
complete with that request's outcome.

Uses include:

- Stamping per-stream version numbers onto event documents before commit.
- Implementing idempotent commits by detecting already-applied requests and completing them.
- Completing requests early based on pre-commit validation.
- Implementing optimistic concurrency checks.

For an example implementation,
see [AppenderPolicy](../../examples/DomainBlocks.MongoDB.Sequencing.Examples/SimpleEventStore/AppenderPolicy.cs).

## Benchmarks

Benchmarks were run against a local single-node MongoDB replica set running in Docker, on two machines:

|         | MacBook Air                                          | Windows PC                                   |
|:--------|:-----------------------------------------------------|:---------------------------------------------|
| CPU     | Apple M3, 8 logical processors                       | AMD Ryzen AI MAX+ 395, 32 logical processors |
| OS      | macOS 27.2.1                                         | Windows 11 Pro                               |
| Docker  | Docker Desktop on the Apple Virtualization framework | Docker Desktop on WSL 2                      |
| MongoDB | 7.0.48                                               | 7.0.40                                       |

Appends are scheduled at random times, at a fixed average rate, as requests from independent users would arrive
(a Poisson process). Each starts at its scheduled time, whether or not earlier appends have finished, and appends one
document. Latency is measured from the scheduled time, so a late start counts. The appenders use the default options:
batch size 1,000, queue capacity 1,000, and no batching delay. Figures are the median of four rounds, leaving out any
round that didn't sustain its rate.

The [benchmarks](../../benchmarks/DomainBlocks.MongoDB.Sequencing.Benchmarks/MongoSequencedAppenderBenchmarkTests.cs)
are included in the repository and can be run against your own environment. `DBX_BENCHMARK_RATE` sets the rate,
`DBX_BENCHMARK_NETWORK_LATENCY_MS` adds network latency, and `DBX_BENCHMARK_NETWORK_JITTER_MS` varies it.

### Sustainable throughput

Each run warms up for 5 seconds, then measures for 20. The sustainable rate is the highest at which at least 99% of the
appends due in that window complete within it, and the backlog then clears.

| Instances | Network latency | MacBook Air (ops/sec) | Windows PC (ops/sec) |
|----------:|:----------------|----------------------:|---------------------:|
|         1 | none            |               112,000 |               70,000 |
|         4 | none            |                91,000 |               71,000 |
|         1 | 1 ms            |                79,000 |               54,000 |
|         4 | 1 ms            |                67,000 |               54,000 |

Adding instances doesn't raise the sustainable rate: every transaction takes its turn on the counter document, whichever
instance runs it. On the MacBook Air, more instances lower it.

For the 1 ms rows, a proxy delays each of the server's responses by 1 ms. A single append's median latency then rises
from 0.64 ms to 10 ms on the MacBook Air, and from 3.0 ms to 7.2 ms on the Windows PC.

On the MacBook Air, the delay added about 2.3 ms to each round trip, against 1.2 ms on the Windows PC. Its Docker VM is
slower to resume after short pauses, which likely accounts for the difference.

Deployments with a multi-node MongoDB cluster can expect lower throughput, as `w: "majority"` write concern waits for a
majority of members to acknowledge each commit.

### Latency

Latency in milliseconds with no network latency added:

| Instances | Load               | MacBook Air p50 | MacBook Air p99 | Windows PC p50 | Windows PC p99 |
|----------:|:-------------------|----------------:|----------------:|---------------:|---------------:|
|         1 | 1 in flight        |            0.64 |             1.9 |            3.0 |            4.8 |
|         1 | 1,000/s            |            0.91 |             3.3 |            4.8 |            7.6 |
|         1 | 10,000/s           |             1.2 |             5.1 |            5.7 |            8.9 |
|         1 | 80% of sustainable |             3.7 |              13 |            9.4 |             15 |
|         4 | 1,000/s            |             1.6 |              11 |            9.3 |             40 |
|         4 | 10,000/s           |             4.8 |              27 |             11 |             45 |
|         4 | 80% of sustainable |              12 |              63 |             18 |             71 |

### Batching delay

`BatchingDelay` makes a batch that isn't full wait for more appends before it commits. None of the delays tried (1, 2,
and 5 ms) raised the sustainable rate. With several instances, a 1 ms delay usually lowered latency at 10,000/s and
above, since it gathers appends into fewer transactions; with one instance, it only added latency.

Latency in milliseconds at 10,000/s:

| Instances | Network latency | Batching delay | MacBook Air p50 | MacBook Air p99 | Windows PC p50 | Windows PC p99 |
|----------:|:----------------|:---------------|----------------:|----------------:|---------------:|---------------:|
|         1 | none            | none           |             1.2 |             5.1 |            5.7 |            8.9 |
|         1 | none            | 1 ms           |             5.4 |              11 |            6.3 |             10 |
|         4 | none            | none           |             4.8 |              27 |             11 |             45 |
|         4 | none            | 1 ms           |             3.6 |              17 |            9.3 |             34 |
|         4 | 1 ms            | none           |              19 |              98 |             26 |            118 |
|         4 | 1 ms            | 1 ms           |              17 |              58 |             26 |            107 |

A delay can still raise throughput for a single process appending as fast as it can, with a fixed number of appends in
flight. With one instance:

| Appends in flight | Batching delay | MacBook Air (ops/sec) | Windows PC (ops/sec) |
|------------------:|:---------------|----------------------:|---------------------:|
|             1,000 | none           |               102,000 |               81,000 |
|             1,000 | 5 ms           |               137,000 |               87,000 |
|             2,000 | none           |               136,000 |               84,000 |

At 1,000 in flight, the delay raised throughput by about a third on the MacBook Air. On the Windows PC, the gain was
smaller than the variation between rounds. Doubling the appends in flight did about as well as the delay on both
machines.

On Windows, every run here raised the system clock's resolution to 1 ms; at the default resolution, a 1 ms delay lasts
about 15 ms.