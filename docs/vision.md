# DomainBlocks – Vision and Scope

## Vision

Lowering the barrier to entry for building event-sourced applications using DDD principles.

## Goals

* Provide a lightweight, composable foundation for building event-sourced DDD systems in .NET.
* Simplify the creation and management of aggregates and domain events.
* Offer clean abstractions for storing and replaying events, without tying users to a specific event store implementation.
* Support building projections and read models in a testable, extensible way.
* Support the modeling of long-running processes via process managers.
* Enable applications to evolve safely through event versioning and backward-compatible patterns (i.e. read transforms).
* Encourage clarity and testability by isolating domain logic from infrastructure concerns.
* Allow partial adoption: each component should be usable on its own or together with others.
* Avoid imposing framework-style control - keep users in charge of their application structure.

## Non-Goals

What this project is intentionally *not* trying to do

* **Expose functionality that the underlying data store doesn't support** – DomainBlocks does not attempt to simulate
  features that aren’t natively provided by the chosen persistence layer. For example, MongoDB only supports
  transactions at the document level. While global ordering can technically be achieved via multiple round trips or
  coordination mechanisms, we do not consider this a core responsibility of the library.

## Target Audience

Anyone who wants to build event-sourced applications using DDD principles in .NET.

## Guiding Principles

* Minimally opinionated, e.g. we don't tell you how to store events, only what you need to store.
* Composable, opt-in, building blocks. Individual components can be used in isolation without taking on implicit
  dependencies.
* Idiomatic - following best practices.
