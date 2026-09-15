# DomainBlocks

DomainBlocks is a .NET library for building applications using Domain-Driven Design (DDD) principles.

> 🚧 **Work in progress:** The API and functionality may change as the project matures.

## Getting started

Add a store package, `DomainBlocks.EventStore.PostgreSQL` or `DomainBlocks.EventStore.MongoDB`, and build a store:

```csharp
await using var store = new PostgresEventStoreBuilder<IDomainEvent>()
    .UseConnectionString(connectionString)
    .ConfigureOptions(o => o.Schema = "orders")
    .MapEvents(EventTypeMapping.ReadWrite<OrderPlaced>())
    .Build();

await store.EnsureInitializedAsync();
await store.AppendAsync("order-1", [new OrderPlaced(...)]);
```

Events are stored as JSON by default. Serializers, contract mappers, metadata contributors and read transforms are
configured on the builder; a data source or client from a container is passed with `UseDataSource` or `UseClient`.

## Contributing

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Licensed under [AGPL v3](./LICENSE).

Note that the [legacy repository](https://github.com/DomainBlocks/domain-blocks-legacy) remains licensed under MIT.