namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider(IEnumerable<IEntityAdapterProvider> providers) : IEntityAdapterProvider
{
    private readonly IEntityAdapterProvider[] _providers = providers.ToArray();

    public IEntityAdapter<TEntity>? GetAdapter<TEntity>() where TEntity : notnull
    {
        return _providers.Select(x => x.GetAdapter<TEntity>()).FirstOrDefault(x => x != null);
    }
}