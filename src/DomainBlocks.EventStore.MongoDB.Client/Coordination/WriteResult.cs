namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed record WriteResult(long StartPosition, long NextPosition)
{
    public long EndPosition => StartPosition + PositionCount - 1;

    public long PositionCount => NextPosition - StartPosition;

    public bool IsEmpty => PositionCount == 0;
}