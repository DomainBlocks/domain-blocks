# DomainBlocks – Vision and Scope

## Vision

DomainBlocks provides modular, low-friction tooling for event sourcing and DDD in .NET, so developers can model
behaviour, not fight infrastructure.

"Focus on the domain. We’ll handle the plumbing."

## Goals

* Provide a lightweight, composable foundation for building event-sourced DDD systems in .NET.
* Simplify the creation and management of aggregates and domain events.
* Offer clean abstractions for storing and replaying events, without tying users to a specific event store.
* Support building projections and read models in a testable, extensible way.
* Support the modeling of long-running processes via process managers.
* Enable applications to evolve safely through event versioning and backward-compatible patterns.
* Encourage clarity and testability by isolating domain logic from infrastructure concerns.
* Allow partial adoption: each component should be usable on its own or together with others.
* Avoid imposing framework-style control - keep users in charge of their application structure.

## Non-Goals

What this project is intentionally *not* trying to do

- **Expose functionality that the underlying data store doesn't support** – DomainBlocks does not attempt to simulate
  features that aren’t natively provided by the chosen persistence layer. For example, MongoDB only supports
  transactions at the document level. While global ordering can technically be achieved via multiple round trips or
  coordination mechanisms, we do not consider this a core responsibility of the library.

## Target Audience

Who should use it (and who shouldn’t)

## Guiding Principles

Design philosophy or architectural values

* Minimally opinionated, e.g. we don't tell you how to store events, only what you need to store.
* Composable, opt-in, building blocks. Individual components can be used in isolation without taking on implicit
  dependencies.
* Idiomatic - following best practice, but also defining new best practices where appropriate.
* Shared abstractions, e.g. mapping events to/from CLR types should be reusable across both the write and read side.