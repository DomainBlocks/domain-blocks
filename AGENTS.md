# AGENTS.md

## Overview

DomainBlocks is a .NET library for building applications using Domain-Driven Design principles.

A key feature is a correctness-first event store over commodity databases.

The library is currently pre-release, so breaking changes where justified are acceptable.

## Tech Stack

- .NET 10

## Architecture

TBD

## Project Structure

- src/DomainBlocks.Core/ - Shared cross-cutting concerns
- src/DomainBlocks.EventSourcing/ - Storage and retrieval of event-sourced state
- src/DomainBlocks.EventStore/ - Common event store abstractions and related reusable types
- src/DomainBlocks.EventStore.[DatabaseTechnology]/ - An event store implementation for a specific database technology
- src/DomainBlocks.MongoDB.Sequencing/ - Appends to a MongoDB collection with a contiguous sequence number
- src/DomainBlocks.Serialization.Abstractions/ - Common serialization abstractions
- src/DomainBlocks.Serialization.[SerializationFormat]/ - A serialization implementation for a specific format
- examples/ - Example applications of the library's packages
- New assemblies/renames/reshuffling may be proposed where justified

## File Naming

- [TypeName].cs
- One outer-most type per file

## Code Conventions

- 120 char maximum line width - applies to both *.cs and *.md files
- Wrap all comments, including doc comments, at 120 chars
- Prefer succinct doc comments that follow BCL conventions
- Prefer primary constructors
- Use record types only where value semantics apply, otherwise choose classes
- Sealed records and classes for implementations
- Outer-most types internal by default, public only for the library's API surface
- File-scoped namespaces

## Patterns We Use

- The principle of high cohesion and low coupling

## Patterns We Do NOT Use

TBD

## Testing

- NUnit
- Shouldy for assertions
- Prefer using the `ShouldNotBeNull()` assertion rather than the null-forgiving operator
- Test method names to match [SubjectUnderTest]\_[ScenarioUnderTest]\_[ExpectedBehaviour], where SubjectUnderTest is
  typically a method name for unit tests, but may be broader, especially for integration, scenario, or end-to-end tests.
  Examples:
    - Unit test: CalculateTotal_WithDiscount_ReturnsDiscountedTotal
    - Integration test: OrderSubmission_WhenPaymentProviderFails_DoesNotCreateOrder
