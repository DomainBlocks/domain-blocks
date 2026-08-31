using DomainBlocks.EventSourcing.Tests.Integration.Adapters;
using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[TestFixture]
public class EventSourcedStateStoreTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;
    private MongoEventStore<IDomainEvent> _client = null!;
    private EventSourcedStateStore<IDomainEvent, string, StreamPosition, LogPosition> _stateStore = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(TestMongoConnectionStrings.Default);

        _options = new MongoEventStoreOptions
        {
            DatabaseName = "domainblocks_tests"
        };

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<ShoppingSessionStarted>(),
            EventTypeMapping.ReadWrite<ItemAddedToShoppingCart>(),
            EventTypeMapping.ReadWrite<ItemRemovedFromShoppingCart>());

        var eventCodec = TestMongoEventCodec.Create<IDomainEvent>(eventTypeMap);

        _client = MongoEventStore.Create(
            _mongoClient,
            eventCodec,
            _options,
            _loggerFactory.CreateLogger<MongoEventStore<IDomainEvent>>());

        var stateAdapterResolver = new CompositeStateAdapterResolver<IDomainEvent, string>(
        [
            new GenericStateAdapterResolver<IDomainEvent, string>(typeof(AggregateAdapter<,>), 123, "ABC"),
            new GenericStateAdapterResolver<IDomainEvent, string>(typeof(MutableAggregateAdapter<>)),
            new GenericStateAdapterResolver<IDomainEvent, string>(typeof(FunctionalAggregateWrapperAdapter<>))
        ]);

        _stateStore = new EventSourcedStateStore<IDomainEvent, string, StreamPosition, LogPosition>(
            _client,
            stateAdapterResolver);

        await MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);

        await _client.DisposeAsync();
        _loggerFactory.Dispose();
        _mongoClient.Dispose();
    }

    [Test]
    public async Task SaveNewAsync_WhenStreamDoesNotExist_Succeeds()
    {
        var cart = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        cart.AddItem(new ShoppingCartItem(sessionId, "Bar"));

        await _stateStore.SaveNewAsync(cart);

        var (reloaded, _) = await _stateStore.LoadRequiredAsync<ShoppingCart>(sessionId.ToString());

        reloaded.State.SessionId.ShouldBe(sessionId);
        reloaded.State.Items.ShouldBe(cart.State.Items);
    }

    [Test]
    public async Task SaveNewAsync_WhenStreamAlreadyExists_ThrowsStreamAppendConflictException()
    {
        var cart1 = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart1.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        await _stateStore.SaveNewAsync(cart1);

        // Attempting to write a new stream for the same ID should fail.
        var cart2 = new ShoppingCart();
        cart2.AddItem(new ShoppingCartItem(sessionId, "Bar"));

        await _stateStore.SaveNewAsync(cart2).ShouldThrowAsync<StreamAppendConflictException>();
    }

    [Test]
    public async Task SaveAsync_WhenStreamDoesNotExist_Succeeds()
    {
        var sessionId = Guid.NewGuid();

        var (cart, version) = await _stateStore.LoadAsync<ShoppingCart>(sessionId.ToString());
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));

        await _stateStore.SaveAsync(cart, version);

        var (reloaded, _) = await _stateStore.LoadRequiredAsync<ShoppingCart>(sessionId.ToString());

        reloaded.State.SessionId.ShouldBe(sessionId);
        reloaded.State.Items.ShouldBe(cart.State.Items);
    }

    [Test]
    public async Task SaveAsync_WhenStreamExists_Succeeds()
    {
        var cart = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        await _stateStore.SaveNewAsync(cart);

        var (reloaded1, version) = await _stateStore.LoadRequiredAsync<ShoppingCart>(sessionId.ToString());
        reloaded1.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await _stateStore.SaveAsync(reloaded1, version);

        var (reloaded2, _) = await _stateStore.LoadRequiredAsync<ShoppingCart>(sessionId.ToString());

        reloaded2.State.SessionId.ShouldBe(sessionId);
        reloaded2.State.Items.ShouldBe(reloaded1.State.Items);
    }

    [Test]
    public async Task LoadAsync_WhenStreamDoesNotExist_ReturnsInitialState()
    {
        var (cart, version) = await _stateStore.LoadAsync<ShoppingCart>("cart-1");

        cart.ShouldNotBeNull();
        version.HasValue.ShouldBeFalse();
    }

    [Test]
    public async Task LoadRequiredAsync_WhenStreamDoesNotExist_ThrowsStreamNotFoundException()
    {
        const string streamId = "shoppingCart-cart-1";

        var exception = await _stateStore
            .LoadRequiredAsync<ShoppingCart>(streamId)
            .ShouldThrowAsync<StreamNotFoundException>();

        exception.Message.ShouldBe($"Stream '{streamId}' not found.");
    }

    [Test]
    public async Task LoadRequiredAsync_WhenStreamExists_Succeeds()
    {
        var cart = new ShoppingCart();
        cart.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item 1"));
        await _stateStore.SaveNewAsync(cart);

        await _stateStore.LoadRequiredAsync<ShoppingCart>(cart.Id).ShouldNotThrowAsync();
    }

    [Test]
    public async Task MutableScenario()
    {
        var cart = new MutableShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        cart.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await _stateStore.SaveNewAsync(cart);

        var (reloaded, _) = await _stateStore.LoadRequiredAsync<MutableShoppingCart>(cart.Id.ToString());

        reloaded.Id.ShouldBe(cart.Id);
        reloaded.Items.ShouldBe(cart.Items);
    }

    [Test]
    public async Task FunctionalAggregateWrapperScenario()
    {
        var cart = new FunctionalAggregateWrapper<FunctionalShoppingCart>();
        var sessionId = Guid.NewGuid();
        cart.Execute(x => x.AddItem(new ShoppingCartItem(sessionId, "Foo")));
        cart.Execute(x => x.AddItem(new ShoppingCartItem(sessionId, "Bar")));
        await _stateStore.SaveNewAsync(cart);

        var (reloaded, _) = await _stateStore
            .LoadRequiredAsync<FunctionalAggregateWrapper<FunctionalShoppingCart>>(cart.Id.ToString());

        reloaded.Id.ShouldBe(cart.Id);
        reloaded.Value.Items.ShouldBe(cart.Value.Items);
    }
}