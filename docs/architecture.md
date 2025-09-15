# DomainBlocks Architecture

## Library Overview

| Library                                 | Purpose                                                                                                                |
|-----------------------------------------|------------------------------------------------------------------------------------------------------------------------|
| DomainBlocks.Core                       | Defines foundational types and shared primitives used across DomainBlocks libraries.                                   |
| DomainBlocks.EventSourcing              | Provides an store (aka repository) for saving and loading event-sourced entities.                                      |
| DomainBlocks.EventStore                 | High-level event store implementation for storing and retrieving CLR-typed events                                      |
| DomainBlocks.EventStore.Abstractions    | Defines low-level contracts for storing and retrieving events.                                                         |
| DomainBlocks.Serialization              | Maps between string event names and CLR types, and delegates payload (de)serialization to a serializer implementation. |
| DomainBlocks.Serialization.Abstractions | Defines contracts for serializing and deserializing CLR objects to/from payloads.                                      |
| DomainBlocks.Subscriptions              | Do we want to manage subscriptions in a separate library to DomainBlocks.EventStore?                                   |
| DomainBlocks.Subscriptions.Abstractions | See above question.                                                                                                    |

## Dependencies

TBD