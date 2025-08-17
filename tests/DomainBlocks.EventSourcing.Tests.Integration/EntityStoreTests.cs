using DomainBlocks.EventSourcing.Tests.Integration.Adapters;
using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;
using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.MongoDB.Events;
using DomainBlocks.Serialization.Events;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[TestFixture]
public class EntityStoreTests
{
    private EntityStore _entityStore = null!;

    static EntityStoreTests()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    [SetUp]
    public void SetUp()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var collection = database.GetCollection<BsonDocument>("events");

        var eventDataStore = new MongoEventDataStore<BsonDocument>(
            collection,
            x => x,
            x => x.AsBsonDocument);

        EventTypeMapping[] mappings =
        [
            new(typeof(ShoppingSessionStarted)),
            new(typeof(ItemAddedToShoppingCart)),
            new(typeof(ItemRemovedFromShoppingCart))
        ];

        var serializer = new MongoBsonDocumentSerializer();
        var eventSerializer = new EventSerializer<BsonDocument>(mappings, serializer);
        var eventStore = new EventStore<object, BsonDocument>(eventDataStore, eventSerializer);

        var entityAdapterProvider = new CompositeEntityAdapterProvider(
        [
            //new GenericEntityAdapterProvider(typeof(AggregateAdapter<,>), [123, "ABC"]),
            new GenericEntityAdapterProvider(typeof(AggregateAdapter2<,>)),
            new GenericEntityAdapterProvider(typeof(MutableAggregateAdapter<>)),
            new GenericEntityAdapterProvider(typeof(FunctionalAggregateWrapperAdapter<>))
        ]);

        _entityStore = new EntityStore(eventStore, entityAdapterProvider);
    }

    [Test]
    public async Task WriteToExpectedNewStream_WhenNoStream_Succeeds()
    {
        var entity = new ShoppingCart();
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await _entityStore.SaveAsync(entity);

        var reloadedEntity = await _entityStore.LoadAsync<ShoppingCart>(entity.State.SessionId.ToString());

        reloadedEntity.State.SessionId.ShouldBe(entity.State.SessionId);
        reloadedEntity.State.Items.ShouldBe(entity.State.Items);
    }

    [Test]
    public async Task WriteToExpectedNewStream_WhenStreamExists_ThrowsWrongVersionException()
    {
        var entity1 = new ShoppingCart();
        entity1.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await _entityStore.SaveAsync(entity1);

        // Attempting to write a new state stream for the same ID should fail.
        var entity2 = new ShoppingCart
        {
            State = new ShoppingCartState { SessionId = entity1.State.SessionId }
        };

        entity2.AddItem(new ShoppingCartItem(entity1.State.SessionId, "Bar"));

        await Should.ThrowAsync<WrongExpectedVersionException>(() => _entityStore.SaveAsync(entity2));
    }

    [Test]
    public async Task WriteToExpectedExisingStream_WhenNoStream_Succeeds()
    {
        var id = Guid.NewGuid();

        var entity = await _entityStore.LoadOrCreateAsync<ShoppingCart>(id.ToString());
        entity.AddItem(new ShoppingCartItem(id, "Foo"));
        await _entityStore.SaveAsync(entity);

        var reloadedEntity = await _entityStore.LoadAsync<ShoppingCart>(id.ToString());

        reloadedEntity.State.SessionId.ShouldBe(entity.State.SessionId);
        reloadedEntity.State.Items.ShouldBe(entity.State.Items);
    }

    [Test]
    public async Task WriteToExpectedExisingStream_WhenStreamExists_Succeeds()
    {
        var entity1A = new ShoppingCart();
        entity1A.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await _entityStore.SaveAsync(entity1A);

        var entity1B = await _entityStore.LoadAsync<ShoppingCart>(entity1A.State.SessionId.ToString());
        entity1B.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await _entityStore.SaveAsync(entity1B);

        var reloadedEntity = await _entityStore.LoadAsync<ShoppingCart>(entity1A.State.SessionId.ToString());

        reloadedEntity.State.SessionId.ShouldBe(entity1A.State.SessionId);
        reloadedEntity.State.Items.ShouldBe(entity1B.State.Items);
    }

    [Test]
    public async Task LoadAsync_WhenStreamDoesNotExist_ThrowsStreamNotFoundException()
    {
        const string id = "cart-1";

        var exception = await Should.ThrowAsync<StreamNotFoundException>(() =>
            _entityStore.LoadAsync<ShoppingCart>(id));

        exception.Message.ShouldBe("Stream 'shoppingCart-cart-1' could not be found.");
    }

    [Test]
    public async Task LoadAsync_WhenStreamExists_Succeeds()
    {
        var shoppingCart = new ShoppingCart();
        shoppingCart.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item 1"));
        await _entityStore.SaveAsync(shoppingCart);

        await Should.NotThrowAsync(() => _entityStore.LoadAsync<ShoppingCart>(shoppingCart.Id));
    }

    [Test]
    public async Task LoadOrCreateAsync_WhenStreamDoesNotExist_Succeeds()
    {
        await Should.NotThrowAsync(() => _entityStore.LoadOrCreateAsync<ShoppingCart>("cart-1"));
    }

    [Test]
    public async Task MutableScenario()
    {
        var entity = new MutableShoppingCart();
        var sessionId = Guid.NewGuid();
        entity.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        entity.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await _entityStore.SaveAsync(entity);

        var reloadedEntity = await _entityStore.LoadAsync<MutableShoppingCart>(entity.Id.ToString());

        reloadedEntity.Id.ShouldBe(entity.Id);
        reloadedEntity.Items.ShouldBe(entity.Items);
    }

    [Test]
    public async Task FunctionalEntityWrapperScenario()
    {
        var entity = new FunctionalAggregateWrapper<FunctionalShoppingCart>();
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo")));
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar")));
        await _entityStore.SaveAsync(entity);

        var reloadedEntity =
            await _entityStore.LoadAsync<FunctionalAggregateWrapper<FunctionalShoppingCart>>(entity.Id.ToString());

        reloadedEntity.Id.ShouldBe(entity.Id);
        reloadedEntity.Entity.Items.ShouldBe(entity.Entity.Items);
    }
}