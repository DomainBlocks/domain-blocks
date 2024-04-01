namespace DomainBlocks.V1.Persistence.Builders;

public class EntityStreamConfigBuilder
{
    private readonly Type _entityType;
    private string _streamNamePrefix;

    public EntityStreamConfigBuilder(Type entityType)
    {
        _entityType = entityType;
        _streamNamePrefix = DefaultStreamNamePrefix.CreateFor(entityType);
    }

    public EntityStreamConfigBuilder SetStreamNamePrefix(string streamNamePrefix)
    {
        if (string.IsNullOrWhiteSpace(streamNamePrefix))
            throw new ArgumentException("Stream name prefix cannot be null or whitespace.", nameof(streamNamePrefix));

        _streamNamePrefix = streamNamePrefix;
        return this;
    }

    public EntityStreamConfig Build() => new(_entityType, _streamNamePrefix);
}