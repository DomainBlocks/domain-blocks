namespace DomainBlocks.EventSourcing;

public static class VersionedExtensions
{
    public static async Task<TEntity> AsEntity<TEntity>(this Task<Versioned<TEntity>> task) where TEntity : notnull
    {
        var versioned = await task.ConfigureAwait(false);
        return versioned.Entity;
    }
}