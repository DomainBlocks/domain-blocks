namespace DomainBlocks.V1.Persistence;

public class EntityStreamConfig
{
    public EntityStreamConfig(Type entityType, string? streamNamePrefix = null)
    {
        EntityType = entityType;
        StreamNamePrefix = streamNamePrefix;
    }

    public Type EntityType { get; }
    public string? StreamNamePrefix { get; }
}