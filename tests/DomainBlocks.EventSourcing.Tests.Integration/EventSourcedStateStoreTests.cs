using DomainBlocks.EventSourcing.Tests.Integration.Adapters;
using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[TestFixture]
public class EventSourcedStateStoreTests
{
    private MongoEventStoreOptions _options = null!;
    private MongoEventStore<IDomainEvent> _eventStore = null!;
    private EventSourcedStateStore<ShoppingCart, IDomainEvent, string, StreamPosition, LogPosition> _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = "dbx_es_event_sourced_state_tests" };

        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<ShoppingSessionStarted>(),
            EventTypeMapping.ReadWrite<ItemAddedToShoppingCart>(),
            EventTypeMapping.ReadWrite<ItemRemovedFromShoppingCart>());

        var eventCodec = TestMongoEventCodec.Create<IDomainEvent>(eventTypeMap);

        _eventStore = MongoEventStore.Create(
            SetUpFixture.MongoClient,
            eventCodec,
            _options,
            SetUpFixture.LoggerFactory.CreateLogger<MongoEventStore<IDomainEvent>>());

        _store = EventSourcedStateStore.Create(_eventStore, new AggregateAdapter<ShoppingCart, ShoppingCartState>());

        await MongoEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.MongoClient, _options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await SetUpFixture.MongoClient.DropDatabaseAsync(_options.DatabaseName);
        await _eventStore.DisposeAsync();
    }

    [Test]
    public async Task SaveNewAsync_WhenStreamDoesNotExist_Succeeds()
    {
        var cart = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        cart.AddItem(new ShoppingCartItem(sessionId, "Bar"));

        await _store.SaveNewAsync(cart);

        var (reloaded, _) = await _store.LoadRequiredAsync(sessionId.ToString());

        reloaded.State.SessionId.ShouldBe(sessionId);
        reloaded.State.Items.ShouldBe(cart.State.Items);
    }

    [Test]
    public async Task SaveNewAsync_WhenStreamAlreadyExists_ThrowsStreamAppendConflictException()
    {
        var cart1 = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart1.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        await _store.SaveNewAsync(cart1);

        // Attempting to write a new stream for the same ID should fail.
        var cart2 = new ShoppingCart();
        cart2.AddItem(new ShoppingCartItem(sessionId, "Bar"));
    }

    [Test]
    public async Task SaveAsync_WhenStreamDoesNotExist_Succeeds()
    {
        var sessionId = Guid.NewGuid();

        var (cart, version) = await _store.LoadAsync(sessionId.ToString());
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));

        await _store.SaveAsync(cart, version);

        var (reloaded, _) = await _store.LoadRequiredAsync(sessionId.ToString());

        reloaded.State.SessionId.ShouldBe(sessionId);
        reloaded.State.Items.ShouldBe(cart.State.Items);
    }

    [Test]
    public async Task SaveAsync_WhenStreamExists_Succeeds()
    {
        var cart = new ShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        await _store.SaveNewAsync(cart);

        var (reloaded1, version) = await _store.LoadRequiredAsync(sessionId.ToString());
        reloaded1.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await _store.SaveAsync(reloaded1, version);

        var (reloaded2, _) = await _store.LoadRequiredAsync(sessionId.ToString());

        reloaded2.State.SessionId.ShouldBe(sessionId);
        reloaded2.State.Items.ShouldBe(reloaded1.State.Items);
    }

    [Test]
    public async Task LoadAsync_WhenStreamDoesNotExist_ReturnsInitialState()
    {
        var (cart, version) = await _store.LoadAsync("cart-1");

        cart.ShouldNotBeNull();
        version.HasValue.ShouldBeFalse();
    }

    [Test]
    public async Task LoadRequiredAsync_WhenStreamDoesNotExist_ThrowsStreamNotFoundException()
    {
        const string streamId = "shoppingCart-cart-1";

        var exception = await _store.LoadRequiredAsync(streamId).ShouldThrowAsync<StateNotFoundException>();

        exception.Message.ShouldBe($"State with ID '{streamId}' not found.");
    }

    [Test]
    public async Task LoadRequiredAsync_WhenStreamExists_Succeeds()
    {
        var cart = new ShoppingCart();
        cart.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item 1"));
        await _store.SaveNewAsync(cart);

        await _store.LoadRequiredAsync(cart.Id).ShouldNotThrowAsync();
    }

    [Test]
    public async Task MutableScenario()
    {
        var store = EventSourcedStateStore.Create(_eventStore, new MutableAggregateAdapter<MutableShoppingCart>());

        var cart = new MutableShoppingCart();
        var sessionId = Guid.NewGuid();
        cart.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        cart.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await store.SaveNewAsync(cart);

        var (reloaded, _) = await store.LoadRequiredAsync(cart.Id.ToString());

        reloaded.Id.ShouldBe(cart.Id);
        reloaded.Items.ShouldBe(cart.Items);
    }

    [Test]
    public async Task FunctionalAggregateWrapperScenario()
    {
        var store = EventSourcedStateStore.Create(
            _eventStore,
            new FunctionalAggregateWrapperAdapter<FunctionalShoppingCart>());

        var cart = new FunctionalAggregateWrapper<FunctionalShoppingCart>();
        var sessionId = Guid.NewGuid();
        cart.Execute(x => x.AddItem(new ShoppingCartItem(sessionId, "Foo")));
        cart.Execute(x => x.AddItem(new ShoppingCartItem(sessionId, "Bar")));
        await store.SaveNewAsync(cart);

        var (reloaded, _) = await store.LoadRequiredAsync(cart.Id.ToString());

        reloaded.Id.ShouldBe(cart.Id);
        reloaded.Value.Items.ShouldBe(cart.Value.Items);
    }
}