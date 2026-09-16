# Creating an event store

September 2026. How a store is created, who owns what, and why the API has the shape it has. The layering behind
the store is in [event-codec-and-store-decorator.md](event-codec-and-store-decorator.md).

## The shape

One builder per backend, one obvious way through it:

```csharp
await using var store = new PostgresEventStoreBuilder<IDomainEvent>()
    .UseConnectionString(connectionString)              // or .UseDataSource(dataSource)
    .ConfigureOptions(o => o.Schema = "orders")
    .MapEvents(
        EventTypeMapping.ReadWrite<OrderPlaced>(),
        EventTypeMapping.ReadOnly<OrderPlaced>("OrderPlacedV1"))
    .UseLoggerFactory(loggerFactory)
    .Build();

await store.EnsureInitializedAsync();                   // schema, functions, publication; idempotent
```

`MongoEventStoreBuilder<TEvent>` and `KurrentDBEventStoreBuilder<TEvent>` have the same surface with `UseClient`
in place of `UseDataSource`. Everything beyond a connection and a type map is optional:

| Method | Default |
|---|---|
| `UseEventSerializer`, `UseMetadataSerializer` | PostgreSQL: System.Text.Json into `jsonb` and JSON text. MongoDB: BSON documents. KurrentDB: UTF-8 JSON bytes. |
| `AddContractMappers` | none; a contract type is mapped under its type name unless mapped explicitly |
| `UseCodec` | replaces type map, mappers and serializers with a ready codec |
| `AddMetadataContributors`, `AddReadTransforms`, `UseDroppedEventPlaceholder` | none |
| `UseLoggerFactory`, `UseLogger` | no logging; the factory path names the logger after the store class |
| `UseOptions`, `ConfigureOptions` | the options class's defaults |

A required step that is missing fails at `Build()` with one line naming the method to call. `Build()` never touches
the database, so a store can be built synchronously in a container factory; initialization is a separate, explicit
call on the built store.

## Who owns the client

| How the client arrives | Who disposes it | When |
|---|---|---|
| `UseDataSource` / `UseClient` | The caller, never the store | Hosted apps, where the client is a container singleton; test fixtures sharing one client |
| `UseConnectionString` | The store, after its own resources | Console apps, scripts and tests with no container |
| Neither, in a future DI registration | The container | The registration resolves the client and calls the borrow path |

`IEventStore` extends `IAsyncDisposable`. Disposing a store releases what the store owns: its append queue, the
PostgreSQL replication feed, and a client it created. A container disposes singletons in reverse registration
order, so a store registered after its client is disposed before it. That is the shape MongoDB's guidance leads to
(one `MongoClient` singleton) and it needs nothing store-specific.

A builder that created a client builds one store; a second `Build()` throws, so one data source never gets two
owners. A builder on the borrowed path may build as many stores as it likes.

## PostgreSQL specifics the builder takes care of

* The schema name has to reach three places with the same value: the data source's type mappings
  (`UsePostgresEventStore`), schema creation, and the store. On the owned path the builder creates the data source
  at `Build()`, after the options are final, so all three agree without the caller threading anything.
* The replication feed opens its own connection from a connection string. A data source's connection string drops
  the password unless `Persist Security Info=true`, which used to be a silent trap. On the owned path the builder
  defaults the replication connection string to the raw connection string it was given.
* On the borrowed path both of those remain the caller's responsibility, exactly as with `PostgresEventStore.Create`.

## Initialization

`EnsureInitializedAsync` is a member of `IEventStore`. The store knows *what* it needs in its backing database; the
host decides *when* to create it. Splitting the two this way is what keeps the rest of the design simple:

* Builders never perform I/O. `Build()` is synchronous and can run in a container factory; `BuildAsync` or an
  initializing builder would also mean one DDL round per built store, which is wrong for fixtures that initialise
  once and build many.
* It is idempotent and safe to call concurrently, so calling it from start-up code, a test fixture or a hosted
  service is always safe, including from several replicas starting at once.
* It is provider-specific underneath: PostgreSQL creates a schema, functions and a publication; MongoDB creates
  indexes; KurrentDB has nothing to create, so its store completes immediately. Provider-specific choices, such as
  whether PostgreSQL creates the publication, are builder configuration (`ConfigureAdminOptions`), not call-site
  arguments, so the call itself is the same for every store.
* Being on the interface, it survives decoration: the store `Build()` returns, a fake in a test host, or anything
  a user wraps around a store can be initialized without knowing what it is. That is what lets one generic
  start-up component serve every provider, below.
* It is the only admin operation on the store. `DropAsync` stays on the static admin classes: a destructive method
  does not belong in IntelliSense next to `AppendAsync`.

## One class per backend

`Build()` returns `IEventStore<TEvent, string, StreamPosition, LogPosition>`: the concrete store with the configured
contributors and transforms applied through the existing `WithMetadataContributors` and `WithReadTransforms`
extensions. There is one store class per backend, `PostgresEventStore<TEvent>` and so on. It owns its append
queue, feed and, when the builder created it, its client; it implements `EnsureInitializedAsync` over the static
admin class; and it sees only `IEventCodec`, which the dependency tests enforce.

The static `Create(client, codec, options, logger)` factories remain as the primitive for callers who build a codec
themselves, and the decorator extensions remain for composing any `IEventStore` by hand.

## `EventCodecBuilder`

The store-agnostic half of creation. Each store builder owns one, fixed to its data types, and forwards `MapEvents`,
`MapEvent<T>`, `UseEventTypeMap`, `AddContractMappers`, `UseEventSerializer`, `UseMetadataSerializer` and `UseCodec`
to it, so users see a flat builder and never name the codec builder. It is public so that a codec can be built on
its own, in tests for instance. Two rules in it remove known traps: contract types are registered in the type map
automatically, because the stored name comes from the contract type; and the store's serializer defaults apply only
to a serializer that was not set explicitly.

## Start-up

Nothing is built yet; this records the design. There are three idiomatic places to call `EnsureInitializedAsync`
in a hosted application, and the interface method serves all of them:

1. **Explicitly in `Program.cs`**, before the host runs, the way EF Core migrations are usually applied. No library
   support is needed and the DDL step is visible in the one file people read first.
2. **An opt-in hosted initializer**, registered by a future `AddPostgresEventStore(...)` through something like
   `InitializeOnStartup()`. It implements `IHostedLifecycleService.StartingAsync`, not plain `StartAsync`: the host
   runs every `StartingAsync` before any `StartAsync`, so subscription and projection workers see the schema
   regardless of registration order. Opt-in rather than default, as with EF and Marten, because locked-down
   deployments create the schema out of band.
3. **Out of band**: an init container, a migration job or a DBA script. The application never calls initialize.

Registration binds `IEventStore<TEvent, string, StreamPosition, LogPosition>` directly; there is no concrete type to
forward from:

```csharp
services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));      // MongoDB's guidance
services.AddMongoEventStore<IDomainEvent>(b => b.MapEvents(...));                   // resolves the client itself
services.AddPostgresEventStore<IDomainEvent>((sp, b) => b
    .ConfigureOptions(o => configuration.GetSection("EventStore").Bind(o))
    .MapEvents(...))
    .InitializeOnStartup();
```

A registration resolves the client and `ILoggerFactory` from the container, calls the borrow path and registers
the built store as a singleton. The options classes are plain mutable objects, so `IConfiguration.Bind` works today.
The `Add*` methods belong in each store package, depending only on
`Microsoft.Extensions.DependencyInjection.Abstractions`; the generic initializer needs the hosting abstractions, so
it belongs in a small shared hosting package, keeping `DomainBlocks.EventStore` free of package dependencies.

## Packages

`DomainBlocks.EventStore.Abstractions` was merged into `DomainBlocks.EventStore`. An abstractions package is a
dependency firewall and a version contract; the implementation has no package dependencies and ships on the same
version, so the split bought nothing and produced seams. The rule it stood for, a store sees only `IEventCodec`, is
enforced by a dependency test in each store's unit test project instead. Each store package references
`DomainBlocks.EventStore` and the serializer package of its default format. `DomainBlocks.Serialization.Abstractions`
stays separate: it keeps the Protobuf and BSON packages independent of event store versioning.
