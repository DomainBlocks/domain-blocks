namespace DomainBlocks.Persistence
{
    public static class LoadedAggregateState
    {
        public static LoadedAggregateState<T> Create<T>(T aggregateState, long version, long? snapshotVersion, long eventsLoaded)
        {
            return new LoadedAggregateState<T>(aggregateState, version, snapshotVersion, eventsLoaded);
        }
    }

    public sealed class LoadedAggregateState<T>
    {
        public LoadedAggregateState(T aggregateState, long version, long? snapshotVersion, long eventsLoadedCount)
        {
            AggregateState = aggregateState;
            Version = version;
            SnapshotVersion = snapshotVersion;
            EventsLoadedCount = eventsLoadedCount;
        }

        public T AggregateState { get; }
        public long Version { get; }
        public long? SnapshotVersion { get; }
        public long EventsLoadedCount { get; }
    }
}