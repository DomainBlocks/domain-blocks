# DomainBlocks Architecture

## Library Overview

| Library                                 | Purpose                                                                                          |
|-----------------------------------------|--------------------------------------------------------------------------------------------------|
| DomainBlocks.Core                       | Defines foundational types and shared primitives used across DomainBlocks libraries.             |
| DomainBlocks.EventSourcing              | Provides a store for saving and loading event-sourced entities.                                  |
| DomainBlocks.EventStore                 | High-level event store implementation for storing/retrieving CLR-typed events and subscriptions. |
| DomainBlocks.EventStore.Abstractions    | Defines contracts for specific event store backend implementations.                              |
| DomainBlocks.EventStore.*               | Specific event store backend implementations (e.g. MongoDB, KurrentDB).                          |
| DomainBlocks.Serialization.Abstractions | Defines contracts for specific serializer implementations.                                       |
| DomainBlocks.Serialization.*            | Specific serializer implementations.                                                             |

## Dependencies

TBD