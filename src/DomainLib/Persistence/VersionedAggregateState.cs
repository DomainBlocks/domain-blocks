namespace DomainLib.Persistence
{
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
}