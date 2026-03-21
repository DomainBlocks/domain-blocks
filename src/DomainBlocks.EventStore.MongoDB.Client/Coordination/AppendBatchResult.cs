namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public readonly record struct AppendBatchResult(long StartPosition, long NextPosition)
{
    public long PositionCount => NextPosition - StartPosition;
    public bool IsEmpty => PositionCount == 0;
}