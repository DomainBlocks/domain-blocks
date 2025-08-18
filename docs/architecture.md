# DomainBlocks Architecture

## Library Overview

| Library                                 | Purpose                                                                                                                                  |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------|
| DomainBlocks.Core                       | Defines foundational types and shared primitives used across DomainBlocks libraries.                                                     |
| DomainBlocks.EventSourcing              | Provides an store (aka repository) for saving and loading event-sourced entities.                                                        |
| DomainBlocks.Persistence                | Composes persistence building blocks (event store, snapshot store, etc.) into CLR-typed, high-level services. Includes metadata mapping. |
| DomainBlocks.Persistence.Abstractions   | Defines low-level contracts for storing and retrieving event, snapshot, and bookmark data - agnostic of storage or serialization.        |
| DomainBlocks.Serialization              | Maps between string event names and CLR types, and delegates payload (de)serialization to a serializer implementation.                   |
| DomainBlocks.Serialization.Abstractions | Defines contracts for serializing and deserializing CLR objects to/from payloads.                                                        |
| DomainBlocks.Subscriptions              |                                                                                                                                          |
| DomainBlocks.Subscriptions.Abstractions |                                                                                                                                          |

## Dependencies

TBD

## Thoughts on Event Transforms

* Concept of durable and in-memory stream transforms. Both should be very easy.
* We support durable stream transforms - i.e. write out a new stream version with the transformed events. Can use the
  same logic as in-memory transforms.
* We DO NOT support skips or splits at the deserialisation level. If users want to remove CLR types for old events, this
  can be achieved with a durable stream transform.
* In-memory transforms can possibly be applied to the async enumerable passed into the RestoreAsync method of
  EntityAdapter.