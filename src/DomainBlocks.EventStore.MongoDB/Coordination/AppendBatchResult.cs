namespace DomainBlocks.EventStore.MongoDB.Coordination;

public readonly record struct AppendBatchResult(long StartPosition, long NextPosition)
{
    public long EndPosition => StartPosition + Count - 1;

    public long Count => NextPosition - StartPosition;

    public bool IsEmpty => Count == 0;
}