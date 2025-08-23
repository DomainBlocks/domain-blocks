using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public static class Versioned
{
    public static Versioned<TEntity> New<TEntity>(TEntity entity) => new(entity, ExpectedStreamVersion.None);

    internal static Versioned<TEntity> From<TEntity>(TEntity entity, ExpectedStreamVersion expectedVersion) =>
        new(entity, expectedVersion);
}

public sealed class Versioned<TEntity>
{
    internal Versioned(TEntity entity, ExpectedStreamVersion expectedVersion)
    {
        Entity = entity;
        ExpectedVersion = expectedVersion;
    }

    public TEntity Entity { get; }
    public ExpectedStreamVersion ExpectedVersion { get; }

    public Versioned<TEntity> With(Func<TEntity, TEntity> update) => With(update(Entity));

    public Versioned<TEntity> With(TEntity entity) => new(entity, ExpectedVersion);
}