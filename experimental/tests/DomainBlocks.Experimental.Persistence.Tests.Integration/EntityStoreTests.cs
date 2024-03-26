using System.Text.Json;
using DomainBlocks.Experimental.Persistence.Builders;
using DomainBlocks.Experimental.Persistence.EventStoreDb;
using DomainBlocks.Experimental.Persistence.SqlStreamStore;
using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Shopping.Domain.Events;
using JsonSerializer = DomainBlocks.Experimental.Persistence.Serialization.JsonSerializer;
using ShoppingCartCreated = Shopping.Domain.Events.ShoppingCartCreated;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration;

[TestFixture]
public class EntityStoreTests
{
    private const string PostgresStreamStoreConnectionString =
        "Server=localhost;Port=5433;Database=shopping;User Id=postgres;Password=postgres;";

    private const string EventStoreDbConnectionString = "esdb://localhost:2113?tls=false";

    private static readonly IEntityStore SqlStreamStoreEntityStore;
    private static readonly IEntityStore EventStoreDbEntityStore;

    static EntityStoreTests()
    {
        // Still using the pre-Npgsql 6.0 timestamp behaviour.
        // See: https://www.npgsql.org/doc/types/datetime.html#timestamps-and-timezones
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        var jsonOptions = new JsonSerializerOptions();

        var eventMapper = new EventMapperBuilder()
            .MapAll<IDomainEvent>(x => x.FromAssemblyOf<ShoppingCartCreated>())
            .UseJsonSerialization(jsonOptions)
            .Build();

        var storeConfigBuilder = new EntityStoreConfigBuilder()
            .AddEntityAdapters(adapters => adapters
                .AddGenericFactoryFor(typeof(EntityAdapter<,>))
                .WithConstructorArgs(123, "ABC"))
            .SetEventMapper(eventMapper);

        var sqlStreamStoreConfig = storeConfigBuilder.UseSqlStreamStore(streamStore).Build();
        var eventStoreDbConfig = storeConfigBuilder.UseEventStoreDb(EventStoreDbConnectionString).Build();

        SqlStreamStoreEntityStore = new EntityStore(sqlStreamStoreConfig);
        EventStoreDbEntityStore = new EntityStore(eventStoreDbConfig);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task WriteToExpectedNewStream_WhenNoStream_Succeeds(IEntityStore store)
    {
        var entity = new ShoppingCart();
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await store.SaveAsync(entity);

        var reloadedEntity = await store.LoadAsync<ShoppingCart>(entity.State.Id.ToString());

        Assert.That(reloadedEntity.State.Id, Is.EqualTo(entity.State.Id));
        Assert.That(reloadedEntity.State.Items, Is.EqualTo(entity.State.Items));
    }

    [Test]
    [TestCaseSource(nameof(WrongVersionTestCases))]
    public async Task WriteToExpectedNewStream_WhenStreamExists_ThrowsWrongVersionException(
        IEntityStore store, Type wrongVersionExceptionType)
    {
        var entity1 = new ShoppingCart();
        entity1.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await store.SaveAsync(entity1);

        // Attempting to write a new state stream for the same ID should fail.
        var entity2 = new ShoppingCart
        {
            State = new ShoppingCartState { Id = entity1.State.Id }
        };

        entity2.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));

        Assert.ThrowsAsync(wrongVersionExceptionType, () => store.SaveAsync(entity2));
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task WriteToExpectedExisingStream_WhenNoStream_Succeeds(IEntityStore store)
    {
        var id = Guid.NewGuid();

        var entity = await store.LoadAsync<ShoppingCart>(id.ToString());
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await store.SaveAsync(entity);

        // TODO: Because the ID is internally set, we need to use the ID of the entity we just saved.
        // Adjust the example model so that this isn't confusing.
        var reloadedEntity = await store.LoadAsync<ShoppingCart>(entity.Id);

        Assert.That(reloadedEntity.State.Id, Is.EqualTo(entity.State.Id));
        Assert.That(reloadedEntity.State.Items, Is.EqualTo(entity.State.Items));
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task WriteToExpectedExisingStream_WhenStreamExists_Succeeds(IEntityStore store)
    {
        var entity1A = new ShoppingCart();
        entity1A.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await store.SaveAsync(entity1A);

        var entity1B = await store.LoadAsync<ShoppingCart>(entity1A.State.Id.ToString());
        entity1B.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await store.SaveAsync(entity1B);

        var reloadedEntity = await store.LoadAsync<ShoppingCart>(entity1A.State.Id.ToString());

        Assert.That(reloadedEntity.State.Id, Is.EqualTo(entity1A.State.Id));
        Assert.That(reloadedEntity.State.Items, Is.EqualTo(entity1B.State.Items));
    }

    [Test]
    public async Task MutableScenario()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        var config = new EntityStoreConfigBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapters(x => x.Add(new MutableShoppingCartEntityAdapter()))
            .MapEvents(x => x.MapAll<IDomainEvent>())
            .Build();

        var store = new EntityStore(config);

        var entity = new MutableShoppingCart();
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await store.SaveAsync(entity);

        var reloadedEntity = await store.LoadAsync<MutableShoppingCart>(entity.Id.ToString());

        Assert.That(reloadedEntity.Id, Is.EqualTo(entity.Id));
        Assert.That(reloadedEntity.Items, Is.EqualTo(entity.Items));
    }

    [Test]
    public async Task FunctionalEntityWrapperScenario()
    {
        var config = new EntityStoreConfigBuilder()
            .UseEventStoreDb(EventStoreDbConnectionString)
            .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(FunctionalEntityWrapperAdapter<>)))
            .MapEvents(x => x.MapAll<IDomainEvent>())
            .Configure<FunctionalEntityWrapper<FunctionalShoppingCart>>(config =>
            {
                // TODO: Fix default stream name prefix for generic types, i.e. "functionalEntityWrapper`1".
                config.SetStreamNamePrefix("functionalShoppingCart");
            })
            .Build();

        var store = new EntityStore(config);

        var entity = new FunctionalEntityWrapper<FunctionalShoppingCart>();
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo")));
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar")));
        await store.SaveAsync(entity);

        var reloadedEntity = await store
            .LoadAsync<FunctionalEntityWrapper<FunctionalShoppingCart>>(entity.Id.ToString());

        Assert.That(reloadedEntity.Id, Is.EqualTo(entity.Id));
        Assert.That(reloadedEntity.Entity.Items, Is.EqualTo(entity.Entity.Items));
    }

    [Test]
    public async Task WithoutBuilderScenario()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);
        var eventStore = new SqlStreamStoreEventStore(streamStore);

        var entityAdapterRegistry = new EntityAdapterRegistry(
            new Dictionary<Type, IEntityAdapter>(),
            new[]
            {
                new GenericEntityAdapterFactory(new GenericEntityAdapterTypeResolver(typeof(MutableEntityAdapter<>)))
            });

        var eventTypeMappings = new EventTypeMapping[]
        {
            new(typeof(ShoppingCartCreated)),
            new(typeof(ItemAddedToShoppingCart))
        };

        var serializer = new JsonSerializer();
        var eventMapper = new EventMapper(eventTypeMappings, serializer);

        var config = new EntityStoreConfig(eventStore, entityAdapterRegistry, eventMapper);
        var store = new EntityStore(config);

        var entity = new MutableShoppingCart();
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await store.SaveAsync(entity);

        var reloadedEntity = await store.LoadAsync<MutableShoppingCart>(entity.Id.ToString());

        Assert.That(reloadedEntity.Id, Is.EqualTo(entity.Id));
        Assert.That(reloadedEntity.Items, Is.EqualTo(entity.Items));
    }

    [Test]
    public async Task EventUpcastScenario()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        const string newFieldDefaultValue = "default value";

        var v1StoreConfig = new EntityStoreConfigBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(EntityAdapter<,>)))
            .MapEvents(x =>
            {
                x.Map<ShoppingCartCreated>();
                x.Map<ItemAddedToShoppingCart>();
            })
            .Build();

        // Set up a new version of the ShoppingCartCreated event
        var v2StoreConfig = new EntityStoreConfigBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(EntityAdapter<,>)))
            .MapEvents(mapper =>
            {
                // Write scenarios:
                // One-to-one event type to name - most common use case
                // One-to-many event type to names - not supported / doesn't make sense
                // ** Many-to-one event types to name - unusual, but shouldn't necessarily be disallowed. E.g. possible
                //                                      if all code isn't refactored when introducing a new version.

                // Read scenarios:
                // One-to-one event name to type - most common use case
                // ** One-to-many event name to types - Possible with multiple event versions
                // Many-to-one event names to type - possible with event name changes

                // One-to-one write
                mapper
                    .Map<ShoppingCartCreated>()
                    .WithName("ShoppingCartCreated")
                    .WithDeprecatedNames("Foo", "Bar", "Baz");

                // Many-to-one write
                // Effectively supporting multiple event versions for the same name in the same version of code. This
                // happens anyway over time with event versioning. Just not necessarily in the same version of code.
                // Need a way to disambiguate reads.
                mapper
                    .Map<ShoppingCartCreatedV2>()
                    .WithName("ShoppingCartCreated")
                    .WithMetadata(() => new Dictionary<string, string> { { "Version", "2" } });

                // Disambiguate reads
                // mapper
                //     .MapEventName("ShoppingCartCreated")
                //     .WithTypeSelector(m =>
                //     {
                //         return m["Version"] == "2" ? typeof(ShoppingCartCreatedV2) : typeof(ShoppingCartCreated);
                //     });
                //
                // mapper
                //     .TransformEvent<ShoppingCartCreated>(e => new ShoppingCartCreatedV2(e.Id, newFieldDefaultValue));

                // Transform, Split, Join?
            })
            .Configure<ShoppingCartV2>(x => x.SetStreamNamePrefix("shoppingCart"))
            .Build();

        var v1Store = new EntityStore(v1StoreConfig);
        var v2Store = new EntityStore(v2StoreConfig);

        Guid cartId1;

        // Write to a new stream with V1 of the event
        {
            var entity = new ShoppingCart();
            entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item Foo"));
            await v1Store.SaveAsync(entity);

            Assert.That(entity.State.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(entity.State.Items, Has.Count.EqualTo(1));
            Assert.That(entity.State.Items[0].Name, Is.EqualTo("Item Foo"));

            cartId1 = entity.State.Id;
        }

        // Test restoring and writing to existing stream containing the V1 event, using the repository set up with the
        // refactored code.
        {
            var entity = await v2Store.LoadAsync<ShoppingCartV2>(cartId1.ToString());
            var state = entity.State;

            Assert.That(state.Id, Is.EqualTo(cartId1));
            Assert.That(state.Items, Has.Count.EqualTo(1));
            Assert.That(state.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(state.NewField, Is.EqualTo(newFieldDefaultValue));

            entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item Bar"));
            await v2Store.SaveAsync(entity);

            var reloadedEntity = await v2Store.LoadAsync<ShoppingCartV2>(cartId1.ToString());
            var reloadedState = reloadedEntity.State;

            Assert.That(reloadedState.Id, Is.EqualTo(cartId1));
            Assert.That(reloadedState.Items, Has.Count.EqualTo(2));
            Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(reloadedState.Items[1].Name, Is.EqualTo("Item Bar"));
            Assert.That(reloadedState.NewField, Is.EqualTo(newFieldDefaultValue));
        }

        // Test writing to a new stream with the refactored code - should write V2 of the event
        {
            var entity = new ShoppingCartV2();
            entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item Foo"), "New value");
            await v2Store.SaveAsync(entity);

            var reloadedEntity = await v2Store.LoadAsync<ShoppingCartV2>(entity.State.Id.ToString());
            var reloadedState = reloadedEntity.State;

            Assert.That(reloadedState.Id, Is.EqualTo(entity.State.Id));
            Assert.That(reloadedState.Items, Has.Count.EqualTo(1));
            Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(reloadedState.NewField, Is.EqualTo("New value"));
        }
    }

    private static IEnumerable<TestCaseData> TestCases
    {
        get
        {
            yield return new TestCaseData(SqlStreamStoreEntityStore).SetName(nameof(SqlStreamStoreEntityStore));
            yield return new TestCaseData(EventStoreDbEntityStore).SetName(nameof(EventStoreDbEntityStore));
        }
    }

    private static IEnumerable<TestCaseData> WrongVersionTestCases
    {
        get
        {
            yield return new TestCaseData(
                    SqlStreamStoreEntityStore,
                    typeof(DomainBlocks.ThirdParty.SqlStreamStore.Streams.WrongExpectedVersionException))
                .SetName(nameof(SqlStreamStoreEntityStore));

            yield return new TestCaseData(
                    EventStoreDbEntityStore,
                    typeof(EventStore.Client.WrongExpectedVersionException))
                .SetName(nameof(EventStoreDbEntityStore));
        }
    }
}