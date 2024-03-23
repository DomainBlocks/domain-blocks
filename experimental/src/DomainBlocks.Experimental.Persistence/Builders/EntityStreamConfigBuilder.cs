namespace DomainBlocks.Experimental.Persistence.Builders;

public class EntityStreamConfigBuilder
{
    private readonly Type _entityType;
    private int? _snapshotEventCount;
    private string _streamIdPrefix;

    public EntityStreamConfigBuilder(Type entityType)
    {
        _entityType = entityType;
        _streamIdPrefix = DefaultStreamIdPrefix.CreateFor(entityType);
    }

    public EntityStreamConfigBuilder SetSnapshotEventCount(int? eventCount)
    {
        _snapshotEventCount = eventCount;
        return this;
    }

    public EntityStreamConfigBuilder SetStreamIdPrefix(string streamIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(streamIdPrefix))
            throw new ArgumentException("Stream ID prefix cannot be null or whitespace.", nameof(streamIdPrefix));

        _streamIdPrefix = streamIdPrefix;
        return this;
    }

    public EntityStreamConfig Build() => new(_entityType, _snapshotEventCount, _streamIdPrefix);
}