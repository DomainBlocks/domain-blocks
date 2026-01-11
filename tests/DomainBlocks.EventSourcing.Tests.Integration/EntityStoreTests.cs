using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;
using DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Strict;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[TestFixture]
public class EntityStoreTests
{
    private EntityStore<IDomainEvent> _entityStore = null!;
    private MongoClient _mongoClient = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var eventTypeMap = new EventTypeMapBuilder()
            .MapType<ShoppingSessionStarted>()
            .MapType<ItemAddedToShoppingCart>()
            .MapType<ItemRemovedFromShoppingCart>()
            .Build();

        var codecOptions = new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = eventTypeMap,
            EventSerializer = new BsonDocumentSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        };

        var options = new MongoEventStoreClientOptions<IDomainEvent>
        {
            CollectionOptions = EventStoreCollectionOptions.Default,
            EventCodec = EventCodec.Create(codecOptions)
        };

        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        var eventStoreClient = new MongoEventStoreClient<IDomainEvent>(_mongoClient, options);

        var entityDefinitionProvider = new CompositeEntityDefinitionProvider<IDomainEvent>(
        [
            new GenericEntityDefinitionProvider<IDomainEvent>(typeof(AggregateDefinition<,>), [123, "ABC"]),
            //new GenericEntityDefinitionProvider<IDomainEvent>(typeof(AggregateDefinition2<,>)),
            new GenericEntityDefinitionProvider<IDomainEvent>(typeof(MutableAggregateDefinition<>)),
            new GenericEntityDefinitionProvider<IDomainEvent>(typeof(FunctionalAggregateWrapperDefinition<>))
        ]);

        _entityStore = new EntityStore<IDomainEvent>(eventStoreClient, entityDefinitionProvider);

        await MongoEventStoreAdmin.EnsureIndexesAsync(_mongoClient, options.CollectionOptions);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
    }

    [Test]
    public async Task WriteToExpectedNewStream_WhenStreamDoesNotExist_Succeeds()
    {
        var entity = new ShoppingCart();
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await _entityStore.SaveAsync(Versioned.New(entity));

        var reloaded = await _entityStore.LoadAsync<ShoppingCart>(entity.State.SessionId.ToString()).AsEntity();

        reloaded.State.SessionId.ShouldBe(entity.State.SessionId);
        reloaded.State.Items.ShouldBe(entity.State.Items);
    }

    [Test]
    public async Task WriteToExpectedNewStream_WhenStreamExists_ThrowsWrongExpectedStreamStateException()
    {
        var entity1 = new ShoppingCart();
        entity1.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await _entityStore.SaveAsync(Versioned.New(entity1));

        // Attempting to write a new state stream for the same ID should fail.
        var entity2 = new ShoppingCart
        {
            State = new ShoppingCartState { SessionId = entity1.State.SessionId }
        };

        entity2.AddItem(new ShoppingCartItem(entity1.State.SessionId, "Bar"));

        await _entityStore.SaveAsync(Versioned.New(entity2)).ShouldThrowAsync<StreamAppendConflictException>();
    }

    [Test]
    public async Task WriteToExpectedExisingStream_WhenNoStream_Succeeds()
    {
        var id = Guid.NewGuid();

        var versioned = await _entityStore.LoadOrCreateAsync<ShoppingCart>(id.ToString());
        versioned.Entity.AddItem(new ShoppingCartItem(id, "Foo"));

        await _entityStore.SaveAsync(versioned);

        var reloaded = await _entityStore.LoadAsync<ShoppingCart>(id.ToString()).AsEntity();

        reloaded.State.SessionId.ShouldBe(versioned.Entity.State.SessionId);
        reloaded.State.Items.ShouldBe(versioned.Entity.State.Items);
    }

    [Test]
    public async Task WriteToExpectedExisingStream_WhenStreamExists_Succeeds()
    {
        var entity1A = new ShoppingCart();
        entity1A.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo"));
        await _entityStore.SaveAsync(Versioned.New(entity1A));

        var entity1B = await _entityStore.LoadAsync<ShoppingCart>(entity1A.State.SessionId.ToString());
        entity1B.Entity.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar"));
        await _entityStore.SaveAsync(entity1B);

        var reloaded = await _entityStore.LoadAsync<ShoppingCart>(entity1A.State.SessionId.ToString()).AsEntity();

        reloaded.State.SessionId.ShouldBe(entity1A.State.SessionId);
        reloaded.State.Items.ShouldBe(entity1B.Entity.State.Items);
    }

    [Test]
    public async Task LoadAsync_WhenStreamDoesNotExist_ThrowsStreamNotFoundException()
    {
        const string id = "cart-1";

        var exception = await _entityStore.LoadAsync<ShoppingCart>(id).ShouldThrowAsync<StreamNotFoundException>();

        exception.Message.ShouldBe("Stream 'shoppingCart-cart-1' not found.");
    }

    [Test]
    public async Task LoadAsync_WhenStreamExists_Succeeds()
    {
        var shoppingCart = new ShoppingCart();
        shoppingCart.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Item 1"));
        await _entityStore.SaveAsync(Versioned.New(shoppingCart));

        await _entityStore.LoadAsync<ShoppingCart>(shoppingCart.Id).ShouldNotThrowAsync();
    }

    [Test]
    public async Task LoadOrCreateAsync_WhenStreamDoesNotExist_Succeeds()
    {
        await _entityStore.LoadOrCreateAsync<ShoppingCart>("cart-1").ShouldNotThrowAsync();
    }

    [Test]
    public async Task MutableScenario()
    {
        var entity = new MutableShoppingCart();
        var sessionId = Guid.NewGuid();
        entity.AddItem(new ShoppingCartItem(sessionId, "Foo"));
        entity.AddItem(new ShoppingCartItem(sessionId, "Bar"));
        await _entityStore.SaveAsync(Versioned.New(entity));

        var reloaded = await _entityStore.LoadAsync<MutableShoppingCart>(entity.Id.ToString()).AsEntity();

        reloaded.Id.ShouldBe(entity.Id);
        reloaded.Items.ShouldBe(entity.Items);
    }

    [Test]
    public async Task FunctionalEntityWrapperScenario()
    {
        var entity = new FunctionalAggregateWrapper<FunctionalShoppingCart>();
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Foo")));
        entity.Execute(x => x.AddItem(new ShoppingCartItem(Guid.NewGuid(), "Bar")));
        await _entityStore.SaveAsync(Versioned.New(entity));

        var reloaded = await _entityStore
            .LoadAsync<FunctionalAggregateWrapper<FunctionalShoppingCart>>(entity.Id.ToString())
            .AsEntity();

        reloaded.Id.ShouldBe(entity.Id);
        reloaded.Entity.Items.ShouldBe(entity.Entity.Items);
    }
}