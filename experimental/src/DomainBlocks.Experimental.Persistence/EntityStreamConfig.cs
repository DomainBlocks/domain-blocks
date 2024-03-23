namespace DomainBlocks.Experimental.Persistence;

public class EntityStreamConfig
{
    public EntityStreamConfig(Type entityType, int? snapshotEventCount = null, string? streamIdPrefix = null)
    {
        EntityType = entityType;
        SnapshotEventCount = snapshotEventCount;
        StreamIdPrefix = streamIdPrefix;
    }

    public Type EntityType { get; }
    public int? SnapshotEventCount { get; }
    public string? StreamIdPrefix { get; }
}