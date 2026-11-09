# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Pre-release:** DomainBlocks is in active development, and the API may change before the first stable release.

## Features

DomainBlocks is a set of NuGet packages, so you reference only what you need. Event storage, event filtering, and event
evolution are the first major feature areas; others will follow.

### Event store

The event store is built to be trusted as the system of record:

- **Atomic appends.** The events in a given append operation either all commit or none do.
- **Optimistic concurrency.** Pass the `ExpectedStreamState` with an append, and the operation is rejected with
  `StreamAppendConflictException` if another writer got there first. The exception carries the state that was observed,
  so the caller may reload and retry.
- **Idempotent appends.** Give an append a unique commit ID, and a retry of the same request succeeds without writing
  again, even if the first attempt committed but its reply was lost (e.g. due to a timeout or cancellation).
- **Globally ordered event log.** Global event sequencing guarantees events are observed in commit order across both
  historical reads and live subscriptions. Causes are always observed before their effects, and a replay sees exactly
  the sequence a live subscriber saw.
- **No skipped, phantom, or duplicate events.** A subscription delivers every committed event once, in order, and never
  delivers an event from a transaction that rolled back. A subscriber that cannot keep up is told it fell behind and
  automatically resumes from its last delivered position, so nothing is dropped silently.

See the [README](https://github.com/DomainBlocks/domain-blocks#readme) for code examples, event filtering and
evolution, and deploying PostgreSQL.