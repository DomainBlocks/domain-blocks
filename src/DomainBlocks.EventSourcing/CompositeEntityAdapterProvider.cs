namespace DomainBlocks.EventSourcing;

public class CompositeEntityAdapterProvider(IEnumerable<IEntityAdapterProvider> providers) : IEntityAdapterProvider
{
    private readonly IEntityAdapterProvider[] _providers = providers.ToArray();

    public IEntityAdapter<TEntity>? GetFor<TEntity>() where TEntity : notnull
    {
        return _providers.Select(x => x.GetFor<TEntity>()).FirstOrDefault(x => x != null);
    }
}