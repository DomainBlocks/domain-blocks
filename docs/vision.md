# DomainBlocks – Vision and Scope

## Vision

Lowering the barrier to entry for building event-sourced applications using DDD principles.

## Goals

* Provide a lightweight, composable foundation for building event-sourced DDD systems in .NET.
* Prioritise correctness and consistency guarantees, particularly around event storage and concurrency.
* Simplify the creation and management of aggregates and domain events.
* Offer clean abstractions for storing and replaying events.
* Support building projections and read models in a testable, extensible way.
* Support the modelling of long-running processes via process managers.
* Enable applications to evolve safely through event versioning and backward-compatible patterns (e.g. via event
  transforms).
* Encourage clarity and testability by isolating domain logic from infrastructure concerns.
* Allow partial adoption: each component should be usable on its own or together with others.
* Avoid imposing framework-style control - keep users in charge of their application structure.

## Target Audience

Anyone who wants to build event-sourced applications using DDD principles in .NET.

## Guiding Principles

* Minimally opinionated where possible.
* Composable, opt-in, building blocks.
* Guiding consumers into the "pit of success".