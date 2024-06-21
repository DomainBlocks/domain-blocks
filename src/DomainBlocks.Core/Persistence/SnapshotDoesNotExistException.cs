namespace DomainBlocks.Core.Persistence;

public class SnapshotDoesNotExistException : Exception
{
    public SnapshotDoesNotExistException(string snapshotKey) :
        this(snapshotKey, $"A snapshot with key '{snapshotKey}' does not exist")
    {
        SnapshotKey = snapshotKey;
    }

    public SnapshotDoesNotExistException(string snapshotKey, string message) : base(message)
    {
        SnapshotKey = snapshotKey;
    }

    public string? SnapshotKey { get; }
}