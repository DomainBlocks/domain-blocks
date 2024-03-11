using System.Text.Json;
using DomainBlocks.Core.Serialization;
using DomainBlocks.EventStore.Experimental;
using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;
using DomainBlocks.IntegrationTests.Experimental.Model;
using DomainBlocks.IntegrationTests.Experimental.Persistence;
using DomainBlocks.SqlStreamStore.Experimental;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental;

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

        SqlStreamStoreEntityStore = new EntityStoreBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapterType(typeof(EntityAdapter<,>))
            .Configure(o => o.MapEventsOfType<IDomainEvent>())
            .Build();

        EventStoreDbEntityStore = new EntityStoreBuilder()
            .UseEventStoreDb(EventStoreDbConnectionString, o => o.UseJsonSerialization(jsonOptions))
            .AddEntityAdapterType(typeof(EntityAdapter<,>))
            .Configure(o => o.MapEventsOfType<IDomainEvent>().FromAssemblyOf<IDomainEvent>())
            .Build();
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

        var reloadedEntity = await store.LoadAsync<ShoppingCart>(id.ToString());

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

        var store = new EntityStoreBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapterType(typeof(MutableEntityAdapter<>))
            .Configure(o => o.MapEventsOfType<IDomainEvent>().FromAssemblyOf<IDomainEvent>())
            .Build();

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
        var store = new EntityStoreBuilder()
            .UseEventStoreDb(EventStoreDbConnectionString)
            .AddEntityAdapterType(typeof(FunctionalEntityWrapperAdapter<>))
            .Configure(options =>
            {
                options.MapEventsOfType<IDomainEvent>().FromAssemblyOf<IDomainEvent>();

                // TODO: Fix default stream prefix for generic types, i.e. "functionalEntityWrapper`1".
                options
                    .For<FunctionalEntityWrapper<FunctionalShoppingCart>>()
                    .SetStreamIdPrefix("functionalShoppingCart");
            })
            .Build();

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

        var eventTypeMap = new EventTypeMapping[]
            {
                new(typeof(ShoppingCartCreated)),
                new(typeof(ItemAddedToShoppingCart))
            }
            .ToEventTypeMap();

        var entityAdapterProvider = new EntityAdapterProvider(
            Enumerable.Empty<IEntityAdapter>(),
            new[] { new GenericEntityAdapterFactory(typeof(MutableEntityAdapter<>)) });

        var options = new EntityStoreOptions(eventTypeMap);
        var dataOptions = new EntityStoreOptions<string>(new JsonStringEventDataSerializer());

        var eventStore = new SqlStreamStoreEventStore(streamStore);
        var eventAdapter = new SqlStreamStoreEventAdapter();

        var store = EntityStore.Create(eventStore, eventAdapter, entityAdapterProvider, options, dataOptions);

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

        var v1Repository = new EntityStoreBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapterType(typeof(EntityAdapter<,>))
            .Configure<ShoppingCart>(options =>
            {
                options.SetStreamIdPrefix("shoppingCart");
                options.MapEventType<ShoppingCartCreated>();
                options.MapEventType<ItemAddedToShoppingCart>();
            })
            .Build();

        // Set up a new version of the ShoppingCartCreated event
        var v2Repository = new EntityStoreBuilder()
            .UseSqlStreamStore(streamStore)
            .AddEntityAdapterType(typeof(EntityAdapter<,>))
            .Configure<ShoppingCartV2>(options =>
            {
                options.SetStreamIdPrefix("shoppingCart");

                options.MapEventType<ShoppingCartCreatedV2>().WithName("ShoppingCartCreated");

                // TODO: Upcast
                options.MapEventType<ShoppingCartCreated>().WithName("ShoppingCartCreated");

                // Event name -> conditional deserialize -> transform

                // options
                //     .Upcast<ShoppingCartCreated>()
                //     .To(e => new ShoppingCartCreatedV2(e.Id, newFieldDefaultValue))
                //     .Where(meta => !meta.ContainsKey("Version"));
                options.MapEventType<ItemAddedToShoppingCart>();

                // options.EventTypes
                //     .Map<ShoppingCartCreatedV2>()
                //     .WithName("ShoppingCartCreated")
                //     .WithMetadata(() => new Dictionary<string, string>
                //     {
                //         { "Version", "v2" }
                //     });

                // TODO
                // stream.EventTypes
                //     .Map<ShoppingCartCreated>()
                //     .WithReadCondition(meta => !meta.ContainsKey("Version"))
                //     .WithUpcastTo(e => new ShoppingCartCreatedV2(e.Id, newFieldDefaultValue));
            })
            .Build();

        Guid cartId1;

        // Write to a new stream with V1 of the event
        {
            var entity = new ShoppingCart();
            entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item Foo"));
            await v1Repository.SaveAsync(entity);

            Assert.That(entity.State.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(entity.State.Items, Has.Count.EqualTo(1));
            Assert.That(entity.State.Items[0].Name, Is.EqualTo("Item Foo"));

            cartId1 = entity.State.Id;
        }

        // Test restoring and writing to existing stream containing the V1 event, using the repository set up with the
        // refactored code.
        {
            var entity = await v2Repository.LoadAsync<ShoppingCartV2>(cartId1.ToString());
            var state = entity.State;

            Assert.That(state.Id, Is.EqualTo(cartId1));
            Assert.That(state.Items, Has.Count.EqualTo(1));
            Assert.That(state.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(state.NewField, Is.EqualTo(newFieldDefaultValue));

            entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item Bar"));
            await v2Repository.SaveAsync(entity);

            var reloadedEntity = await v2Repository.LoadAsync<ShoppingCartV2>(cartId1.ToString());
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
            await v2Repository.SaveAsync(entity);

            var reloadedEntity = await v2Repository.LoadAsync<ShoppingCartV2>(entity.State.Id.ToString());
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
                    typeof(global::DomainBlocks.ThirdParty.SqlStreamStore.Streams.WrongExpectedVersionException))
                .SetName(nameof(SqlStreamStoreEntityStore));

            yield return new TestCaseData(
                    EventStoreDbEntityStore,
                    typeof(global::EventStore.Client.WrongExpectedVersionException))
                .SetName(nameof(EventStoreDbEntityStore));
        }
    }
}