namespace DomainBlocks.Core.Persistence;

public sealed class Snapshot<T>
{
    public Snapshot(T snapshotState, long version)
    {
        SnapshotState = snapshotState;
        Version = version;
    }

    public T SnapshotState { get; }
    public long Version { get; }
}