using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventSourcing;

public static class Versioned
{
    public static Versioned<TEntity> New<TEntity>(TEntity entity) => new(entity, StreamVersion.None);

    internal static Versioned<TEntity> From<TEntity>(TEntity entity, StreamVersion expectedVersion) =>
        new(entity, expectedVersion);
}

public sealed class Versioned<TEntity>
{
    internal Versioned(TEntity entity, StreamVersion version)
    {
        Entity = entity;
        Version = version;
    }

    public TEntity Entity { get; }
    public StreamVersion Version { get; }

    public Versioned<TEntity> With(Func<TEntity, TEntity> update) => With(update(Entity));

    public Versioned<TEntity> With(TEntity entity) => new(entity, Version);
}