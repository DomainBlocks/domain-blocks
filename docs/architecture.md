# DomainBlocks Architecture

## Library Overview

| Library                                 | Purpose                                                                                                                                  |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------|
| DomainBlocks.Core                       | Defines foundational types and shared primitives used across DomainBlocks libraries.                                                     |
| DomainBlocks.Persistence                | Composes persistence building blocks (event store, snapshot store, etc.) into CLR-typed, high-level services. Includes metadata mapping. |
| DomainBlocks.Persistence.Abstractions   | Defines low-level contracts for storing and retrieving event, snapshot, and bookmark data - agnostic of storage or serialization.        |
| DomainBlocks.Serialization              | Maps between string event names and CLR types, and delegates payload (de)serialization to a serializer implementation.                   |
| DomainBlocks.Serialization.Abstractions | Defines contracts for serializing and deserializing CLR objects to/from payloads.                                                        |

## Dependencies

TBD

## Notes on Event Transforms

* We handle mapping to/from event names to CLR types.
* We handle deprecated event names.
* We handle in-memory CLR-typed transforms and splits (i.e. post-deserialisation). Implies old versions of event POCOs
  must be kept in the consumer's codebase.
* We DO NOT support splits at the deserialisation level.
* We support event upcasts and splits as durable stream transforms - i.e. write out a new stream version with the
  transformed events. Can use the same code as in-memory transforms.
* Concept of durable and in-memory transforms. Both should be very easy.