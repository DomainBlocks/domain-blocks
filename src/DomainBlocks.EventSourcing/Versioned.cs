namespace DomainBlocks.EventSourcing;

public static class Versioned
{
    public static Versioned<TEntity> New<TEntity>(TEntity entity) => new(entity, -1);

    internal static Versioned<TEntity> From<TEntity>(TEntity entity, long expectedVersion) =>
        new(entity, expectedVersion);
}

public sealed class Versioned<TEntity>
{
    internal Versioned(TEntity entity, long expectedVersion)
    {
        Entity = entity;
        ExpectedVersion = expectedVersion;
    }

    public TEntity Entity { get; }
    public long ExpectedVersion { get; }

    public Versioned<TEntity> With(Func<TEntity, TEntity> update) => With(update(Entity));

    public Versioned<TEntity> With(TEntity entity) => new(entity, ExpectedVersion);
}