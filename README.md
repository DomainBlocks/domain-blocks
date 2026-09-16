# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Work in progress:** The API and functionality may change as the project matures.

## Getting started

Add a store package, `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, and build a store:

```csharp
await using var store = new PostgresEventStoreBuilder<IDomainEvent>()
    .UseConnectionString(connectionString)
    .ConfigureOptions(o => o.Schema = "events")
    .MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>())
    .Build();

await store.EnsureInitializedAsync();
await store.AppendAsync("order-1", [new OrderPlaced(...)]);
```

Subscribe to catch up on existing events and then keep receiving new ones as they are appended:

```csharp
await foreach (var message in store.SubscribeToAll(SubscriptionOrigin.Start))
{
    switch (message)
    {
        case { Event: { } e }:
            Console.WriteLine($"{e.Context.StreamId}: {e.Payload}");
            break;
        case { IsCaughtUp: true }:
            Console.WriteLine("Caught up; now receiving live events");
            break;
    }
}
```

The default origin is the end of the log, so omitting it receives only new events. `SubscribeToStream` works the same
way for a single event stream. A subscriber that consumes too slowly receives a message with `IsFellBehind` set to
`true`, then catches up again.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT.