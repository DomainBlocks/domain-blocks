using System.Text.Json;
using DomainBlocks.EventStore.Experimental;
using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;
using DomainBlocks.SqlStreamStore.Experimental;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Commands;
using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental;

[TestFixture]
public class EventSourcedStateTests
{
    private const string PostgresStreamStoreConnectionString =
        "Server=localhost;Port=5433;Database=shopping;User Id=postgres;Password=postgres;";

    private const string EventStoreConnectionString = "esdb://localhost:2113?tls=false";

    private static readonly IEventSourcedStateRepository SqlStreamStoreRepository;
    private static readonly IEventSourcedStateRepository EventStoreStoreRepository;

    static EventSourcedStateTests()
    {
        // Still using the pre-Npgsql 6.0 timestamp behaviour.
        // See: https://www.npgsql.org/doc/types/datetime.html#timestamps-and-timezones
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { new MutableShoppingCartJsonConverter() }
        };

        SqlStreamStoreRepository = EventSourcedStateRepository
            .CreateBuilder()
            .UseSqlStreamStore(streamStore)
            .ConfigureDefaults(defaults =>
            {
                defaults.EventTypes
                    .MapAll<IDomainEvent>()
                    .Where(type => type != typeof(ItemRemovedFromShoppingCart))
                    .WithApplierMethodName("ApplyEvent");

                defaults.SetSnapshotEventCount(2);
                defaults.UseJsonSerialization(jsonOptions);
            })
            .Build();

        EventStoreStoreRepository = EventSourcedStateRepository
            .CreateBuilder()
            .UseEventStore(EventStoreConnectionString)
            .Configure<MutableShoppingCart>(stream =>
            {
                stream.EventTypes
                    .MapAll<IDomainEvent>()
                    .Where(type => type != typeof(ItemRemovedFromShoppingCart))
                    .WithApplier((s, e) => s.ApplyEvent(e));

                stream.SetSnapshotEventCount(2);
                stream.UseJsonSerialization(jsonOptions);
            })
            .Build();
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task WriteToExpectedNewStream_WhenNoStream_Succeeds(IEventSourcedStateRepository repository)
    {
        var id = Guid.NewGuid();

        var (state, appender) = repository.New<MutableShoppingCart>(id.ToString());
        var command = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");
        state.Execute(command);
        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        var reloadedState = await repository.ReadOnlyRestoreAsync<MutableShoppingCart>(id.ToString());

        Assert.That(reloadedState.Id, Is.EqualTo(state.Id));
        Assert.That(reloadedState.Items, Has.Count.EqualTo(state.Items.Count));
    }

    [Test]
    [TestCaseSource(nameof(WrongVersionTestCases))]
    public async Task WriteToExpectedNewStream_WhenStreamExists_ThrowsWrongVersionException(
        IEventSourcedStateRepository repository, Type wrongVersionExceptionType)
    {
        var id = Guid.NewGuid();

        var (state, appender) = repository.New<MutableShoppingCart>(id.ToString());
        var command = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");
        state.Execute(command);
        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        // Attempting to write a new state stream for the same ID should fail.
        (state, appender) = repository.New<MutableShoppingCart>(id.ToString());
        state.Execute(command);
        appender.Append(state.RaisedEvents);

        Assert.ThrowsAsync(wrongVersionExceptionType, () => appender.CommitAsync());
    }

    [Test]
    [TestCaseSource(nameof(WrongVersionTestCases))]
    public async Task WriteToExpectedExisingStream_WhenNoStream_Succeeds(
        IEventSourcedStateRepository repository, Type wrongVersionExceptionType)
    {
        var id = Guid.NewGuid();

        var (state, appender) = await repository.RestoreAsync<MutableShoppingCart>(id.ToString());
        var command = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");
        state.Execute(command);
        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        var reloadedState = await repository.ReadOnlyRestoreAsync<MutableShoppingCart>(id.ToString());

        Assert.That(reloadedState.Id, Is.EqualTo(state.Id));
        Assert.That(reloadedState.Items, Has.Count.EqualTo(state.Items.Count));
    }

    [Test]
    [TestCaseSource(nameof(WrongVersionTestCases))]
    public async Task WriteToExpectedExisingStream_WhenStreamExists_Succeeds(
        IEventSourcedStateRepository repository, Type wrongVersionExceptionType)
    {
        var id = Guid.NewGuid();

        var (state, appender) = await repository.RestoreAsync<MutableShoppingCart>(id.ToString());
        var command1 = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");
        state.Execute(command1);
        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        (state, appender) = await repository.RestoreAsync<MutableShoppingCart>(id.ToString());
        var command2 = new AddItemToShoppingCart(id, Guid.NewGuid(), "Bar");
        state.Execute(command2);
        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        var reloadedState = await repository.ReadOnlyRestoreAsync<MutableShoppingCart>(id.ToString());

        Assert.That(reloadedState.Id, Is.EqualTo(id));
        Assert.That(reloadedState.Items, Has.Count.EqualTo(2));
        Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Foo"));
        Assert.That(reloadedState.Items[1].Name, Is.EqualTo("Bar"));
    }

    [Test]
    public async Task ImmutableScenario1()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        var repository = EventSourcedStateRepository
            .CreateBuilder()
            .UseSqlStreamStore(streamStore)
            .Configure<ShoppingCartState>(stream =>
            {
                stream.EventTypes
                    .MapAll<IDomainEvent>()
                    .WithApplier(ShoppingCartFunctions.Apply);
            })
            .Build();

        var id = Guid.NewGuid();

        var (state, appender) = repository.New<ShoppingCartState>(id.ToString());
        var command = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");

        var events = ShoppingCartFunctions.Execute(state, command).ToList();
        state = events.Aggregate(state, ShoppingCartFunctions.Apply);

        appender.Append(events);
        await appender.CommitAsync();

        var reloadedState = await repository.ReadOnlyRestoreAsync<ShoppingCartState>(id.ToString());

        Assert.That(reloadedState.Id, Is.EqualTo(state.Id));
        Assert.That(reloadedState.Items, Has.Count.EqualTo(state.Items.Count));
    }

    [Test]
    public async Task ImmutableScenario2()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        var repository = EventSourcedStateRepository
            .CreateBuilder()
            .UseSqlStreamStore(streamStore)
            .Build();

        var id = Guid.NewGuid();

        var (state, appender) = repository.New<ImmutableShoppingCart>(id.ToString());

        var command = new AddItemToShoppingCart(id, Guid.NewGuid(), "Foo");
        state = state.AddItem(id, command.Id, command.Item);

        appender.Append(state.RaisedEvents);
        await appender.CommitAsync();

        var reloadedState = await repository.ReadOnlyRestoreAsync<ImmutableShoppingCart>(id.ToString());

        Assert.That(reloadedState.Id, Is.EqualTo(state.Id));
        Assert.That(reloadedState.Items, Has.Count.EqualTo(state.Items.Count));
    }

    [Test]
    public async Task EventUpcastScenario()
    {
        var streamStoreSettings = new PostgresStreamStoreSettings(PostgresStreamStoreConnectionString);
        var streamStore = new PostgresStreamStore(streamStoreSettings);

        const string newFieldDefaultValue = "default value";

        var v1Repository = EventSourcedStateRepository
            .CreateBuilder()
            .UseSqlStreamStore(streamStore)
            .Configure<ImmutableShoppingCart>(stream => stream.SetStreamIdPrefix("shoppingCart"))
            .Build();

        // Set up a new version of the ShoppingCartCreated event
        var v2Repository = EventSourcedStateRepository
            .CreateBuilder()
            .UseSqlStreamStore(streamStore)
            .Configure<ImmutableShoppingCartV2>(stream =>
            {
                stream.SetStreamIdPrefix("shoppingCart");

                stream.EventTypes
                    .Map<ShoppingCartCreatedV2>()
                    .WithName("ShoppingCartCreated")
                    .WithMetadata(() => new Dictionary<string, string>
                    {
                        { "Version", "v2" }
                    });

                stream.EventTypes
                    .Map<ShoppingCartCreated>()
                    .WithReadCondition(meta => !meta.ContainsKey("Version"))
                    .WithUpcastTo(e => new ShoppingCartCreatedV2(e.Id, newFieldDefaultValue));
            })
            .Build();

        var cartId1 = Guid.NewGuid();

        // Write to a new stream with V1 of the event
        {
            var (state, appender) = v1Repository.New<ImmutableShoppingCart>(cartId1.ToString());
            state = state.AddItem(cartId1, Guid.NewGuid(), "Item Foo");
            appender.Append(state.RaisedEvents);
            await appender.CommitAsync();

            var reloadedState = await v1Repository.ReadOnlyRestoreAsync<ImmutableShoppingCart>(cartId1.ToString());

            Assert.That(reloadedState.Id, Is.EqualTo(cartId1));
            Assert.That(reloadedState.Items, Has.Count.EqualTo(1));
            Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Item Foo"));
        }

        // Test restoring and writing to existing stream containing the V1 event, using the repository set up with the
        // refactored code.
        {
            var (state, appender) = await v2Repository.RestoreAsync<ImmutableShoppingCartV2>(cartId1.ToString());

            Assert.That(state.Id, Is.EqualTo(cartId1));
            Assert.That(state.Items, Has.Count.EqualTo(1));
            Assert.That(state.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(state.NewField, Is.EqualTo(newFieldDefaultValue));

            state = state.AddItem(cartId1, Guid.NewGuid(), "Item Bar");
            appender.Append(state.RaisedEvents);
            await appender.CommitAsync();

            var reloadedState = await v2Repository.ReadOnlyRestoreAsync<ImmutableShoppingCartV2>(cartId1.ToString());

            Assert.That(reloadedState.Id, Is.EqualTo(cartId1));
            Assert.That(reloadedState.Items, Has.Count.EqualTo(2));
            Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(reloadedState.Items[1].Name, Is.EqualTo("Item Bar"));
            Assert.That(reloadedState.NewField, Is.EqualTo(newFieldDefaultValue));
        }

        // Test writing to a new stream with the refactored code - should write V2 of the event
        {
            var cartId2 = Guid.NewGuid();
            var (state, appender) = v2Repository.New<ImmutableShoppingCartV2>(cartId2.ToString());
            state = state.AddItem(cartId2, Guid.NewGuid(), "Item Foo", "New value");
            appender.Append(state.RaisedEvents);
            await appender.CommitAsync();

            var reloadedState = await v2Repository.ReadOnlyRestoreAsync<ImmutableShoppingCartV2>(cartId2.ToString());

            Assert.That(reloadedState.Id, Is.EqualTo(state.Id));
            Assert.That(reloadedState.Items, Has.Count.EqualTo(1));
            Assert.That(reloadedState.Items[0].Name, Is.EqualTo("Item Foo"));
            Assert.That(reloadedState.NewField, Is.EqualTo("New value"));
        }
    }

    private static IEnumerable<TestCaseData> TestCases
    {
        get
        {
            yield return new TestCaseData(SqlStreamStoreRepository).SetName(nameof(SqlStreamStoreRepository));
            yield return new TestCaseData(EventStoreStoreRepository).SetName(nameof(EventStoreStoreRepository));
        }
    }

    private static IEnumerable<TestCaseData> WrongVersionTestCases
    {
        get
        {
            yield return new TestCaseData(
                    SqlStreamStoreRepository,
                    typeof(global::DomainBlocks.ThirdParty.SqlStreamStore.Streams.WrongExpectedVersionException))
                .SetName(nameof(SqlStreamStoreRepository));

            yield return new TestCaseData(
                    EventStoreStoreRepository,
                    typeof(global::EventStore.Client.WrongExpectedVersionException))
                .SetName(nameof(EventStoreStoreRepository));
        }
    }
}