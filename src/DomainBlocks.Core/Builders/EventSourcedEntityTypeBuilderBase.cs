namespace DomainBlocks.Core.Builders;

public abstract class EventSourcedEntityTypeBuilderBase<TEntity> :
    IEventSourcedEntityTypeBuilder,
    IIdentityBuilder<TEntity>,
    IKeyBuilder
{
    protected EventSourcedEntityTypeBuilderBase(EventSourcedEntityTypeBase<TEntity> entityType)
    {
        EntityType = entityType;
    }

    protected EventSourcedEntityTypeBase<TEntity> EntityType { get; set; }

    /// <summary>
    /// Specify a factory function for creating new instances of the entity type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IIdentityBuilder<TEntity> InitialState(Func<TEntity> factory)
    {
        EntityType = EntityType.SetFactory(factory);
        return this;
    }

    public IKeyBuilder HasId(Func<TEntity, string> idSelector)
    {
        EntityType = EntityType.SetIdSelector(idSelector);
        return this;
    }

    public void WithKeyPrefix(string prefix)
    {
        EntityType = EntityType.SetKeyPrefix(prefix);
    }

    ISnapshotKeyBuilder IKeyBuilder.WithStreamKey(Func<string, string> idToStreamKeySelector)
    {
        EntityType = EntityType.SetIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    void ISnapshotKeyBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        EntityType = EntityType.SetIdToSnapshotKeySelector(idToSnapshotKeySelector);
    }

    public IEventSourcedEntityType Build()
    {
        return OnBuild(EntityType);
    }

    protected abstract EventSourcedEntityTypeBase<TEntity> OnBuild(EventSourcedEntityTypeBase<TEntity> source);
}