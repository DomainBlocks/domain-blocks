using DomainBlocks.V1.Persistence.Entities;

namespace DomainBlocks.V1.Tests.Persistence.Entities;

[TestFixture]
public class EntityAdapterRegistryTests
{
    [Test]
    public void Constructor_NonGenericEntityAdapterImplementation_ThrowsArgumentException()
    {
        var entityAdapter = new TestEntityAdapter();

        var entityAdapters = new Dictionary<Type, IEntityAdapter>
        {
            { entityAdapter.EntityType, entityAdapter }
        };

        var genericEntityAdapterFactories = Enumerable.Empty<GenericEntityAdapterFactory>();

        Assert.That(
            () => _ = new EntityAdapterRegistry(entityAdapters, genericEntityAdapterFactories),
            Throws.TypeOf<ArgumentException>());
    }

    private class TestEntityAdapter : IEntityAdapter
    {
        public Type EntityType => typeof(object);
    }
}