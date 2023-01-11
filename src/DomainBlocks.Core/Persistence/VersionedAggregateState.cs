namespace DomainBlocks.Core.Persistence;

public static class VersionedAggregateState
{
    public static VersionedAggregateState<T> Create<T>(T aggregateState, long version)
    {
        return new VersionedAggregateState<T>(aggregateState, version);
    }
}

public sealed class VersionedAggregateState<T>
{
    public VersionedAggregateState(T aggregateState, long version)
    {
        AggregateState = aggregateState;
        Version = version;
    }

    public T AggregateState { get; }
    public long Version { get; }
}